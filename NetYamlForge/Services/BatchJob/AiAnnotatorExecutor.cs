// DCS001 抑制理由: テーブル名・カラム名はすべて IsValidIdentifier() で検証済みの設定値のみを使用する動的SQL生成ユーティリティです。
#pragma warning disable DCS001
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob.Sdk;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// Generic AI annotator — works with any entity table, not just photos.
/// Configure via jobs.yml:
///   type: ai_annotator
///   aiProvider: lmstudio | antigravity | gemini | ollama | anthropic
///   batchSize: 5
///   annotationConfig:
///     sourceTable: documents
///     primaryKey: doc_id
///     filePathField: file_path
///     statusField: ai_status
///     queueTable: annotation_queue
///     embeddingTable: doc_embeddings
///     annotationPrompt: "..."   # optional — overrides built-in photo prompt
///     resultFields:             # JSON key → DB column (null = skip)
///       caption_short: summary
///       tags: null
///     autoEmbed: true
/// </summary>
public class AiAnnotatorExecutor : AiQueueStepHandlerBase<AiAnnotatorExecutor.QueueRow>
{
    public override string StepType => "ai_annotator";

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly IEmbeddingService _embedding;
    private readonly ILogger<AiAnnotatorExecutor> _logger;
    private readonly AiProviderDispatcher _providerDispatcher;

    private const string DefaultAnnotationPrompt = """
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

    // Default photo-schema result field mapping (annotation JSON key → DB column)
    private static readonly Dictionary<string, string?> DefaultResultFields = new()
    {
        ["caption_short"]    = "caption_short",
        ["caption_long"]     = "caption_long",
        ["scene_type"]       = "scene_type",
        ["subjects"]         = "subjects",
        ["activities"]       = "activities",
        ["person_count"]     = "person_count",
        ["confidence_score"] = "confidence_score",
        ["annotation_model"] = "annotation_model",
    };

    // Default text fields for building embedding input
    private static readonly List<EmbeddingTextField> DefaultEmbedTextFields =
    [
        new() { Field = "caption_short" },
        new() { Field = "caption_long" },
        new() { Field = "scene_type",   Prefix = "场景" },
        new() { Field = "subjects",     Prefix = "主体" },
        new() { Field = "activities",   Prefix = "活动" },
    ];

    public AiAnnotatorExecutor(
        IWebHostEnvironment env,
        IConfiguration configuration,
        IEmbeddingService embedding,
        ICliChainService cliChain,
        ILogger<AiAnnotatorExecutor> logger) : base(cliChain, logger)
    {
        _env = env;
        _configuration = configuration;
        _embedding = embedding;
        _logger = logger;
        _providerDispatcher = new AiProviderDispatcher(logger);
    }

    protected override async Task<IReadOnlyList<QueueRow>> FetchPendingAsync(
        BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        int batchSize, CancellationToken ct)
    {
        var cfg      = job.AnnotationConfig ?? new AnnotationJobConfig();
        var provider = job.AiProvider ?? "antigravity";

        var rows = (await db.QueryAsync<QueueRow>(
            $"""
            SELECT q.queue_id, q.{cfg.PrimaryKey} AS pk_value, q.{cfg.FilePathField} AS file_path,
                   q.provider, q.retry_count
            FROM {cfg.QueueTable} q
            WHERE q.status = 'queued' AND q.provider = @Provider
            ORDER BY q.priority DESC, q.queued_at ASC
            LIMIT @Batch
            """,
            new { Batch = batchSize, Provider = provider },
            transaction: tx)).ToList();
        return rows;
    }

    protected override async Task MarkProcessingAsync(
        QueueRow row, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
    {
        var cfg = job.AnnotationConfig ?? new AnnotationJobConfig();
        var now = DateTime.UtcNow;
        await db.ExecuteAsync(
            $"UPDATE {cfg.QueueTable} SET status='processing', started_at=@Now WHERE queue_id=@Id",
            new { Now = now, Id = row.queue_id }, transaction: tx);
    }

    protected override async Task<RowOutcome> ProcessRowAsync(
        QueueRow row, BatchJobDefinition job, string? projectName, IDbConnection db, IDbTransaction tx,
        CancellationToken ct)
    {
        var cfg      = job.AnnotationConfig ?? new AnnotationJobConfig();
        var provider = job.AiProvider ?? "antigravity";
        var prompt   = cfg.AnnotationPrompt ?? DefaultAnnotationPrompt;
        var now      = DateTime.UtcNow;

        var absolutePath = ResolveFilePath(row.file_path);
        if (!File.Exists(absolutePath))
        {
            return RowOutcome.Fail($"File not found: {absolutePath}");
        }

        var usedProvider = string.IsNullOrEmpty(row.provider) ? provider : row.provider;
        var rawText = await _providerDispatcher.DispatchAsync(
            usedProvider, prompt, absolutePath, projectName,
            key => ProjectEnvLoader.GetValue(key, projectName, _configuration), ct);

        var annotation = rawText != null ? ParseAnnotationJson(rawText) : null;

        if (annotation == null)
        {
            return RowOutcome.Fail("AI returned empty or unparseable response");
        }

        await WriteAnnotationResult(db, tx, row.pk_value, cfg, annotation, usedProvider, now);

        if (cfg.AutoEmbed)
            await TryInlineEmbed(db, tx, row.pk_value, cfg, annotation, ct);

        var ms = (long)(DateTime.UtcNow - now).TotalMilliseconds;
        await db.ExecuteAsync(
            $"UPDATE {cfg.QueueTable} SET status='done', completed_at=@Now, processing_ms=@Ms, provider=@Provider WHERE queue_id=@Id",
            new { Now = DateTime.UtcNow, Ms = ms, Provider = usedProvider, Id = row.queue_id },
            transaction: tx);

        _logger.LogInformation("Annotated {Table}/{Id} via {Provider}", cfg.SourceTable, row.pk_value, usedProvider);
        return RowOutcome.Ok();
    }

    protected override async Task WriteOutcomeAsync(
        QueueRow row, RowOutcome outcome, BatchJobDefinition job, IDbConnection db, IDbTransaction tx)
    {
        if (outcome.Status == RowStatus.Failed)
        {
            var cfg = job.AnnotationConfig ?? new AnnotationJobConfig();
            var error = outcome.Reason ?? "Unknown error";
            await FailRow(db, tx, row, cfg, error);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Write annotation result columns back to source table
    // ──────────────────────────────────────────────────────────

    private async Task WriteAnnotationResult(
        IDbConnection db, IDbTransaction tx,
        string pkValue, AnnotationJobConfig cfg,
        AnnotationResult annotation, string provider, DateTime now)
    {
        var fields = cfg.ResultFields ?? DefaultResultFields;
        var setClauses = new List<string>();
        var parameters = new Dictionary<string, object?> { ["PkValue"] = pkValue, ["Now"] = now };

        // Build SET clause from mapping
        foreach (var (jsonKey, dbCol) in fields)
        {
            if (string.IsNullOrEmpty(dbCol)) continue;

            object? val = jsonKey switch
            {
                "caption_short"    => annotation.CaptionShort,
                "caption_long"     => annotation.CaptionLong,
                "scene_type"       => annotation.SceneType,
                "subjects"         => annotation.Subjects,
                "activities"       => annotation.Activities,
                "person_count"     => (object)annotation.PersonCount,
                "confidence_score" => annotation.ConfidenceScore,
                "annotation_model" => AiProviderDispatcher.GetModelName(provider),
                "tags"             => annotation.Tags != null ? string.Join(",", annotation.Tags) : null,
                _                  => null
            };

            setClauses.Add($"{dbCol} = @p_{jsonKey}");
            parameters[$"p_{jsonKey}"] = val;
        }

        // Always write status, model, and timestamps
        setClauses.Add($"{cfg.StatusField} = 'done'");
        if (!fields.ContainsKey("annotation_model"))
        {
            setClauses.Add("annotation_model = @ModelName");
            parameters["ModelName"] = AiProviderDispatcher.GetModelName(provider);
        }
        setClauses.Add("annotation_at = @Now");
        setClauses.Add("updated_at = @Now");

        var sql = $"UPDATE {cfg.SourceTable} SET {string.Join(", ", setClauses)} WHERE {cfg.PrimaryKey} = @PkValue";
        await db.ExecuteAsync(sql, parameters, transaction: tx);
    }

    // ──────────────────────────────────────────────────────────
    // Inline embedding after annotation
    // ──────────────────────────────────────────────────────────

    private async Task TryInlineEmbed(
        IDbConnection db, IDbTransaction tx,
        string pkValue, AnnotationJobConfig cfg,
        AnnotationResult annotation, CancellationToken ct)
    {
        try
        {
            await db.ExecuteAsync($"""
                CREATE TABLE IF NOT EXISTS {cfg.EmbeddingTable} (
                    {cfg.PrimaryKey} TEXT NOT NULL PRIMARY KEY,
                    embedding         TEXT NOT NULL,
                    created_at        TEXT NOT NULL
                )
                """, transaction: tx);

            var textFields = cfg.EmbedTextFields ?? DefaultEmbedTextFields;
            var text = BuildEmbedText(annotation, textFields);
            if (string.IsNullOrWhiteSpace(text)) return;

            var vecs = await _embedding.EmbedBatchAsync([text], ct);
            var vec = vecs.Count > 0 ? vecs[0] : null;
            if (vec == null || vec.Length == 0) return;

            await db.ExecuteAsync($"""
                INSERT OR REPLACE INTO {cfg.EmbeddingTable}({cfg.PrimaryKey}, embedding, created_at)
                VALUES (@PkValue, @Embedding, @Now)
                """,
                new { PkValue = pkValue, Embedding = JsonSerializer.Serialize(vec), Now = DateTime.UtcNow.ToString("o") },
                transaction: tx);

            _logger.LogInformation("Inline embedding generated for {Table}/{Id} ({Dims} dims)", cfg.SourceTable, pkValue, vec.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inline embedding failed for {Table}/{Id}, will retry via cron", cfg.SourceTable, pkValue);
        }
    }

    private static string BuildEmbedText(AnnotationResult a, IList<EmbeddingTextField> fields)
    {
        var parts = new List<string>();
        foreach (var f in fields)
        {
            var val = f.Field switch
            {
                "caption_short" => a.CaptionShort,
                "caption_long"  => a.CaptionLong,
                "scene_type"    => a.SceneType,
                "subjects"      => a.Subjects,
                "activities"    => a.Activities,
                _               => null
            };
            if (string.IsNullOrWhiteSpace(val)) continue;
            parts.Add(string.IsNullOrEmpty(f.Prefix) ? val : $"{f.Prefix}: {val}");
        }
        return string.Join(". ", parts);
    }

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    private string ResolveFilePath(string storedPath)
    {
        if (File.Exists(storedPath)) return storedPath;
        return Path.Combine(_env.WebRootPath, storedPath.TrimStart('/'));
    }

    private static AnnotationResult? ParseAnnotationJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var cleaned = Regex.Replace(text, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Trim();
            var start = cleaned.IndexOf('{');
            var end   = cleaned.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            return JsonSerializer.Deserialize<AnnotationResult>(cleaned[start..(end + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception)
        {
            // JSON反序列化解析失败时吞掉异常并返回null以作fallback
            return null;
        }
    }

    private static async Task FailRow(
        IDbConnection db, IDbTransaction tx, QueueRow row, AnnotationJobConfig cfg, string error)
    {
        await db.ExecuteAsync($"""
            UPDATE {cfg.QueueTable} SET
                status = CASE WHEN retry_count >= 3 THEN 'failed' ELSE 'queued' END,
                retry_count   = retry_count + 1,
                error_message = @Err,
                completed_at  = @Now
            WHERE queue_id = @Id
            """,
            new { Err = error[..Math.Min(error.Length, 500)], Now = DateTime.UtcNow, Id = row.queue_id },
            transaction: tx);
    }

    public class QueueRow
    {
        public int    queue_id  { get; set; }
        public string pk_value  { get; set; } = "";
        public string file_path { get; set; } = "";
        public string provider  { get; set; } = "";
        public int    retry_count { get; set; }
    }

    internal class AnnotationResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("caption_short")]
        public string? CaptionShort   { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("caption_long")]
        public string? CaptionLong    { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("scene_type")]
        public string? SceneType      { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("subjects")]
        public string? Subjects       { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("activities")]
        public string? Activities     { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        public string[]? Tags         { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("person_count")]
        public int PersonCount        { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; }
    }
}
