using System.Data;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using Dapper;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// AI を使用してブログ記事を自動生成し、データベースに保存するジョブ実行器。
/// </summary>
public class AutomatedBlogGeneratorExecutor : IBatchStepHandler
{
    public string StepType => "automated_blog_generator";
    private readonly IAntigravityCliService _antigravity;
    private readonly ILogger<AutomatedBlogGeneratorExecutor> _logger;

    public AutomatedBlogGeneratorExecutor(IAntigravityCliService antigravity, ILogger<AutomatedBlogGeneratorExecutor> logger)
    {
        _antigravity = antigravity;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var r = await ExecuteAsync(job, projectName ?? "", db, ct);
        result.Success = r.Success;
        result.RowsAffected = r.RowsAffected;
        result.ErrorMessage = r.ErrorMessage;
        result.ErrorDetail = r.ErrorDetail;
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

            var nowJst = TimeZoneInfo.ConvertTime(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
            var dateStr = nowJst.ToString("yyyy年MM月dd日");

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
                prompt = prompt
                    .Replace("{target}", target)
                    .Replace("{language}", language)
                    .Replace("{date}", dateStr);
            }

            BlogArticle? blogData = null;

            if (job.Id == "japan_it_news_briefing")
            {
                blogData = await ExecuteNewsJobAsync(job, projectName, dateStr, cancellationToken);
            }
            else
            {
                var rawResponse = await _antigravity.PromptAsync(prompt, projectName: projectName, cancellationToken: cancellationToken);
                _logger.LogDebug("AI raw response (first 500 chars): {Resp}", rawResponse?[..Math.Min(500, rawResponse?.Length ?? 0)]);

                if (!string.IsNullOrWhiteSpace(rawResponse))
                {
                    try
                    {
                        var cleaned = System.Text.RegularExpressions.Regex.Replace(rawResponse, @"```(?:json)?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                        var start = cleaned.IndexOf('{');
                        var end = cleaned.LastIndexOf('}');
                        if (start >= 0 && end > start)
                        {
                            var json = cleaned[start..(end + 1)];
                            blogData = System.Text.Json.JsonSerializer.Deserialize<BlogArticle>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to parse AI JSON response for {JobId}. Raw: {Raw}", job.Id, rawResponse?[..Math.Min(1000, rawResponse?.Length ?? 0)]);
                    }
                }
            }

            if (blogData == null || string.IsNullOrEmpty(blogData.Content))
            {
                _logger.LogError("AI response could not be parsed for {JobId}", job.Id);
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
                Slug = blogData.Slug + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
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

    // ── Japan IT News: fetch RSS → build content in code, AI writes narrative only ──

    private async Task<BlogArticle> ExecuteNewsJobAsync(
        BatchJobDefinition job, string projectName, string dateStr, CancellationToken ct)
    {
        _logger.LogInformation("japan_it_news_briefing: fetching RSS feeds...");
        var sites = await FetchRssArticlesAsync(ct);

        // Build article listing context for AI (titles only, no URL generation asked)
        var articleListing = new StringBuilder();
        foreach (var site in sites)
        {
            articleListing.AppendLine($"【{site.SiteName}】");
            if (site.Articles.Any())
                foreach (var a in site.Articles.Take(3))
                    articleListing.AppendLine($"  - {a.Title}");
            else
                articleListing.AppendLine("  (記事取得失敗)");
        }

        var narrativePrompt = $@"あなたは日本のIT業界に精通したテクノロジーライターです。
本日（{dateStr}）に以下の記事が各ITサイトで公開されました。

{articleListing}

以下の3つのセクションのみをJSON形式で生成してください。URLは一切生成しないでください。

1. trend_overview: 本日のIT業界全体のトレンド概要（2〜3文、日本語）
2. developer_insights: 開発者・エンジニアへの示唆 3点（各30〜50文字の箇条書き文字列の配列）
3. summary: まとめ（2〜3文、日本語）

出力は以下のJSONのみ（他のテキスト不要）:
{{
  ""trend_overview"": ""..."",
  ""developer_insights"": [""点1"", ""点2"", ""点3""],
  ""summary"": ""...""
}}";

        NewsNarrative? narrative = null;
        try
        {
            var raw = await _antigravity.PromptAsync(narrativePrompt, projectName: projectName, cancellationToken: ct);
            _logger.LogDebug("News narrative AI response: {Raw}", raw?[..Math.Min(500, raw?.Length ?? 0)]);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var cleaned = System.Text.RegularExpressions.Regex.Replace(raw, @"```(?:json)?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                var s = cleaned.IndexOf('{'); var e = cleaned.LastIndexOf('}');
                if (s >= 0 && e > s)
                    narrative = System.Text.Json.JsonSerializer.Deserialize<NewsNarrative>(cleaned[s..(e + 1)], new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse AI narrative for japan_it_news_briefing");
        }

        var content = BuildNewsContent(dateStr, sites, narrative);
        return new BlogArticle
        {
            Title = $"【IT簡報】{dateStr} 日本IT業界の注目ニュース",
            Summary = "Zenn・Qiita・DevelopersIO・gihyo.jp から本日の日本IT業界注目トピックをお届けします",
            Content = content,
            Slug = "it-news-briefing"
        };
    }

    private record RssArticle(string Title, string Url, string? Description);
    private record SiteInfo(string SiteName, string SiteUrl, string FeedUrl);
    private record SiteArticles(string SiteName, string SiteUrl, List<RssArticle> Articles);

    private static readonly SiteInfo[] NewsSites =
    [
        new("Zenn",         "https://zenn.dev/",              "https://zenn.dev/feed"),
        new("Qiita",        "https://qiita.com/",             "https://qiita.com/popular-items/feed.atom"),
        new("DevelopersIO", "https://dev.classmethod.jp/",    "https://dev.classmethod.jp/feed/"),
        new("gihyo.jp",     "https://gihyo.jp/",              "https://gihyo.jp/feed/atom"),
    ];

    private async Task<List<SiteArticles>> FetchRssArticlesAsync(CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");

        var result = new List<SiteArticles>();
        foreach (var site in NewsSites)
        {
            try
            {
                _logger.LogInformation("Fetching RSS: {Site} -> {Url}", site.SiteName, site.FeedUrl);
                var xml = await client.GetStringAsync(site.FeedUrl, ct);
                var articles = ParseFeed(xml);
                _logger.LogInformation("RSS {Site}: {Count} articles fetched", site.SiteName, articles.Count);
                result.Add(new SiteArticles(site.SiteName, site.SiteUrl, articles));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch RSS for {Site}", site.SiteName);
                result.Add(new SiteArticles(site.SiteName, site.SiteUrl, []));
            }
        }
        return result;
    }

    private static List<RssArticle> ParseFeed(string xml)
    {
        var articles = new List<RssArticle>();
        var doc = XDocument.Parse(xml);

        // RSS 2.0
        var items = doc.Descendants("item").Take(5).ToList();
        if (items.Count > 0)
        {
            foreach (var item in items)
            {
                var title = item.Element("title")?.Value?.Trim();
                var link  = item.Element("link")?.Value?.Trim();
                var desc  = item.Element("description")?.Value?.Trim();
                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(link))
                    articles.Add(new RssArticle(title, link, Truncate(StripHtml(desc), 120)));
            }
            return articles;
        }

        // Atom 1.0
        XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        foreach (var entry in doc.Descendants(ns + "entry").Take(5))
        {
            var title   = entry.Element(ns + "title")?.Value?.Trim();
            var linkEl  = entry.Elements(ns + "link").FirstOrDefault(x => x.Attribute("rel")?.Value == "alternate")
                          ?? entry.Element(ns + "link");
            var link    = linkEl?.Attribute("href")?.Value?.Trim()
                          ?? linkEl?.Value?.Trim();
            var desc    = (entry.Element(ns + "summary") ?? entry.Element(ns + "content"))?.Value?.Trim();
            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(link))
                articles.Add(new RssArticle(title, link, Truncate(StripHtml(desc), 120)));
        }
        return articles;
    }

    private string BuildNewsContent(string dateStr, List<SiteArticles> sites, NewsNarrative? narrative)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## 本日のIT業界トレンド概要");
        sb.AppendLine(narrative?.TrendOverview ?? "本日の日本IT業界における最新ニュースをお届けします。");
        sb.AppendLine();

        sb.AppendLine("## 📰 各サイト注目トピック");
        sb.AppendLine();

        var emojis = new Dictionary<string, string>
        {
            ["Zenn"] = "🔷", ["Qiita"] = "🟢", ["DevelopersIO"] = "🔶", ["gihyo.jp"] = "📖"
        };

        foreach (var site in sites)
        {
            var emoji = emojis.GetValueOrDefault(site.SiteName, "🔹");
            sb.AppendLine($"### {emoji} {site.SiteName}");

            if (site.Articles.Count > 0)
            {
                foreach (var a in site.Articles.Take(3))
                {
                    sb.AppendLine($"- **[{a.Title}]({a.Url})**");
                    if (!string.IsNullOrEmpty(a.Description))
                        sb.AppendLine($"  {a.Description}");
                }
            }
            else
            {
                sb.AppendLine($"- 本日の記事取得に失敗しました。[{site.SiteName} トップページ]({site.SiteUrl}) をご覧ください。");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## 💡 開発者・エンジニアへの示唆");
        if (narrative?.DeveloperInsights?.Count > 0)
            foreach (var ins in narrative.DeveloperInsights)
                sb.AppendLine($"- {ins}");
        else
            sb.AppendLine("- 最新の技術動向に注目してください。");
        sb.AppendLine();

        sb.AppendLine("## まとめ");
        sb.AppendLine(narrative?.Summary ?? "引き続き日本IT業界の動向を注視してください。");

        return sb.ToString();
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Trim();
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? null : (s.Length <= max ? s : s[..max] + "…");

    private class BlogArticle
    {
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Content { get; set; } = "";
        public string Slug { get; set; } = "";
    }

    private class NewsNarrative
    {
        public string? TrendOverview { get; set; }
        public List<string>? DeveloperInsights { get; set; }
        public string? Summary { get; set; }
    }
}
