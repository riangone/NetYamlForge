using System.Data;
using Dapper;
using NetYamlForge.Services.Ai;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// AI を使用してブログ記事を自動生成し、データベースに保存するジョブ実行器。
/// </summary>
public class AutomatedBlogGeneratorExecutor
{
    private readonly IGeminiCliService _gemini;
    private readonly ILogger<AutomatedBlogGeneratorExecutor> _logger;

    public AutomatedBlogGeneratorExecutor(IGeminiCliService gemini, ILogger<AutomatedBlogGeneratorExecutor> logger)
    {
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<BatchJobResult> ExecuteAsync(
        BatchJobDefinition job,
        string projectName,
        IDbConnection db,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult { JobId = job.Id, StartedAt = DateTime.UtcNow };

        try
        {
            var target = job.Settings.Params?.GetValueOrDefault("target") ?? "京王電鉄9008";
            var categoryIdStr = job.Settings.Params?.GetValueOrDefault("categoryId") ?? "1";
            var tagNamesStr = job.Settings.Params?.GetValueOrDefault("tags") ?? "";
            var author = job.Settings.Params?.GetValueOrDefault("author") ?? "AI Analyst";
            var language = job.Settings.Params?.GetValueOrDefault("language") ?? "ja-JP";
            var customPrompt = job.Settings.Params?.GetValueOrDefault("prompt");
            
            int.TryParse(categoryIdStr, out var categoryId);

            _logger.LogInformation("Automated Blog Generation Start: {Target} (Project: {Project}, Language: {Language})", target, projectName, language);

            var prompt = customPrompt;
            if (string.IsNullOrEmpty(prompt))
            {
                prompt = $@"
あなたはプロの証券アナリストです。
銘柄や市場セグメント「{target}」についての最新の分析レポートをブログ記事として作成してください。
**必ず以下の言語で執筆してください: {language}**

構成:
1. キャッチーなタイトル
2. 概要
3. 最近の動向と分析
4. 今後の見通しとリスク要因
5. 投資判断またはまとめ
6. 関連リンクや参考情報

出力形式は以下のJSONのみとしてください:
{{
  ""title"": ""記事のタイトル"",
  ""summary"": ""記事の短い要約"",
  ""content"": ""マークダウン形式の本文内容"",
  ""slug"": ""analysis-report""
}}
";
            }
            else
            {
                prompt = prompt.Replace("{target}", target).Replace("{language}", language);
            }

            var blogData = await _gemini.PromptJsonAsync<BlogArticle>(prompt, projectName: projectName, cancellationToken: cancellationToken);

            if (blogData == null || string.IsNullOrEmpty(blogData.Content))
            {
                throw new Exception("AI failed to generate blog article or returned invalid format.");
            }

            // Save to DB
            var sqlPost = @"
                INSERT INTO Post (
                    Title, Slug, Summary, Content, CategoryId, AuthorName, Status, FeaturedFlag, ViewCount, PublishedAt, CreatedAt, UpdatedAt
                ) VALUES (
                    @Title, @Slug, @Summary, @Content, @CategoryId, @AuthorName, 'published', 0, 0, @Now, @Now, @Now
                );
                SELECT last_insert_rowid();";

            var postId = await db.QuerySingleAsync<int>(sqlPost, new {
                blogData.Title,
                Slug = blogData.Slug + "-" + DateTime.UtcNow.ToString("yyyyMMdd"),
                blogData.Summary,
                blogData.Content,
                CategoryId = categoryId,
                AuthorName = author,
                Now = DateTime.UtcNow
            });

            // Handle Tags
            if (!string.IsNullOrEmpty(tagNamesStr))
            {
                var tagNames = tagNamesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var tagName in tagNames)
                {
                    // Find or create tag
                    var tagId = await db.QueryFirstOrDefaultAsync<int?>(
                        "SELECT Id FROM Tag WHERE Name = @Name", new { Name = tagName });
                    
                    if (tagId == null)
                    {
                        var slug = tagName.ToLowerInvariant().Replace(" ", "-");
                        await db.ExecuteAsync(
                            "INSERT INTO Tag (Name, Slug, CreatedAt) VALUES (@Name, @Slug, @Now)", 
                            new { Name = tagName, Slug = slug, Now = DateTime.UtcNow });
                        tagId = await db.QuerySingleAsync<int>("SELECT last_insert_rowid()");
                    }

                    // Link tag to post
                    await db.ExecuteAsync(
                        "INSERT OR IGNORE INTO PostTag (PostId, TagId) VALUES (@PostId, @TagId)",
                        new { PostId = postId, TagId = tagId.Value });
                }
            }

            result.Success = true;
            result.RowsAffected = 1;
            _logger.LogInformation("Blog article created successfully: {Title} (ID: {PostId})", blogData.Title, postId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automated Blog Generation Error: {JobId}", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorDetail = ex.ToString();
        }
        finally
        {
            result.EndedAt = DateTime.UtcNow;
        }

        return result;
    }

    private class BlogArticle
    {
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Content { get; set; } = "";
        public string Slug { get; set; } = "";
    }
}
