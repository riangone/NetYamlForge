using System.Data;
using System.Text.Json;
using Dapper;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// job type: photo_embedding_generator
/// annotation_status = 'done' の写真に対して Gemini text-embedding-004 でベクトルを生成し
/// photo_embeddings テーブルへ保存する。
/// </summary>
public class EmbeddingGeneratorExecutor : IBatchStepHandler
{
    public string StepType => "photo_embedding_generator";

    private readonly IGeminiEmbeddingService _embedding;
    private readonly ILogger<EmbeddingGeneratorExecutor> _logger;

    public EmbeddingGeneratorExecutor(
        IGeminiEmbeddingService embedding,
        ILogger<EmbeddingGeneratorExecutor> logger)
    {
        _embedding = embedding;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS photo_embeddings (
                photo_id   TEXT NOT NULL PRIMARY KEY,
                embedding  TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """, transaction: tx);

        var batchSize = job.BatchSize > 0 ? job.BatchSize : 10;

        var photos = (await db.QueryAsync<PhotoRow>(
            """
            SELECT p.photo_id,
                   p.caption_short, p.caption_long,
                   p.scene_type, p.subjects, p.activities,
                   p.style_labels, p.ocr_text, p.gps_address
            FROM photos p
            LEFT JOIN photo_embeddings e ON e.photo_id = p.photo_id
            WHERE p.deleted_at IS NULL
              AND p.annotation_status = 'done'
              AND e.photo_id IS NULL
            LIMIT @Batch
            """,
            new { Batch = batchSize },
            transaction: tx)).ToList();

        if (photos.Count == 0)
        {
            result.Success = true;
            result.RowsAffected = 0;
            return;
        }

        // Batch embed all texts in one Python call for efficiency
        var texts = photos.Select(BuildText).ToList();
        var vecs = await _embedding.EmbedBatchAsync(texts, ct);

        var done = 0;
        for (var i = 0; i < photos.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var photo = photos[i];
            var vec = i < vecs.Count ? vecs[i] : null;
            if (vec == null || vec.Length == 0)
            {
                _logger.LogWarning("Embedding returned null for photo {Id}", photo.photo_id);
                continue;
            }

            await db.ExecuteAsync(
                """
                INSERT OR REPLACE INTO photo_embeddings(photo_id, embedding, created_at)
                VALUES (@PhotoId, @Embedding, @Now)
                """,
                new
                {
                    PhotoId = photo.photo_id,
                    Embedding = JsonSerializer.Serialize(vec),
                    Now = DateTime.UtcNow.ToString("o")
                },
                transaction: tx);
            done++;

            _logger.LogInformation("Embedded photo {Id} ({Dims} dims)", photo.photo_id, vec.Length);
        }

        result.Success = true;
        result.RowsAffected = done;
    }

    private static string BuildText(PhotoRow p)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.caption_short)) parts.Add(p.caption_short);
        if (!string.IsNullOrWhiteSpace(p.caption_long))  parts.Add(p.caption_long);
        if (!string.IsNullOrWhiteSpace(p.scene_type))    parts.Add($"场景: {p.scene_type}");
        if (!string.IsNullOrWhiteSpace(p.subjects))      parts.Add($"主体: {p.subjects}");
        if (!string.IsNullOrWhiteSpace(p.activities))    parts.Add($"活动: {p.activities}");
        if (!string.IsNullOrWhiteSpace(p.style_labels))  parts.Add($"风格: {p.style_labels}");
        if (!string.IsNullOrWhiteSpace(p.gps_address))   parts.Add($"地点: {p.gps_address}");
        if (!string.IsNullOrWhiteSpace(p.ocr_text))      parts.Add($"文字: {p.ocr_text}");
        return string.Join(". ", parts);
    }

    private record PhotoRow(
        string photo_id,
        string? caption_short, string? caption_long,
        string? scene_type, string? subjects, string? activities,
        string? style_labels, string? ocr_text, string? gps_address);
}
