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

    // ─── AI extraction pipeline ───

    private async Task ProcessPendingTasksAsync(string projectName, string connString, Dictionary<int, string> taskMap)
    {
        await AiFolderProcessorHelper.ProcessBatchTasksAsync(
            _env, Cli, _logger, projectName, connString, taskMap);
    }

    public async Task ProcessSingleTaskAsync(string projectName, string connString, int taskId, string relativeFilePath)
    {
        await AiFolderProcessorHelper.ProcessSingleTaskAsync(
            _env, Cli, _logger, projectName, connString, taskId, relativeFilePath);
    }
}

public class AiFolderProcessorTaskPayload
{
    public string ProjectName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public int TaskId { get; set; }
    public string RelativeFilePath { get; set; } = string.Empty;
}
