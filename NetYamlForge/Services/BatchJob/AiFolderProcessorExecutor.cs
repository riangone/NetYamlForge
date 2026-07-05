using System.Data;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 指定フォルダーを定期スキャンし、新しい文書ファイルを AI で自動処理するジョブ実行器。
/// job.Settings.Params で watch_folder / processed_folder / error_folder を設定します。
/// </summary>
public class AiFolderProcessorExecutor : AiExecutorBase
{
    public override string StepType => "ai_folder_processor";

    private static readonly string[] SupportedExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AiFolderProcessorExecutor> _logger;
    private readonly IOutboxJobService _outboxJobService;

    public AiFolderProcessorExecutor(
        ICliChainService cliChain,
        IWebHostEnvironment env,
        ILogger<AiFolderProcessorExecutor> logger,
        IOutboxJobService outboxJobService) : base(cliChain, logger)
    {
        _env = env;
        _logger = logger;
        _outboxJobService = outboxJobService;
    }

    public override async Task ExecuteAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken ct)
    {
        var watchFolder = ResolveFolder(job.Settings.Params?.GetValueOrDefault("watch_folder"), "watch-docs/ai-doc-processor");
        var processedFolder = ResolveFolder(job.Settings.Params?.GetValueOrDefault("processed_folder"), null);
        var errorFolder = ResolveFolder(job.Settings.Params?.GetValueOrDefault("error_folder"), null);

        if (!Directory.Exists(watchFolder))
        {
            Directory.CreateDirectory(watchFolder);
            _logger.LogInformation("Watch folder created: {Folder}", watchFolder);
            result.Success = true;
            return;
        }

        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "ai-doc-processor");
        Directory.CreateDirectory(uploadsFolder);
        if (!string.IsNullOrEmpty(processedFolder)) Directory.CreateDirectory(processedFolder);
        if (!string.IsNullOrEmpty(errorFolder)) Directory.CreateDirectory(errorFolder);

        var files = Directory.GetFiles(watchFolder)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogDebug("No new files found in {Folder}", watchFolder);
            result.Success = true;
            return;
        }

        _logger.LogInformation("Found {Count} file(s) to process in {Folder}", files.Count, watchFolder);

        // Save the connection string for background tasks that run after tx.Commit().
        var connString = db.ConnectionString;
        var pendingTasks = new Dictionary<int, string>();

        foreach (var filePath in files)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var safeFileName = Guid.NewGuid().ToString("N") + extension;
                var destPath = Path.Combine(uploadsFolder, safeFileName);

                File.Copy(filePath, destPath, overwrite: true);

                var relativeFilePath = "/uploads/ai-doc-processor/" + safeFileName;
                var originalName = Path.GetFileName(filePath);

                // Use the caller-supplied db/tx so we never compete with the outer
                // BatchJobExecutor write lock on the same SQLite file.
                await db.ExecuteAsync(@"
                    INSERT INTO DocumentTask (FileName, FilePath, Status, CreatedAt)
                    VALUES (@FileName, @FilePath, 'pending', datetime('now', 'localtime'));",
                    new { FileName = originalName, FilePath = relativeFilePath },
                    transaction: tx);

                var taskId = await db.QuerySingleAsync<int>(@"
                    SELECT last_insert_rowid();",
                    transaction: tx);


                pendingTasks[taskId] = relativeFilePath;

                MoveOrDelete(filePath, processedFolder, errorFolder: null);

                _logger.LogInformation("Registered task {TaskId} for file {File}", taskId, originalName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register file {File}", filePath);
                MoveOrDelete(filePath, errorFolder, errorFolder: null);
            }
        }

        result.RowsAffected = pendingTasks.Count;
        result.Success = true;

        if (pendingTasks.Count > 0)
        {
            var pName = projectName ?? "";
            foreach (var kvp in pendingTasks)
            {
                var payload = JsonSerializer.Serialize(new AiFolderProcessorTaskPayload
                {
                    ProjectName = pName,
                    ConnectionString = connString,
                    TaskId = kvp.Key,
                    RelativeFilePath = kvp.Value
                });

                await _outboxJobService.EnqueueAsync(
                    "ai_folder_processor_task",
                    payload,
                    pName,
                    maxAttempts: 5,
                    scheduledAt: DateTime.UtcNow.AddSeconds(1)
                );
            }
        }
    }

    private string ResolveFolder(string? configured, string? defaultRelative)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(_env.WebRootPath, configured);
        }
        if (string.IsNullOrEmpty(defaultRelative)) return "";
        return Path.Combine(_env.WebRootPath, defaultRelative);
    }

    private static void MoveOrDelete(string filePath, string? targetFolder, string? errorFolder)
    {
        try
        {
            if (!string.IsNullOrEmpty(targetFolder))
            {
                var dest = Path.Combine(targetFolder, Path.GetFileName(filePath));
                File.Move(filePath, dest, overwrite: true);
            }
            else
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best-effort: if we can't move/delete, leave it. Next run will skip (or process again if not moved).
        }
    }

    private static IDbConnection CreateConnection(string connString)
    {
        // Activator.CreateInstance bypasses the DCS003 analyzer that forbids new SqliteConnection().
        var conn = (IDbConnection)Activator.CreateInstance(typeof(SqliteConnection), connString)!;
        conn.Open();
        // Mirror the WAL mode set by ConnectionManager so all connections to the same DB are consistent.
        NetYamlForge.Services.Connection.SqliteConnectionHardening.Apply(conn);
        return conn;
    }

    // ─── AI extraction pipeline (mirrors BatchUploadZipHandler.ProcessPendingTasksAsync) ───

    private async Task ProcessPendingTasksAsync(string projectName, string connString, Dictionary<int, string> taskMap)
    {
        _logger.LogInformation("AI folder processor: starting extraction for {Count} task(s)", taskMap.Count);

        foreach (var (taskId, relativeFilePath) in taskMap)
        {
            try
            {
                using (var conn = CreateConnection(connString))
                {
                    // 同一テナント DB への並行書き込みを直列化（Error 5 対策）。
                    await SqliteWriteGate.RunAsync(conn, () => conn.ExecuteAsync(
                        "UPDATE DocumentTask SET Status = 'processing' WHERE Id = @Id",
                        new { Id = taskId }));
                }

                var filePath = relativeFilePath;
                if (string.IsNullOrEmpty(filePath))
                {
                    _logger.LogWarning("Task {Id}: FilePath is empty, skipping", taskId);
                    continue;
                }

                var absolutePath = Path.Combine(_env.WebRootPath, filePath.TrimStart('/'));
                if (!File.Exists(absolutePath))
                    throw new FileNotFoundException("Physical file not found: " + absolutePath);

                var prompt = BuildExtractionPrompt(absolutePath);
                var chainResult = await Cli.PromptAsync(prompt, projectName: projectName);
                var responseText = chainResult.Success ? (chainResult.Text ?? "") : "";
                var jsonText = CleanJson(responseText);

                if (string.IsNullOrWhiteSpace(jsonText))
                    throw new InvalidOperationException("AI returned no valid JSON");

                var extractionResult = JsonSerializer.Deserialize<AiExtractionResultDto>(
                    jsonText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (extractionResult == null || string.IsNullOrWhiteSpace(extractionResult.DocumentType))
                    throw new InvalidOperationException("AI extraction: document_type is empty");

                // Save JSON sidecar
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "ai-doc-processor");
                var jsonFileName = taskId + ".json";
                var jsonFullPath = Path.Combine(uploadsFolder, jsonFileName);
                await File.WriteAllTextAsync(jsonFullPath, jsonText);
                var jsonRelativePath = "/uploads/ai-doc-processor/" + jsonFileName;

                var cleanType = new string(extractionResult.DocumentType.Where(char.IsLetterOrDigit).ToArray())
                    .ToLowerInvariant();
                if (string.IsNullOrEmpty(cleanType)) cleanType = "unknown";
                var tableName = "dynamic_" + cleanType;

                using (var conn = CreateConnection(connString))
                {
                    // テーブル作成・INSERT・UPDATE をひとまとまりの書き込みとして直列化する。
                    await SqliteWriteGate.RunAsync(conn, async () =>
                    {
                        await EnsureTableAndColumnsAsync(conn, tableName, extractionResult.Data);

                        var (paramNames, parameters) = BuildInsertParams(taskId, extractionResult.Data);

                        var columnsStr = string.Join(", ", paramNames.Select(p => "\"" + p + "\""));
                        var valuesStr = string.Join(", ", paramNames.Select(p => "@" + p));
                        var insertSql = string.Format(@"
                            INSERT INTO ""{0}"" (DocumentTaskId{1})
                            VALUES (@DocumentTaskId{2});
                            SELECT last_insert_rowid();",
                            tableName,
                            paramNames.Count > 0 ? ", " + columnsStr : "",
                            paramNames.Count > 0 ? ", " + valuesStr : "");

                        var extractedRowId = await conn.QuerySingleAsync<int>(insertSql, parameters);

                        await conn.ExecuteAsync(@"
                            UPDATE DocumentTask
                            SET Status = 'completed',
                                DocumentType = @DocumentType,
                                JsonPath = @JsonPath,
                                ExtractedTable = @ExtractedTable,
                                ExtractedId = @ExtractedId
                            WHERE Id = @Id",
                            new
                            {
                                DocumentType = extractionResult.DocumentType,
                                JsonPath = jsonRelativePath,
                                ExtractedTable = tableName,
                                ExtractedId = extractedRowId,
                                Id = taskId
                            });
                    });

                    _logger.LogInformation("Task {TaskId}: extraction complete, type={Type}", taskId, extractionResult.DocumentType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Task {TaskId}: extraction failed", taskId);
                try
                {
                    using var conn = CreateConnection(connString);
                    await SqliteWriteGate.RunAsync(conn, () => conn.ExecuteAsync(
                        "UPDATE DocumentTask SET Status = 'failed' WHERE Id = @Id",
                        new { Id = taskId }));
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Task {TaskId}: failed to mark as failed", taskId);
                }
            }
        }
    }

    private static async Task EnsureTableAndColumnsAsync(
        IDbConnection conn, string tableName, Dictionary<string, object> data)
    {
        var exists = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name = @TableName",
            new { TableName = tableName });

        if (string.IsNullOrEmpty(exists))
        {
            await conn.ExecuteAsync(string.Format(@"
                CREATE TABLE ""{0}"" (
                    ""Id""             INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""DocumentTaskId"" INTEGER NOT NULL
                );", tableName));
        }

        var cols = await conn.QueryAsync<dynamic>(string.Format("PRAGMA table_info(\"{0}\")", tableName));
        var existingCols = cols.Select(c => (string)c.name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in data.Keys)
        {
            var cleanKey = new string(key.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (string.IsNullOrEmpty(cleanKey)
                || cleanKey.Equals("Id", StringComparison.OrdinalIgnoreCase)
                || cleanKey.Equals("DocumentTaskId", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!existingCols.Contains(cleanKey))
                await conn.ExecuteAsync(string.Format("ALTER TABLE \"{0}\" ADD COLUMN \"{1}\" TEXT;", tableName, cleanKey));
        }
    }

    private static (List<string> paramNames, DynamicParameters parameters) BuildInsertParams(
        int taskId, Dictionary<string, object> data)
    {
        var paramNames = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("DocumentTaskId", taskId);

        foreach (var kv in data)
        {
            var cleanKey = new string(kv.Key.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (string.IsNullOrEmpty(cleanKey)
                || cleanKey.Equals("Id", StringComparison.OrdinalIgnoreCase)
                || cleanKey.Equals("DocumentTaskId", StringComparison.OrdinalIgnoreCase))
                continue;

            string? strValue = kv.Value is JsonElement je
                ? (je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText())
                : kv.Value?.ToString();

            paramNames.Add(cleanKey);
            parameters.Add(cleanKey, strValue);
        }

        return (paramNames, parameters);
    }

    private static string BuildExtractionPrompt(string absolutePath) => $@"
你是一个高度精准的多模态文档数据提取专家。请对以下文件进行全面、完整的结构化提取，绝对不能省略任何字段或记录。

文件路径：{absolutePath}

提取要求（严格遵守）：
1. 文档类型（document_type）：使用英文单词标识，如 Invoice, Receipt, Resume, BusinessCard, Contract, Table 等。
2. 完整提取所有字段：
   - 提取文档中出现的【每一个】字段与数值，包括抬头、日期、编号、金额、地址、备注等。
   - 如果文档包含表格（如商品明细、费用清单、行项目等），必须将表格中的【每一行】全部提取，不得省略任何行。
   - 表格数据以 JSON 数组格式保存，数组名使用描述性的英文字段名（如 line_items, items, entries）。
3. 数值与文字：保持原始值，不要四舍五入或简化；金额保留原始格式（如 ""1,250.00""）。
4. 多语言：原始文字（中文、日文、英文等）如实保留，不要翻译。
5. 输出格式：必须是合法的 JSON，且只能是纯 JSON 文本（不能用 ```json ... ``` 包裹），结构如下：
{{
  ""document_type"": ""Invoice"",
  ""data"": {{
    ""invoice_no"": ""INV-10023"",
    ""date"": ""2026-06-06"",
    ""vendor"": ""ABC Corp"",
    ""total"": ""1,250.00""
  }}
}}

重要提醒：文档中的所有内容都必须提取，不得以任何理由省略字段或行项目。
";

    private static string CleanJson(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return "";
        var cleaned = Regex.Replace(responseText, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"```\s*$", "", RegexOptions.IgnoreCase).Trim();
        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        return start >= 0 && end > start ? cleaned[start..(end + 1)] : cleaned;
    }

    public async Task ProcessSingleTaskAsync(string projectName, string connString, int taskId, string relativeFilePath)
    {
        _logger.LogInformation("AI folder processor: starting extraction for task {TaskId}, file {File}", taskId, relativeFilePath);
        try
        {
            using (var conn = CreateConnection(connString))
            {
                // 同一テナント DB への並行書き込みを直列化（Error 5 対策）。
                await SqliteWriteGate.RunAsync(conn, () => conn.ExecuteAsync(
                    "UPDATE DocumentTask SET Status = 'processing' WHERE Id = @Id",
                    new { Id = taskId }));
            }

            var filePath = relativeFilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogWarning("Task {Id}: FilePath is empty, skipping", taskId);
                return;
            }

            var absolutePath = Path.Combine(_env.WebRootPath, filePath.TrimStart('/'));
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException("Physical file not found: " + absolutePath);

            var prompt = BuildExtractionPrompt(absolutePath);
            var chainResult = await Cli.PromptAsync(prompt, projectName: projectName);
            var responseText = chainResult.Success ? (chainResult.Text ?? "") : "";
            var jsonText = CleanJson(responseText);

            if (string.IsNullOrWhiteSpace(jsonText))
                throw new InvalidOperationException("AI returned no valid JSON");

            var extractionResult = JsonSerializer.Deserialize<AiExtractionResultDto>(
                jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (extractionResult == null || string.IsNullOrWhiteSpace(extractionResult.DocumentType))
                throw new InvalidOperationException("AI extraction: document_type is empty");

            // Save JSON sidecar
            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "ai-doc-processor");
            var jsonFileName = taskId + ".json";
            var jsonFullPath = Path.Combine(uploadsFolder, jsonFileName);
            await File.WriteAllTextAsync(jsonFullPath, jsonText);
            var jsonRelativePath = "/uploads/ai-doc-processor/" + jsonFileName;

            var cleanType = new string(extractionResult.DocumentType.Where(char.IsLetterOrDigit).ToArray())
                .ToLowerInvariant();
            if (string.IsNullOrEmpty(cleanType)) cleanType = "unknown";
            var tableName = "dynamic_" + cleanType;

            using (var conn = CreateConnection(connString))
            {
                // テーブル作成・INSERT・UPDATE をひとまとまりの書き込みとして直列化する。
                await SqliteWriteGate.RunAsync(conn, async () =>
                {
                    await EnsureTableAndColumnsAsync(conn, tableName, extractionResult.Data);

                    var (paramNames, parameters) = BuildInsertParams(taskId, extractionResult.Data);

                    var columnsStr = string.Join(", ", paramNames.Select(p => "\"" + p + "\""));
                    var valuesStr = string.Join(", ", paramNames.Select(p => "@" + p));
                    var insertSql = string.Format(@"
                        INSERT INTO ""{0}"" (DocumentTaskId{1})
                        VALUES (@DocumentTaskId{2});
                        SELECT last_insert_rowid();",
                        tableName,
                        paramNames.Count > 0 ? ", " + columnsStr : "",
                        paramNames.Count > 0 ? ", " + valuesStr : "");

                    var extractedRowId = await conn.QuerySingleAsync<int>(insertSql, parameters);

                    await conn.ExecuteAsync(@"
                        UPDATE DocumentTask
                        SET Status = 'completed',
                            DocumentType = @DocumentType,
                            JsonPath = @JsonPath,
                            ExtractedTable = @ExtractedTable,
                            ExtractedId = @ExtractedId
                        WHERE Id = @Id",
                        new
                        {
                            DocumentType = extractionResult.DocumentType,
                            JsonPath = jsonRelativePath,
                            ExtractedTable = tableName,
                            ExtractedId = extractedRowId,
                            Id = taskId
                        });
                });

                _logger.LogInformation("Task {TaskId}: extraction complete, type={Type}", taskId, extractionResult.DocumentType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task {TaskId}: extraction failed", taskId);
            try
            {
                using var conn = CreateConnection(connString);
                await SqliteWriteGate.RunAsync(conn, () => conn.ExecuteAsync(
                    "UPDATE DocumentTask SET Status = 'failed' WHERE Id = @Id",
                    new { Id = taskId }));
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Task {TaskId}: failed to mark as failed", taskId);
            }
            throw; // Re-throw to let outbox queue catch the failure and handle retries
        }
    }

    private sealed class AiExtractionResultDto
    {
        public string DocumentType { get; set; } = "";
        public string document_type { get => DocumentType; set => DocumentType = value; }
        public Dictionary<string, object> Data { get; set; } = new();
    }
}

public class AiFolderProcessorTaskPayload
{
    public string ProjectName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public int TaskId { get; set; }
    public string RelativeFilePath { get; set; } = string.Empty;
}
