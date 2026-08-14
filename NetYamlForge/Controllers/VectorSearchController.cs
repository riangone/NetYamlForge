// DCS001 抑制理由: テーブル名は IsValidIdentifier() で検証済みの設定値のみを使用する動的SQL生成ユーティリティです。
#pragma warning disable DCS001
using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers;

/// <summary>
/// Generic vector search endpoint — works with any entity that has an embeddings table.
///
/// GET /{project}/api/vector-search
///   ?q=search text
///   &amp;limit=20
///   &amp;sourceTable=photos          (default: photos)
///   &amp;embeddingTable=photo_embeddings  (default: photo_embeddings)
///   &amp;pkField=photo_id            (default: photo_id)
///   &amp;labelField=caption_short    (default: caption_short)
///
/// GET /{project}/api/embedding-stats
///   ?sourceTable=photos
///   &amp;embeddingTable=photo_embeddings
///   &amp;statusField=annotation_status
/// </summary>
[Route("{project}/api")]
public class VectorSearchController : Controller
{
    private readonly IEmbeddingService _embedding;
    private readonly IDbConnection _db;
    private readonly ILogger<VectorSearchController> _logger;

    public VectorSearchController(
        IEmbeddingService embedding,
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
        [FromQuery] string sourceTable    = "photos",
        [FromQuery] string embeddingTable = "photo_embeddings",
        [FromQuery] string pkField        = "photo_id",
        [FromQuery] string labelField     = "caption_short",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new { results = Array.Empty<object>(), message = "请输入搜索词" });

        if (limit is < 1 or > 100) limit = 20;

        // Validate table/column names to prevent SQL injection
        if (!IsValidIdentifier(sourceTable) || !IsValidIdentifier(embeddingTable) ||
            !IsValidIdentifier(pkField) || !IsValidIdentifier(labelField))
            return BadRequest("Invalid table or column name");

        var queryVec = await _embedding.EmbedAsync(q.Trim(), ct);
        if (queryVec == null)
            return Ok(new { results = Array.Empty<object>(), message = "Embedding 服务不可用，请检查 EmbeddingProvider 配置" });

        var tableExists = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@Name",
            new { Name = embeddingTable });
        if (tableExists == 0)
            return Ok(new { results = Array.Empty<object>(), message = $"向量表 {embeddingTable} 不存在，请先运行嵌入生成任务" });

        // Load embeddings + source row (photo-schema fields included when using default tables)
        string selectSql;
        if (sourceTable == "photos" && embeddingTable == "photo_embeddings")
        {
            selectSql = $"""
                SELECT e.{pkField}, e.embedding,
                       s.file_name, s.file_path, s.caption_short, s.scene_type,
                       s.subjects, s.gps_address, s.taken_at, s.thumb_path,
                       s.confidence_score
                FROM {embeddingTable} e
                JOIN {sourceTable} s ON s.{pkField} = e.{pkField}
                WHERE s.deleted_at IS NULL
                """;
        }
        else
        {
            selectSql = $"""
                SELECT e.{pkField}, e.embedding, s.{labelField}
                FROM {embeddingTable} e
                JOIN {sourceTable} s ON s.{pkField} = e.{pkField}
                """;
        }

        var rows = (await _db.QueryAsync(selectSql)).ToList();

        if (rows.Count == 0)
            return Ok(new { results = Array.Empty<object>(), message = "向量索引为空，请等待嵌入生成任务完成" });

        var scored = rows
            .Select(r =>
            {
                var dict = (IDictionary<string, object>)r;
                float[]? vec = null;
                try { vec = JsonSerializer.Deserialize<float[]>((string)dict["embedding"]); }
                catch { /* skip */ }
                return (sim: vec != null ? CosineSimilarity(queryVec, vec) : -1f, row: dict);
            })
            .Where(x => x.sim > 0)
            .OrderByDescending(x => x.sim)
            .Take(limit)
            .ToList();

        var results = scored.Select(x =>
        {
            var r = x.row;
            var pkValue = GetStr(r, pkField);

            // Photo-specific enriched result
            if (sourceTable == "photos")
            {
                var fileName = GetStr(r, "file_name") ?? "";
                var pathBase = Request.PathBase.Value ?? "";
                var thumb = $"{pathBase}/{project}/photo-file/thumb/{Uri.EscapeDataString(pkValue)}?w=300";
                var serve = $"{pathBase}/{project}/photo-file/serve/{Uri.EscapeDataString(pkValue)}";
                return (object)new
                {
                    id            = pkValue,
                    file_name     = fileName,
                    caption_short = GetStr(r, "caption_short"),
                    scene_type    = GetStr(r, "scene_type"),
                    subjects      = GetStr(r, "subjects"),
                    gps_address   = GetStr(r, "gps_address"),
                    taken_at      = GetStr(r, "taken_at"),
                    confidence    = r.TryGetValue("confidence_score", out var cs) && cs is double d ? (double?)d : null,
                    similarity    = Math.Round((double)x.sim, 4),
                    thumb_url     = thumb,
                    photo_url     = serve
                };
            }

            // Generic result
            return (object)new
            {
                id         = pkValue,
                label      = GetStr(r, labelField),
                similarity = Math.Round((double)x.sim, 4),
            };
        }).ToList();

        return Ok(new { results, total = results.Count, query = q, sourceTable, embeddingTable });
    }

    [HttpGet("embedding-stats")]
    public async Task<IActionResult> Stats(
        string project,
        [FromQuery] string sourceTable    = "photos",
        [FromQuery] string embeddingTable = "photo_embeddings",
        [FromQuery] string statusField    = "annotation_status")
    {
        if (!IsValidIdentifier(sourceTable) || !IsValidIdentifier(embeddingTable) || !IsValidIdentifier(statusField))
            return BadRequest("Invalid table or column name");

        var tableExists = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@Name",
            new { Name = embeddingTable });
        if (tableExists == 0)
            return Ok(new { total = 0, embedded = 0, pending = 0 });

        var total = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {sourceTable} WHERE {statusField}='done'");
        var embedded = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {embeddingTable}");

        return Ok(new { total, embedded, pending = total - embedded, sourceTable, embeddingTable });
    }

    private static string? GetStr(IDictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static bool IsValidIdentifier(string name)
        => !string.IsNullOrWhiteSpace(name) &&
           System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$") &&
           name.Length <= 64;

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
