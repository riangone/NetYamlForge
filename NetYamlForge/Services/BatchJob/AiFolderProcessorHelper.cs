using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// AiFolderProcessorExecutor の共通文書処理ロジックを分離したヘルパークラス。
/// ProcessPendingTasksAsync と ProcessSingleTaskAsync の共通処理を担当します。
/// </summary>
public static class AiFolderProcessorHelper
{
    /// <summary>
    /// 文書処理タスクを単一処理します。
    /// </summary>
    public static async Task ProcessSingleTaskAsync(
        IWebHostEnvironment env,
        ICliChainService cli,
        ILogger logger,
        string projectName,
        string connString,
        int taskId,
        string relativeFilePath)
    {
        logger.LogInformation("AI folder processor: starting extraction for task {TaskId}, file {File}", taskId, relativeFilePath);
        try
        {
            using (var conn = CreateConnection(connString))
            {
                await SqliteWriteGate.RunAsync(conn, () => conn.ExecuteAsync(
                    "UPDATE DocumentTask SET Status = 'processing' WHERE Id = @Id",
                    new { Id = taskId }));
            }

            var filePath = relativeFilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                logger.LogWarning("Task {Id}: FilePath is empty, skipping", taskId);
                return;
            }

            var absolutePath = Path.Combine(env.WebRootPath, filePath.TrimStart('/'));
            if (!File.Exists(absolutePath))
                throw new FileNotFoundException("Physical file not found: " + absolutePath);

            var prompt = BuildExtractionPrompt(absolutePath);
            var chainResult = await cli.PromptAsync(prompt, projectName: projectName);
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
            var uploadsFolder = Path.Combine(env.WebRootPath, "uploads", "ai-doc-processor");
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

                logger.LogInformation("Task {TaskId}: extraction complete, type={Type}", taskId, extractionResult.DocumentType);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Task {TaskId}: extraction failed", taskId);
            try
            {
                using var conn = CreateConnection(connString);
                await SqliteWriteGate.RunAsync(conn, () => conn.ExecuteAsync(
                    "UPDATE DocumentTask SET Status = 'failed' WHERE Id = @Id",
                    new { Id = taskId }));
            }
            catch (Exception dbEx)
            {
                logger.LogError(dbEx, "Task {TaskId}: failed to mark as failed", taskId);
            }
            throw;
        }
    }

    /// <summary>
    /// 複数の文書処理タスクを一括処理します。
    /// </summary>
    public static async Task<Dictionary<int, string>> ProcessBatchTasksAsync(
        IWebHostEnvironment env,
        ICliChainService cli,
        ILogger logger,
        string projectName,
        string connString,
        Dictionary<int, string> taskMap)
    {
        logger.LogInformation("AI folder processor: starting extraction for {Count} task(s)", taskMap.Count);

        var processedTasks = new Dictionary<int, string>();

        foreach (var (taskId, relativeFilePath) in taskMap)
        {
            try
            {
                await ProcessSingleTaskAsync(env, cli, logger, projectName, connString, taskId, relativeFilePath);
                processedTasks[taskId] = relativeFilePath;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Task {TaskId}: extraction failed", taskId);
            }
        }

        return processedTasks;
    }

    /// <summary>
    /// テーブルを作成し、必要なカラムを追加します。
    /// </summary>
    public static async Task EnsureTableAndColumnsAsync(
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

    /// <summary>
    /// INSERT用のパラメータを構築します。
    /// </summary>
    public static (List<string> paramNames, DynamicParameters parameters) BuildInsertParams(
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

    /// <summary>
    /// AI抽取用プロンプトを構築します。
    /// </summary>
    public static string BuildExtractionPrompt(string absolutePath) => $@"
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

    /// <summary>
    /// JSONレスポンスをクリーニングします。
    /// </summary>
    public static string CleanJson(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return "";
        var cleaned = System.Text.RegularExpressions.Regex.Replace(responseText, @"```(?:json)?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"```\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        return start >= 0 && end > start ? cleaned[start..(end + 1)] : cleaned;
    }

    /// <summary>
    /// データベース接続を作成します。
    /// </summary>
    public static IDbConnection CreateConnection(string connString)
    {
        var conn = (IDbConnection)Activator.CreateInstance(typeof(Microsoft.Data.Sqlite.SqliteConnection), connString)!;
        conn.Open();
        SqliteConnectionHardening.Apply(conn);
        return conn;
    }

    /// <summary>
    /// AI抽取結果DTO
    /// </summary>
    internal class AiExtractionResultDto
    {
        public string DocumentType { get; set; } = "";
        public string document_type { get => DocumentType; set => DocumentType = value; }
        public Dictionary<string, object> Data { get; set; } = new();
    }
}
