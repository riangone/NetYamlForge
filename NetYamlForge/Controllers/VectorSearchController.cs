using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers;

/// <summary>
/// GET /{project}/api/vector-search?q=...&amp;limit=20
/// Gemini text-embedding-004 でクエリをベクトル化し、コサイン類似度で最近傍写真を返す。
/// </summary>
[Route("{project}/api")]
public class VectorSearchController : Controller
{
    private readonly IGeminiEmbeddingService _embedding;
    private readonly IDbConnection _db;
    private readonly ILogger<VectorSearchController> _logger;

    public VectorSearchController(
        IGeminiEmbeddingService embedding,
        IDbConnection db,
        ILogger<VectorSearchController> logger)
    {
        _embedding = embedding;
        _db = db;
        _logger = logger;
    }

    [HttpGet("vector-search")]
    public async Task<IActionResult> Search(
        string project,
        [FromQuery] string? q,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { results = Array.Empty<object>(), message = "请输入搜索词" });

        if (limit is < 1 or > 100) limit = 20;

        // 1. Embed the query
        var queryVec = await _embedding.EmbedAsync(q.Trim(), ct);
        if (queryVec == null)
            return Ok(new { results = Array.Empty<object>(), message = "Embedding 服务不可用，请检查 EmbeddingProvider 配置（当前: lmstudio）及 LM Studio 是否运行在 http://localhost:1234/v1" });

        // 2. Check table exists
        var tableExists = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='photo_embeddings'");
        if (tableExists == 0)
            return Ok(new { results = Array.Empty<object>(), message = "尚未生成向量索引，请先运行「向量嵌入生成」批处理任务" });

        // 3. Load all stored embeddings with photo metadata
        var rows = (await _db.QueryAsync(
            """
            SELECT e.photo_id, e.embedding,
                   p.file_name, p.file_path, p.caption_short, p.scene_type,
                   p.subjects, p.gps_address, p.taken_at, p.thumb_path,
                   p.confidence_score
            FROM photo_embeddings e
            JOIN photos p ON p.photo_id = e.photo_id
            WHERE p.deleted_at IS NULL
            """)).ToList();

        if (rows.Count == 0)
            return Ok(new { results = Array.Empty<object>(), message = "向量索引为空，请等待批处理任务完成标注后运行嵌入生成" });

        // 4. Cosine similarity in C#
        var scored = rows
            .Select(r =>
            {
                float[]? vec = null;
                try { vec = JsonSerializer.Deserialize<float[]>((string)r.embedding); }
                catch { /* skip malformed */ }

                var sim = vec != null ? CosineSimilarity(queryVec, vec) : -1f;
                return (sim, row: r);
            })
            .Where(x => x.sim > 0)
            .OrderByDescending(x => x.sim)
            .Take(limit)
            .ToList();

        // 5. Build result DTO
        var results = scored.Select(x =>
        {
            var r = x.row;
            var fileName = (string)r.file_name;
            // Build thumbnail URL: /photo/thumb/{fileName}?w=300
            var thumbUrl = $"/photo/thumb/{Uri.EscapeDataString(fileName)}?w=300";
            // Original photo URL from file_path
            var photoUrl = (string?)r.file_path ?? $"/uploads/photo-vault/{fileName}";

            return new
            {
                photo_id      = (string)r.photo_id,
                file_name     = fileName,
                caption_short = (string?)r.caption_short,
                scene_type    = (string?)r.scene_type,
                subjects      = (string?)r.subjects,
                gps_address   = (string?)r.gps_address,
                taken_at      = (string?)r.taken_at,
                confidence    = (double?)r.confidence_score,
                similarity    = Math.Round((double)x.sim, 4),
                thumb_url     = thumbUrl,
                photo_url     = photoUrl
            };
        }).ToList();

        return Ok(new { results, total = results.Count, query = q });
    }

    [HttpGet("embedding-stats")]
    public async Task<IActionResult> Stats(string project)
    {
        var tableExists = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='photo_embeddings'");

        if (tableExists == 0)
            return Ok(new { total_photos = 0, embedded = 0, pending = 0 });

        var total = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM photos WHERE deleted_at IS NULL AND annotation_status='done'");
        var embedded = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM photo_embeddings");

        return Ok(new { total_photos = total, embedded, pending = total - embedded });
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < 1e-8f ? 0f : dot / denom;
    }
}
