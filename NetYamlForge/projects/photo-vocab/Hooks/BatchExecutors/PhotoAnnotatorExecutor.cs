using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.BatchJob.Sdk;

namespace NetYamlForge.Projects.PhotoVocab.Hooks;

/// <summary>
/// processing_queue からフォトを取り出し、Unified CLI Chain を用いて画像標注を実行する。
/// job type: photo_annotator
/// </summary>
public class PhotoAnnotatorExecutor : AiQueueStepHandlerBase<PhotoAnnotatorExecutor.QueueRow>
{
    public override string StepType => "photo_annotator";

    private readonly IWebHostEnvironment _env;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<PhotoAnnotatorExecutor> _logger;

    private const string AnnotationPrompt = """
        你是专业的图片分析AI。请仔细分析图片，仅输出合法JSON（不加任何说明、不使用markdown代码块），字段如下：
        {
          "caption_short": "25字以内的简洁中文描述",
          "caption_long": "100字以内的详细中文描述，包含画面内容、色调、氛围",
          "scene_type": "场景类别，必须是以下之一：indoor/outdoor/portrait/landscape/food/architecture/street/nature/event/abstract/vehicle/document/other",
          "subjects": "主体对象，中文逗号分隔，如：人物,建筑,动物",
          "activities": "画面中可见的活动，中文逗号分隔，无则填null",
          "tags": ["中文标签1", "中文标签2", "最多15个"],
          "person_count": 0,
          "confidence_score": 0.90
        }
        """;

    public PhotoAnnotatorExecutor(
        IWebHostEnvironment env,
        IEmbeddingService embedding,
        ICliChainService cliChain,
        ILogger<PhotoAnnotatorExecutor> logger) : base(cliChain, logger)
    {
        _env = env;
        _embedding = embedding;
        _logger = logger;
    }

    protected override async Task<IReadOnlyList<QueueRow>> FetchPendingAsync(
        BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        int batchSize, CancellationToken ct)
    {
        var provider = job.AiProvider ?? "antigravity";
        var rows = (await db.QueryAsync<QueueRow>(
            @"SELECT q.queue_id, q.photo_id, q.file_path, q.provider, q.retry_count
              FROM processing_queue q
              WHERE q.status = 'queued' AND q.provider = @Provider
              ORDER BY q.priority DESC, q.queued_at ASC
              LIMIT @Batch",
            new { Batch = batchSize, Provider = provider },
            transaction: tx)).ToList();
        return rows;
    }

    protected override async Task MarkProcessingAsync(
        QueueRow row, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
    {
        var now = DateTime.UtcNow;
        await db.ExecuteAsync(
            "UPDATE processing_queue SET status='processing', started_at=@Now WHERE queue_id=@Id",
            new { Now = now, Id = row.queue_id }, transaction: tx);
    }

    protected override async Task<RowOutcome> ProcessRowAsync(
        QueueRow row, BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        CancellationToken ct)
    {
        using var telemetry = new BatchTelemetryScope(_logger, StepType, projectName, job.Id);
        try
        {
            var now = DateTime.UtcNow;
            var absolutePath = ResolveFilePath(row.file_path);
            if (!File.Exists(absolutePath))
            {
                telemetry.RecordError($"File not found: {absolutePath}");
                return RowOutcome.Fail($"File not found: {absolutePath}");
            }

            var provider = job.AiProvider ?? "antigravity";
            var usedProvider = string.IsNullOrEmpty(row.provider) ? provider : row.provider;
            var annotation = await AnnotateAsync(absolutePath, usedProvider, projectName, ct);

            if (annotation == null)
            {
                telemetry.RecordError("AI returned empty or unparseable response");
                return RowOutcome.Fail("AI returned empty or unparseable response");
            }

            // 写回 photos 表
            await db.ExecuteAsync(@"
                UPDATE photos SET
                    caption_short      = @CaptionShort,
                    caption_long       = @CaptionLong,
                    scene_type         = @SceneType,
                    subjects           = @Subjects,
                    activities         = @Activities,
                    person_count       = @PersonCount,
                    confidence_score   = @Confidence,
                    annotation_status  = 'done',
                    annotation_model   = @Model,
                    annotation_at      = @Now,
                    updated_at         = @Now
                WHERE photo_id = @PhotoId",
                new
                {
                    CaptionShort = annotation.CaptionShort,
                    CaptionLong  = annotation.CaptionLong,
                    SceneType    = annotation.SceneType,
                    Subjects     = annotation.Subjects,
                    Activities   = annotation.Activities,
                    PersonCount  = annotation.PersonCount,
                    Confidence   = annotation.ConfidenceScore,
                    Model        = "antigravity-cli",
                    Now          = now,
                    PhotoId      = row.photo_id,
                }, transaction: tx);

            // 写回 tags（简单实现：INSERT OR IGNORE）
            if (annotation.Tags?.Length > 0)
            {
                foreach (var tag in annotation.Tags.Take(15).Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var tagId = Guid.NewGuid().ToString("N");
                    await db.ExecuteAsync(@"
                        INSERT OR IGNORE INTO tags (tag_id, name, category, created_at)
                        VALUES (@TagId, @Name, 'auto', @Now)",
                        new { TagId = tagId, Name = tag.Trim().ToLowerInvariant(), Now = now },
                        transaction: tx);

                    var resolvedTagId = await db.QueryFirstOrDefaultAsync<string>(
                        "SELECT tag_id FROM tags WHERE name = @Name",
                        new { Name = tag.Trim().ToLowerInvariant() }, transaction: tx);

                    if (resolvedTagId != null)
                    {
                        await db.ExecuteAsync(@"
                            INSERT OR IGNORE INTO photo_tags (photo_id, tag_id, tag_name, confidence)
                            VALUES (@PhotoId, @TagId, @TagName, @Conf)",
                            new { PhotoId = row.photo_id, TagId = resolvedTagId, TagName = tag.Trim().ToLowerInvariant(), Conf = annotation.ConfidenceScore },
                            transaction: tx);
                    }
                }
            }

            // 标注完成后立即生成嵌入，无需等待 cron
            try
            {
                await db.ExecuteAsync("""
                    CREATE TABLE IF NOT EXISTS photo_embeddings (
                        photo_id   TEXT NOT NULL PRIMARY KEY,
                        embedding  TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    )
                    """, transaction: tx);

                var embedText = BuildEmbedText(annotation, null, null, null);
                if (!string.IsNullOrWhiteSpace(embedText))
                {
                    var vecs = await _embedding.EmbedBatchAsync([embedText], ct);
                    var vec = vecs.Count > 0 ? vecs[0] : null;
                    if (vec != null && vec.Length > 0)
                    {
                        await db.ExecuteAsync("""
                            INSERT OR REPLACE INTO photo_embeddings(photo_id, embedding, created_at)
                            VALUES (@PhotoId, @Embedding, @Now)
                            """,
                            new { PhotoId = row.photo_id, Embedding = JsonSerializer.Serialize(vec), Now = DateTime.UtcNow.ToString("o") },
                            transaction: tx);
                        _logger.LogInformation("Embedding generated inline for photo {PhotoId} ({Dims} dims)", row.photo_id, vec.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Inline embedding failed for photo {PhotoId}, will retry via cron", row.photo_id);
            }

            var ms = (long)(DateTime.UtcNow - now).TotalMilliseconds;
            await db.ExecuteAsync(@"
                UPDATE processing_queue SET
                    status='done', completed_at=@Now, processing_ms=@Ms, provider=@Provider
                WHERE queue_id=@Id",
                new { Now = DateTime.UtcNow, Ms = ms, Provider = usedProvider, Id = row.queue_id },
                transaction: tx);

            _logger.LogInformation("Photo annotated: {PhotoId} via {Provider}", row.photo_id, usedProvider);
            return RowOutcome.Ok();
        }
        catch (Exception ex)
        {
            telemetry.RecordError(ex.Message);
            throw;
        }
    }

    protected override async Task WriteOutcomeAsync(
        QueueRow row, RowOutcome outcome, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
    {
        if (outcome.Status == RowStatus.Failed)
        {
            var error = outcome.Reason ?? "Unknown error";
            await FailRow(db, tx, row, error);
        }
    }

    private static string BuildEmbedText(AnnotationResult a, string? sceneType, string? gpsAddress, string? ocrText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(a.CaptionShort)) parts.Add(a.CaptionShort);
        if (!string.IsNullOrWhiteSpace(a.CaptionLong))  parts.Add(a.CaptionLong);
        if (!string.IsNullOrWhiteSpace(a.SceneType ?? sceneType)) parts.Add($"场景: {a.SceneType ?? sceneType}");
        if (!string.IsNullOrWhiteSpace(a.Subjects))     parts.Add($"主体: {a.Subjects}");
        if (!string.IsNullOrWhiteSpace(a.Activities))   parts.Add($"活动: {a.Activities}");
        if (!string.IsNullOrWhiteSpace(gpsAddress))     parts.Add($"地点: {gpsAddress}");
        if (!string.IsNullOrWhiteSpace(ocrText))        parts.Add($"文字: {ocrText}");
        return string.Join(". ", parts);
    }

    private async Task<AnnotationResult?> AnnotateAsync(
        string absolutePath, string provider, string? projectName, CancellationToken ct)
    {
        var cliResult = await CliInvoker.RunCliAsync(Cli, AnnotationPrompt, absolutePath, projectName, ct);
        if (!cliResult.Success || string.IsNullOrWhiteSpace(cliResult.Text))
        {
            _logger.LogWarning("Unified CLI Chain annotation failed (project: {Project})", projectName);
            return null;
        }

        if (AiResultParser.TryParseJson<AnnotationResult>(cliResult.Text, out var annotation, out var error))
        {
            return annotation;
        }

        _logger.LogWarning("Failed to parse annotation JSON: {Error}", error);
        return null;
    }

    private string ResolveFilePath(string storedPath)
    {
        if (File.Exists(storedPath)) return storedPath;
        var relative = storedPath.TrimStart('/');
        return Path.Combine(_env.WebRootPath, relative);
    }

    /// <summary>
    /// 直接标注单张照片，不经过 processing_queue，适合「立即触发」场景。
    /// </summary>
    public async Task<(bool Success, string? Error)> AnnotateSingleAsync(
        string photoId, string projectName, IDbConnection db, IDbTransaction? tx, CancellationToken ct)
    {
        var row = await db.QueryFirstOrDefaultAsync<QueueRow>(
            "SELECT photo_id, file_path FROM photos WHERE photo_id = @Id",
            new { Id = photoId }, transaction: tx);

        if (row == null) return (false, "照片不存在");

        var absolutePath = ResolveFilePath(row.file_path);
        if (!File.Exists(absolutePath))
            return (false, $"文件不存在: {absolutePath}");

        var now = DateTime.UtcNow;

        await db.ExecuteAsync(
            "UPDATE photos SET annotation_status = 'processing', updated_at = @Now WHERE photo_id = @Id",
            new { Now = now, Id = photoId }, transaction: tx);

        AnnotationResult? annotation;
        try
        {
            annotation = await AnnotateAsync(absolutePath, "antigravity", projectName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnnotateSingle failed for photo {PhotoId}", photoId);
            await db.ExecuteAsync(
                "UPDATE photos SET annotation_status = 'failed', updated_at = @Now WHERE photo_id = @Id",
                new { Now = now, Id = photoId }, transaction: tx);
            return (false, ex.Message);
        }

        if (annotation == null)
        {
            await db.ExecuteAsync(
                "UPDATE photos SET annotation_status = 'failed', updated_at = @Now WHERE photo_id = @Id",
                new { Now = now, Id = photoId }, transaction: tx);
            return (false, "AI 返回空结果");
        }

        await db.ExecuteAsync(@"
            UPDATE photos SET
                caption_short      = @CaptionShort,
                caption_long       = @CaptionLong,
                scene_type         = @SceneType,
                subjects           = @Subjects,
                activities         = @Activities,
                person_count       = @PersonCount,
                confidence_score   = @Confidence,
                annotation_status  = 'done',
                annotation_model   = @Model,
                annotation_at      = @Now,
                updated_at         = @Now
            WHERE photo_id = @PhotoId",
            new
            {
                CaptionShort = annotation.CaptionShort,
                CaptionLong  = annotation.CaptionLong,
                SceneType    = annotation.SceneType,
                Subjects     = annotation.Subjects,
                Activities   = annotation.Activities,
                PersonCount  = annotation.PersonCount,
                Confidence   = annotation.ConfidenceScore,
                Model        = "antigravity-cli",
                Now          = now,
                PhotoId      = photoId
            }, transaction: tx);

        if (annotation.Tags?.Length > 0)
        {
            foreach (var tag in annotation.Tags.Take(15).Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                var tagId = Guid.NewGuid().ToString("N");
                await db.ExecuteAsync(@"
                    INSERT OR IGNORE INTO tags (tag_id, name, category, created_at)
                    VALUES (@TagId, @Name, 'auto', @Now)",
                    new { TagId = tagId, Name = tag.Trim().ToLowerInvariant(), Now = now }, transaction: tx);

                var resolvedTagId = await db.QueryFirstOrDefaultAsync<string>(
                    "SELECT tag_id FROM tags WHERE name = @Name",
                    new { Name = tag.Trim().ToLowerInvariant() }, transaction: tx);

                if (resolvedTagId != null)
                {
                    await db.ExecuteAsync(@"
                        INSERT OR IGNORE INTO photo_tags (photo_id, tag_id, tag_name, confidence)
                        VALUES (@PhotoId, @TagId, @TagName, @Conf)",
                        new { PhotoId = photoId, TagId = resolvedTagId, TagName = tag.Trim().ToLowerInvariant(), Conf = annotation.ConfidenceScore },
                        transaction: tx);
                }
            }
        }

        try
        {
            await db.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS photo_embeddings (
                    photo_id   TEXT NOT NULL PRIMARY KEY,
                    embedding  TEXT NOT NULL,
                    created_at TEXT NOT NULL
                )
                """, transaction: tx);

            var embedText = BuildEmbedText(annotation, null, null, null);
            if (!string.IsNullOrWhiteSpace(embedText))
            {
                var vecs = await _embedding.EmbedBatchAsync([embedText], ct);
                var vec = vecs.Count > 0 ? vecs[0] : null;
                if (vec != null && vec.Length > 0)
                {
                    await db.ExecuteAsync("""
                        INSERT OR REPLACE INTO photo_embeddings(photo_id, embedding, created_at)
                        VALUES (@PhotoId, @Embedding, @Now)
                        """,
                        new { PhotoId = photoId, Embedding = JsonSerializer.Serialize(vec), Now = DateTime.UtcNow.ToString("o") },
                        transaction: tx);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inline embedding failed for photo {PhotoId} during AnnotateSingle", photoId);
        }

        _logger.LogInformation("Photo annotated (immediate): {PhotoId} via Unified CLI Chain", photoId);
        return (true, null);
    }

    private static async Task FailRow(IDbConnection db, IDbTransaction tx, QueueRow row, string error)
    {
        await db.ExecuteAsync(@"
            UPDATE processing_queue SET
                status = CASE WHEN retry_count >= 3 THEN 'failed' ELSE 'queued' END,
                retry_count  = retry_count + 1,
                error_message = @Err,
                completed_at  = @Now
            WHERE queue_id = @Id",
            new { Err = error[..Math.Min(error.Length, 500)], Now = DateTime.UtcNow, Id = row.queue_id },
            transaction: tx);
    }

    public class QueueRow
    {
        public int queue_id { get; set; }
        public string photo_id  { get; set; } = "";
        public string file_path { get; set; } = "";
        public string provider  { get; set; } = "";
        public int retry_count  { get; set; }
    }

    private class AnnotationResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("caption_short")]
        public string? CaptionShort    { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("caption_long")]
        public string? CaptionLong     { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("scene_type")]
        public string? SceneType       { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("subjects")]
        public string? Subjects        { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("activities")]
        public string? Activities      { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        public string[]? Tags          { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("person_count")]
        public int PersonCount         { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("confidence_score")]
        public double ConfidenceScore  { get; set; }
    }
}
