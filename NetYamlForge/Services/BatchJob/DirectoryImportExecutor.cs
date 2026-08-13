using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// ディレクトリから写真を一括インポートするジョブ実行器
/// </summary>
public class DirectoryImportExecutor : IBatchStepHandler
{
    public string StepType => "directory_import";
    private readonly ProjectManager _projectManager;
    private readonly IWebHostEnvironment _env;
    private readonly IBatchJobScheduler _scheduler;
    private readonly ILogger<DirectoryImportExecutor> _logger;

    public DirectoryImportExecutor(
        ProjectManager projectManager,
        IWebHostEnvironment env,
        IBatchJobScheduler scheduler,
        ILogger<DirectoryImportExecutor> logger)
    {
        _projectManager = projectManager;
        _env = env;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var r = await ExecuteAsync(job, projectName ?? "", db, tx, ct);
        result.Success = r.Success;
        result.RowsAffected = r.RowsAffected;
        result.ErrorMessage = r.ErrorMessage;
        result.ErrorDetail = r.ErrorDetail;
    }

    public async Task<BatchJobResult> ExecuteAsync(
        BatchJobDefinition job,
        string projectName,
        IDbConnection db,
        IDbTransaction tx,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult
        {
            JobId = job.Id,
            StartedAt = DateTime.UtcNow
        };

        // 租户项目根目录解析
        string baseDir;
        if (!string.IsNullOrEmpty(projectName) && _projectManager.TryGet(projectName, out var projectInfo) && projectInfo != null)
        {
            baseDir = projectInfo.ProjectDir;
        }
        else
        {
            // ⚠️ システムレベル Job（projectName 未指定）はテナント境界外の専用ディレクトリへ隔離する
            baseDir = PathSafetyGuard.GetSystemJobBaseDir(_env.ContentRootPath);
        }

        try
        {
            _logger.LogInformation("Directory Import Job started: {JobId} for Project: {Project}", job.Id, projectName);

            var sql = await GetSqlAsync(job, baseDir);
            var items = await db.QueryAsync<ImportJobRow>(sql, transaction: tx);
            var jobRow = items.FirstOrDefault();

            if (jobRow == null)
            {
                _logger.LogInformation("No pending directory import jobs found.");
                result.Success = true;
                result.RowsAffected = 0;
                result.EndedAt = DateTime.UtcNow;
                return result;
            }

            _logger.LogInformation("Processing import job: {JobId}, path: {Path}", jobRow.job_id, jobRow.source_path);

            if (string.IsNullOrEmpty(jobRow.source_path) || !Directory.Exists(jobRow.source_path))
            {
                var errorMsg = $"Directory not found or empty path: '{jobRow.source_path}'";
                _logger.LogWarning(errorMsg);

                await db.ExecuteAsync(
                    "UPDATE import_jobs SET status = @Status, error_message = @Error, completed_at = @CompletedAt WHERE job_id = @JobId",
                    new { Status = job.Behavior.UpdateStatusOnError, Error = errorMsg, CompletedAt = DateTime.UtcNow, JobId = jobRow.job_id },
                    transaction: tx
                );

                result.Success = false;
                result.ErrorMessage = errorMsg;
                result.EndedAt = DateTime.UtcNow;
                return result;
            }

            // 1. 更新状態为 scanning
            await db.ExecuteAsync(
                "UPDATE import_jobs SET status = @Status, started_at = @StartedAt WHERE job_id = @JobId",
                new { Status = job.Behavior.UpdateStatusOnStart, StartedAt = DateTime.UtcNow, JobId = jobRow.job_id },
                transaction: tx
            );

            var filterPattern = string.IsNullOrEmpty(jobRow.file_filter)
                ? "*.jpg,*.jpeg,*.png,*.heic,*.dng,*.cr3,*.webp"
                : jobRow.file_filter;

            var filters = filterPattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var allFiles = new List<string>();
            var recursiveOpt = jobRow.recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var filter in filters)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    var files = Directory.GetFiles(jobRow.source_path, filter, recursiveOpt);
                    allFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to scan files with pattern {Pattern} in {Path}", filter, jobRow.source_path);
                }
            }

            var uniqueFiles = allFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var filesFound = uniqueFiles.Count;

            _logger.LogInformation("Found {Count} files in directory {Path}", filesFound, jobRow.source_path);

            // 2. 更新状態为 importing
            await db.ExecuteAsync(
                "UPDATE import_jobs SET status = 'importing', files_found = @FilesFound WHERE job_id = @JobId",
                new { FilesFound = filesFound, JobId = jobRow.job_id },
                transaction: tx
            );

            var filesImported = 0;
            var filesSkipped = 0;
            var filesFailed = 0;

            foreach (var filePath in uniqueFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists)
                    {
                        filesFailed++;
                        continue;
                    }

                    // 排重
                    if (job.Behavior.DeduplicateByPath)
                    {
                        var exists = await db.QueryFirstOrDefaultAsync<int>(
                            "SELECT COUNT(*) FROM photos WHERE file_path = @FilePath AND deleted_at IS NULL",
                            new { FilePath = filePath },
                            transaction: tx
                        );
                        if (exists > 0)
                        {
                            filesSkipped++;
                            continue;
                        }
                    }

                    var photoId = Guid.NewGuid().ToString("N");
                    var fileName = fileInfo.Name;
                    var fileSize = fileInfo.Length;
                    var fileFormat = fileInfo.Extension.TrimStart('.').ToUpper();
                    var now = DateTime.UtcNow;
                    var takenAt = fileInfo.LastWriteTimeUtc;

                    var sqlInsertPhoto = @"
                        INSERT INTO photos (
                            photo_id, file_name, file_path, file_size, file_format,
                            taken_at, album_id, annotation_status, created_at, updated_at
                        ) VALUES (
                            @PhotoId, @FileName, @FilePath, @FileSize, @FileFormat,
                            @TakenAt, @AlbumId, 'pending', @Now, @Now
                        )";

                    await db.ExecuteAsync(sqlInsertPhoto, new
                    {
                        PhotoId = photoId,
                        FileName = fileName,
                        FilePath = filePath,
                        FileSize = fileSize,
                        FileFormat = fileFormat,
                        TakenAt = takenAt,
                        AlbumId = string.IsNullOrEmpty(jobRow.album_id) ? null : jobRow.album_id,
                        Now = now
                    }, transaction: tx);

                    if (job.Behavior.AutoEnqueueAnnotation)
                    {
                        var sqlInsertQueue = @"
                            INSERT INTO processing_queue (
                                photo_id, file_path, status, provider, priority, retry_count, queued_at
                            ) VALUES (
                                @PhotoId, @FilePath, 'queued', @Provider, 1, 0, @Now
                            )";

                        await db.ExecuteAsync(sqlInsertQueue, new
                        {
                            PhotoId = photoId,
                            FilePath = filePath,
                            Provider = string.IsNullOrEmpty(jobRow.ai_provider) ? "antigravity" : jobRow.ai_provider,
                            Now = now
                        }, transaction: tx);
                    }

                    filesImported++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importing file {Path}", filePath);
                    filesFailed++;
                }
            }

            var statusStr = (filesFailed > 0 && filesImported == 0) ? job.Behavior.UpdateStatusOnError : job.Behavior.UpdateStatusOnDone;
            var finalErrorMsg = filesFailed > 0 ? $"{filesFailed} files failed to import." : null;

            await db.ExecuteAsync(
                @"UPDATE import_jobs SET 
                    status = @Status, 
                    files_imported = @FilesImported, 
                    files_skipped = @FilesSkipped, 
                    files_failed = @FilesFailed, 
                    error_message = @ErrorMessage,
                    completed_at = @CompletedAt 
                  WHERE job_id = @JobId",
                new
                {
                    Status = statusStr,
                    FilesImported = filesImported,
                    FilesSkipped = filesSkipped,
                    FilesFailed = filesFailed,
                    ErrorMessage = finalErrorMsg,
                    CompletedAt = DateTime.UtcNow,
                    JobId = jobRow.job_id
                },
                transaction: tx
            );

            result.Success = true;
            result.RowsAffected = filesImported;
            result.EndedAt = DateTime.UtcNow;
            _logger.LogInformation("Import job completed: {JobId}. Imported: {Imported}, Skipped: {Skipped}, Failed: {Failed}",
                jobRow.job_id, filesImported, filesSkipped, filesFailed);

            if (filesImported > 0 && job.Behavior.AutoEnqueueAnnotation && !string.IsNullOrEmpty(projectName))
            {
                _ = _scheduler.TriggerJobNowAsync(projectName, "antigravity_cli_worker");
                _logger.LogInformation("Triggered antigravity_cli_worker immediately after import of {Count} photos", filesImported);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during directory import job execution: {JobId}", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorDetail = ex.ToString();
            result.EndedAt = DateTime.UtcNow;
        }

        return result;
    }

    private async Task<string> GetSqlAsync(BatchJobDefinition job, string baseDir)
    {
        if (!string.IsNullOrEmpty(job.Settings.SqlQuery)) return job.Settings.SqlQuery;
        if (!string.IsNullOrEmpty(job.Settings.SqlFile))
        {
            var safeSqlFile = PathSafetyGuard.NormalizeAndValidatePath(job.Settings.SqlFile, baseDir, "SqlFile");
            if (File.Exists(safeSqlFile))
            {
                return await File.ReadAllTextAsync(safeSqlFile);
            }
        }
        throw new InvalidOperationException("SQL file or query is not specified.");
    }

    private class ImportJobRow
    {
        public string job_id { get; set; } = "";
        public string job_type { get; set; } = "";
        public string source_path { get; set; } = "";
        public bool recursive { get; set; }
        public string file_filter { get; set; } = "";
        public string ai_provider { get; set; } = "";
        public string album_id { get; set; } = "";
    }
}
