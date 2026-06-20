// DCS001 抑制理由: テーブル名・カラム名は設定値のみを使用する動的SQL生成ユーティリティです。
#pragma warning disable DCS001
using System.Data;
using System.Text.Json;
using Dapper;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// Generic AI embedding generator — works with any entity table.
/// Configure via jobs.yml:
///   type: ai_embedding_generator
///   batchSize: 10
///   embeddingConfig:
///     sourceTable: documents
///     primaryKey: doc_id
///     statusField: ai_status
///     statusValue: done
///     embeddingTable: doc_embeddings
///     textFields:
///       - field: summary
///       - field: body
///       - field: category
///         prefix: "分类"
/// </summary>
public class AiEmbeddingGeneratorExecutor : IBatchStepHandler
{
    public string StepType => "ai_embedding_generator";

    private static readonly List<EmbeddingTextField> DefaultTextFields =
    [
        new() { Field = "caption_short" },
        new() { Field = "caption_long" },
        new() { Field = "scene_type",  Prefix = "场景" },
        new() { Field = "subjects",    Prefix = "主体" },
        new() { Field = "activities",  Prefix = "活动" },
        new() { Field = "style_labels",Prefix = "风格" },
        new() { Field = "gps_address", Prefix = "地点" },
        new() { Field = "ocr_text",    Prefix = "文字" },
    ];

    private readonly IEmbeddingService _embedding;
    private readonly ILogger<AiEmbeddingGeneratorExecutor> _logger;

    public AiEmbeddingGeneratorExecutor(
        IEmbeddingService embedding,
        ILogger<AiEmbeddingGeneratorExecutor> logger)
    {
        _embedding = embedding;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var cfg = job.EmbeddingConfig ?? new EmbeddingJobConfig();
        var textFields = cfg.TextFields ?? DefaultTextFields;
        var batch = job.BatchSize > 0 ? job.BatchSize : 10;

        await db.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {cfg.EmbeddingTable} (
                {cfg.PrimaryKey} TEXT NOT NULL PRIMARY KEY,
                embedding         TEXT NOT NULL,
                created_at        TEXT NOT NULL
            )
            """, transaction: tx);

        // Select fields dynamically from config
        var selectCols = string.Join(", ", textFields.Select(f => $"s.{f.Field}").Distinct());
        var rows = (await db.QueryAsync(
            $"""
            SELECT s.{cfg.PrimaryKey}, {selectCols}
            FROM {cfg.SourceTable} s
            LEFT JOIN {cfg.EmbeddingTable} e ON e.{cfg.PrimaryKey} = s.{cfg.PrimaryKey}
            WHERE s.{cfg.StatusField} = @StatusValue
              AND e.{cfg.PrimaryKey} IS NULL
            LIMIT @Batch
            """,
            new { cfg.StatusValue, Batch = batch },
            transaction: tx)).ToList();

        if (rows.Count == 0)
        {
            result.Success = true;
            result.RowsAffected = 0;
            return;
        }

        var texts = rows
            .Select(r => BuildText((IDictionary<string, object>)r, cfg.PrimaryKey, textFields))
            .ToList();

        var vecs = await _embedding.EmbedBatchAsync(texts, ct);

        var done = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var row = rows[i];
            var rowDict = (IDictionary<string, object>)row;
            var pkValue = rowDict.TryGetValue(cfg.PrimaryKey, out var pkObj) ? pkObj?.ToString() : null;
            var vec = i < vecs.Count ? vecs[i] : null;

            if (vec == null || vec.Length == 0)
            {
                _logger.LogWarning("Embedding returned null for {Table}/{Id}", cfg.SourceTable, pkValue);
                continue;
            }

            await db.ExecuteAsync(
                $"""
                INSERT OR REPLACE INTO {cfg.EmbeddingTable}({cfg.PrimaryKey}, embedding, created_at)
                VALUES (@PkValue, @Embedding, @Now)
                """,
                new { PkValue = pkValue, Embedding = JsonSerializer.Serialize(vec), Now = DateTime.UtcNow.ToString("o") },
                transaction: tx);

            if (cfg.UpdateStatusAfterEmbed)
            {
                await db.ExecuteAsync(
                    $"UPDATE {cfg.SourceTable} SET {cfg.StatusField}=@Done WHERE {cfg.PrimaryKey}=@PkValue",
                    new { Done = cfg.DoneStatusValue, PkValue = pkValue },
                    transaction: tx);
            }

            done++;
            _logger.LogInformation("Embedded {Table}/{Id} ({Dims} dims)", cfg.SourceTable, pkValue, vec.Length);
        }

        result.Success = true;
        result.RowsAffected = done;
    }

    private static string BuildText(IDictionary<string, object> dict, string primaryKey, IList<EmbeddingTextField> fields)
    {
        var parts = new List<string>();

        foreach (var f in fields)
        {
            if (!dict.TryGetValue(f.Field, out var rawVal)) continue;
            var val = rawVal?.ToString();
            if (string.IsNullOrWhiteSpace(val)) continue;
            parts.Add(string.IsNullOrEmpty(f.Prefix) ? val : $"{f.Prefix}: {val}");
        }

        return string.Join(". ", parts);
    }
}
