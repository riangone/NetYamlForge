// DCS001 suppressed: dynamic IN clauses built from internal DB-derived IDs, never from raw user input.
#pragma warning disable DCS001
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Projects.KbForge;

public class KbForgeRepository
{
    private readonly IDbConnection _db;
    private readonly IEmbeddingService _embedding;
    private readonly IAntigravityCliService _ai;
    private readonly ILogger<KbForgeRepository> _logger;

    public KbForgeRepository(
        IDbConnection db,
        IEmbeddingService embedding,
        IAntigravityCliService ai,
        ILogger<KbForgeRepository> logger)
    {
        _db = db;
        _embedding = embedding;
        _ai = ai;
        _logger = logger;
    }

    // ── Tags ──

    public async Task<List<KbTag>> GetAllTagsAsync()
    {
        var rows = await _db.QueryAsync<KbTag>("SELECT * FROM tags ORDER BY article_count DESC, name");
        return rows.ToList();
    }

    // ── Articles ──

    public async Task<List<KbArticle>> GetAllAsync(string? filterTag = null, string? filterStatus = null)
    {
        var where = new List<string>();
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(filterStatus))
        {
            where.Add("a.status = @Status");
            p.Add("Status", filterStatus);
        }
        if (!string.IsNullOrWhiteSpace(filterTag))
        {
            where.Add("EXISTS (SELECT 1 FROM article_tags at2 JOIN tags t ON t.id=at2.tag_id WHERE at2.article_id=a.id AND t.slug=@Tag)");
            p.Add("Tag", filterTag);
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var sql = $"SELECT a.* FROM articles a {whereClause} ORDER BY a.updated_at DESC";

        var articles = (await _db.QueryAsync<KbArticle>(sql, p)).ToList();
        await AttachTagsAsync(articles);
        return articles;
    }

    public async Task<KbArticle?> GetByIdAsync(string id)
    {
        var article = await _db.QueryFirstOrDefaultAsync<KbArticle>(
            "SELECT * FROM articles WHERE id = @Id", new { Id = id });
        if (article != null) await AttachTagsAsync([article]);
        return article;
    }

    public async Task<KbArticle?> GetBySlugAsync(string slug)
    {
        var article = await _db.QueryFirstOrDefaultAsync<KbArticle>(
            "SELECT * FROM articles WHERE slug = @Slug", new { Slug = slug });
        if (article != null) await AttachTagsAsync([article]);
        return article;
    }

    public async Task<KbArticle> CreateAsync(KbSaveArticleRequest req, string author)
    {
        var id = Guid.NewGuid().ToString("N");
        var slug = await GenerateSlugAsync(req.Title);
        var now = DateTime.UtcNow.ToString("o");

        await _db.ExecuteAsync(
            """
            INSERT INTO articles (id, slug, title, content, summary, status, author, embedding_status, created_at, updated_at)
            VALUES (@Id, @Slug, @Title, @Content, @Summary, @Status, @Author, 'pending', @Now, @Now)
            """,
            new { Id = id, Slug = slug, req.Title, req.Content, req.Summary, req.Status, Author = author, Now = now });

        await SyncTagsAsync(id, req.Tags);
        return (await GetByIdAsync(id))!;
    }

    public async Task<KbArticle?> UpdateAsync(string id, KbSaveArticleRequest req, string changedBy)
    {
        var existing = await GetByIdAsync(id);
        if (existing == null) return null;

        await _db.ExecuteAsync(
            """
            INSERT INTO revisions (id, article_id, title, content, changed_by, created_at)
            VALUES (@RevId, @ArticleId, @Title, @Content, @ChangedBy, @Now)
            """,
            new
            {
                RevId = Guid.NewGuid().ToString("N"),
                ArticleId = id,
                existing.Title,
                existing.Content,
                ChangedBy = changedBy,
                Now = DateTime.UtcNow.ToString("o")
            });

        var needsNewSlug = !string.Equals(existing.Title, req.Title, StringComparison.Ordinal);
        var slug = needsNewSlug ? await GenerateSlugAsync(req.Title, id) : existing.Slug;
        var now = DateTime.UtcNow.ToString("o");

        await _db.ExecuteAsync(
            """
            UPDATE articles
            SET slug=@Slug, title=@Title, content=@Content, summary=@Summary,
                status=@Status, embedding_status='pending', updated_at=@Now
            WHERE id=@Id
            """,
            new { Id = id, Slug = slug, req.Title, req.Content, req.Summary, req.Status, Now = now });

        await SyncTagsAsync(id, req.Tags);
        return await GetByIdAsync(id);
    }

    public async Task DeleteAsync(string id)
    {
        await _db.ExecuteAsync("DELETE FROM articles WHERE id = @Id", new { Id = id });
    }

    public async Task IncrementViewCountAsync(string id)
    {
        await _db.ExecuteAsync(
            "UPDATE articles SET view_count = view_count + 1 WHERE id = @Id", new { Id = id });
    }

    // ── Revisions ──

    public async Task<List<KbRevision>> GetRevisionsAsync(string articleId)
    {
        var rows = await _db.QueryAsync<KbRevision>(
            "SELECT * FROM revisions WHERE article_id = @ArticleId ORDER BY created_at DESC LIMIT 10",
            new { ArticleId = articleId });
        return rows.ToList();
    }

    // ── Search ──

    public async Task<List<KbSearchResult>> KeywordSearchAsync(string query)
    {
        var like = $"%{query}%";
        var rows = await _db.QueryAsync<KbArticle>(
            """
            SELECT * FROM articles
            WHERE status='published' AND (title LIKE @Like OR content LIKE @Like OR summary LIKE @Like)
            ORDER BY updated_at DESC LIMIT 20
            """,
            new { Like = like });
        var articles = rows.ToList();
        await AttachTagsAsync(articles);
        return articles.Select(a => new KbSearchResult { Article = a, Score = 1f }).ToList();
    }

    public async Task<List<KbSearchResult>> SemanticSearchAsync(string query, int topK = 10)
    {
        var queryVec = await _embedding.EmbedAsync(query);
        if (queryVec == null || queryVec.Length == 0)
        {
            _logger.LogWarning("Embedding service unavailable, falling back to keyword search");
            return await KeywordSearchAsync(query);
        }

        var embedRows = (await _db.QueryAsync<(string id, string embedding)>(
            "SELECT id, embedding FROM article_embeddings")).ToList();

        var scored = new List<(string id, float score)>(embedRows.Count);
        foreach (var (rowId, embJson) in embedRows)
        {
            try
            {
                var vec = JsonSerializer.Deserialize<float[]>(embJson);
                if (vec == null) continue;
                scored.Add((rowId, CosineSimilarity(queryVec, vec)));
            }
            catch { }
        }

        var topIds = scored.OrderByDescending(x => x.score).Take(topK).ToList();
        if (topIds.Count == 0) return [];

        var idList = string.Join(",", topIds.Select(x => $"'{x.id}'"));
        var articles = (await _db.QueryAsync<KbArticle>(
            $"SELECT * FROM articles WHERE id IN ({idList}) AND status='published'")).ToList();
        await AttachTagsAsync(articles);

        return topIds
            .Where(t => articles.Any(a => a.Id == t.id))
            .Select(t => new KbSearchResult
            {
                Article = articles.First(a => a.Id == t.id),
                Score = t.score,
                IsSemantic = true
            })
            .ToList();
    }

    // ── AI Summarize ──

    public async Task<string?> SummarizeAsync(string articleId)
    {
        var article = await GetByIdAsync(articleId);
        if (article == null) return null;

        var prompt = $"""
            Summarize the following knowledge base article in 2-3 concise sentences.
            Output only the summary text, no preamble or labels.

            Title: {article.Title}

            {article.Content}
            """;
        try
        {
            var summary = (await _ai.PromptAsync(prompt)).Trim();
            await _db.ExecuteAsync(
                "UPDATE articles SET summary=@Summary, updated_at=@Now WHERE id=@Id",
                new { Summary = summary, Now = DateTime.UtcNow.ToString("o"), Id = articleId });
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI summarization failed for article {Id}", articleId);
            return null;
        }
    }

    // ── Helpers ──

    private async Task AttachTagsAsync(List<KbArticle> articles)
    {
        if (articles.Count == 0) return;
        var idList = string.Join(",", articles.Select(a => $"'{a.Id}'"));
        var rows = (await _db.QueryAsync<(string article_id, string name)>(
            $"SELECT at.article_id, t.name FROM article_tags at JOIN tags t ON t.id=at.tag_id WHERE at.article_id IN ({idList})")).ToList();
        var byId = rows.GroupBy(r => r.article_id)
            .ToDictionary(g => g.Key, g => g.Select(r => r.name).ToList());
        foreach (var a in articles)
            a.Tags = byId.TryGetValue(a.Id, out var tags) ? tags : [];
    }

    private async Task SyncTagsAsync(string articleId, List<string> tagNames)
    {
        await _db.ExecuteAsync(
            "DELETE FROM article_tags WHERE article_id = @Id", new { Id = articleId });

        foreach (var name in tagNames.Select(t => t.Trim())
                     .Where(t => !string.IsNullOrEmpty(t)).Distinct())
        {
            var slug = Slugify(name);
            var tagId = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT id FROM tags WHERE slug = @Slug", new { Slug = slug });

            if (tagId == null)
            {
                tagId = Guid.NewGuid().ToString("N");
                await _db.ExecuteAsync(
                    "INSERT INTO tags (id, name, slug, article_count) VALUES (@Id, @Name, @Slug, 0)",
                    new { Id = tagId, Name = name, Slug = slug });
            }

            await _db.ExecuteAsync(
                "INSERT OR IGNORE INTO article_tags (article_id, tag_id) VALUES (@ArticleId, @TagId)",
                new { ArticleId = articleId, TagId = tagId });
        }

        await _db.ExecuteAsync(
            "UPDATE tags SET article_count = (SELECT COUNT(*) FROM article_tags WHERE tag_id=tags.id)");
    }

    private async Task<string> GenerateSlugAsync(string title, string? excludeId = null)
    {
        var baseSlug = Slugify(title);
        var slug = baseSlug;
        for (var i = 2; i < 1000; i++)
        {
            var existing = await _db.QueryFirstOrDefaultAsync<string>(
                "SELECT id FROM articles WHERE slug = @Slug AND (@ExcludeId IS NULL OR id != @ExcludeId)",
                new { Slug = slug, ExcludeId = excludeId });
            if (existing == null) return slug;
            slug = $"{baseSlug}-{i}";
        }
        return $"{baseSlug}-{Guid.NewGuid():N}";
    }

    private static string Slugify(string text)
    {
        var s = text.ToLowerInvariant().Trim();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? "untitled" : s;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0f || normB == 0f ? 0f : dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
