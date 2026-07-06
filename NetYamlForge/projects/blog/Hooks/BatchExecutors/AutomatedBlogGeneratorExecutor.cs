using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Dapper;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.BatchJob.Sdk;

namespace NetYamlForge.Projects.Blog.Hooks;

public class BlogJobInput
{
    public string JobId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string Target { get; set; } = "";
    public int CategoryId { get; set; }
    public string TagNamesStr { get; set; } = "";
    public string Author { get; set; } = "";
    public string Language { get; set; } = "";
    public string DateStr { get; set; } = "";
    public string Prompt { get; set; } = "";
    public bool IsNews { get; set; }
    public List<SiteArticles>? NewsSites { get; set; }
}

public class BlogJobResult
{
    public BlogArticle? Article { get; set; }
    public NewsNarrative? Narrative { get; set; }
}

public class BlogArticle
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Content { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class NewsNarrative
{
    public string? TrendOverview { get; set; }
    public List<string>? DeveloperInsights { get; set; }
    public string? Summary { get; set; }
}

public record RssArticle(string Title, string Url, string? Description);
public record SiteInfo(string SiteName, string SiteUrl, string FeedUrl);
public record SiteArticles(string SiteName, string SiteUrl, List<RssArticle> Articles);

/// <summary>
/// AI を使用してブログ記事を自動生成し、データベースに保存するジョブ実行器。
/// </summary>
public class AutomatedBlogGeneratorExecutor : ProjectBatchExecutorBase<BlogJobInput, BlogJobResult>
{
    public override string StepType => "automated_blog_generator";

    private static readonly SiteInfo[] JapanNewsSites =
    [
        new("Zenn",         "https://zenn.dev/",              "https://zenn.dev/feed"),
        new("Qiita",        "https://qiita.com/",             "https://qiita.com/popular-items/feed.atom"),
        new("DevelopersIO", "https://dev.classmethod.jp/",    "https://dev.classmethod.jp/feed/"),
        new("gihyo.jp",     "https://gihyo.jp/",              "https://gihyo.jp/feed/atom"),
    ];

    private static readonly SiteInfo[] ChinaHardcoreNewsSites =
    [
        new("博客园新闻",   "https://news.cnblogs.com/",      "http://feed.cnblogs.com/news/rss"),
        new("开源中国",     "https://www.oschina.net/",       "https://www.oschina.net/news/rss"),
        new("Solidot",      "https://www.solidot.org/",       "https://www.solidot.org/index.rss"),
        new("美团技术团队", "https://tech.meituan.com/",      "https://tech.meituan.com/feed")
    ];

    private static readonly SiteInfo[] ChinaGeneralNewsSites =
    [
        new("36氪",         "https://36kr.com/",              "https://36kr.com/feed"),
        new("少数派",       "https://sspai.com/",             "https://sspai.com/feed"),
        new("IT之家",       "https://www.ithome.com/",        "https://www.ithome.com/rss/"),
        new("极客公园",     "http://www.geekpark.net/",       "http://www.geekpark.net/rss")
    ];

    public AutomatedBlogGeneratorExecutor(ICliChainService cliChain, ILogger<AutomatedBlogGeneratorExecutor> logger) 
        : base(cliChain, logger)
    {
    }

    protected override async Task<BlogJobInput?> LoadInputAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var target = job.Settings.Params?.GetValueOrDefault("target") ?? "京王電鉄9008";
        var categoryIdStr = job.Settings.Params?.GetValueOrDefault("categoryId") ?? "1";
        var tagNamesStr = job.Settings.Params?.GetValueOrDefault("tags") ?? "";
        var author = job.Settings.Params?.GetValueOrDefault("author") ?? "AI Analyst";
        var language = job.Settings.Params?.GetValueOrDefault("language") ?? "ja-JP";
        var customPrompt = job.Settings.Params?.GetValueOrDefault("prompt");

        int.TryParse(categoryIdStr, out var categoryId);

        var nowJst = TimeZoneInfo.ConvertTime(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        var dateStr = nowJst.ToString("yyyy年MM月dd日");

        var isNews = job.Id == "japan_it_news_briefing" || job.Id == "china_it_news_hardcore" || job.Id == "china_it_news_general";

        var input = new BlogJobInput
        {
            JobId = job.Id,
            ProjectName = projectName ?? "",
            Target = target,
            CategoryId = categoryId,
            TagNamesStr = tagNamesStr,
            Author = author,
            Language = language,
            DateStr = dateStr,
            IsNews = isNews
        };

        if (isNews)
        {
            var newsSites = job.Id switch
            {
                "china_it_news_hardcore" => ChinaHardcoreNewsSites,
                "china_it_news_general" => ChinaGeneralNewsSites,
                _ => JapanNewsSites
            };

            Logger.LogInformation("{JobId}: fetching RSS feeds...", job.Id);
            var sites = await FetchRssArticlesAsync(newsSites, ct);
            input.NewsSites = sites;

            var articleListing = new StringBuilder();
            foreach (var site in sites)
            {
                articleListing.AppendLine($"【{site.SiteName}】");
                if (site.Articles.Any())
                    foreach (var a in site.Articles.Take(3))
                        articleListing.AppendLine($"  - {a.Title}");
                else
                    articleListing.AppendLine("  (記事取得失敗/无内容)");
            }

            input.Prompt = job.Id.StartsWith("china_") ?
                $@"你是精通中国科技、IT行业及开发社区的资深科技作家。
本日（{dateStr}）在各大技术媒体与社区上公开了以下文章：

{articleListing}

请生成以下 3 个部分的 JSON 内容，绝对不要在任何内容中生成 URL：

1. trend_overview: 今日中国IT/科技行业整体趋势概述（2〜3句话，中文）
2. developer_insights: 给开发者与技术人员的启示 3点（每点 30〜50 字，以字符串数组形式输出）
3. summary: 总结（2〜3句话，中文）

输出格式必须是以下 JSON 且不包含任何其他文本：
{{
  ""trend_overview"": ""..."",
  ""developer_insights"": [""启示1"", ""启示2"", ""启示3""],
  ""summary"": ""...""
}}"
                :
                $@"あなたは日本のIT業界に精通したテクノロジーライターです。
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
        }
        else
        {
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
}}";
            }
            else
            {
                prompt = prompt
                    .Replace("{target}", target)
                    .Replace("{language}", language)
                    .Replace("{date}", dateStr);
            }
            input.Prompt = prompt;
        }

        return input;
    }

    protected override string BuildPrompt(BlogJobInput input) => input.Prompt;

    protected override BlogJobResult? ParseResult(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // ニュース系ジョブは trend_overview を含む NewsNarrative、それ以外は BlogArticle として解釈する。
        if (raw.Contains("trend_overview", StringComparison.OrdinalIgnoreCase))
        {
            if (AiResultParser.TryParseJson<NewsNarrative>(raw, out var narrative, out var narrErr))
                return new BlogJobResult { Narrative = narrative };
            Logger.LogError("Failed to parse AI narrative response: {Error} / {Raw}", narrErr, raw);
            return null;
        }

        if (AiResultParser.TryParseJson<BlogArticle>(raw, out var article, out var artErr))
            return new BlogJobResult { Article = article };

        Logger.LogError("Failed to parse AI article response: {Error} / {Raw}", artErr, raw);
        return null;
    }

    protected override async Task PersistAsync(
        BlogJobInput input, BlogJobResult result,
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        BlogArticle? blogData = result.Article;

        if (input.IsNews)
        {
            var content = BuildNewsContent(input.DateStr, input.NewsSites ?? new(), result.Narrative, input.JobId);

            string blogTitle = input.JobId switch
            {
                "china_it_news_hardcore" => $"【IT简报】{input.DateStr} 中国IT硬核技术每日速递",
                "china_it_news_general" => $"【IT简报】{input.DateStr} 中国科技与IT行业综合资讯",
                _ => $"【IT簡報】{input.DateStr} 日本IT業界の注目ニュース"
            };

            string blogSummary = input.JobId switch
            {
                "china_it_news_hardcore" => "博客园、开源中国、Solidot、美团技术团队今日中国硬核技术与开源简报",
                "china_it_news_general" => "36氪、少数派、IT之家、极客公园今日科技动态与数字化生活资讯",
                _ => "Zenn・Qiita・DevelopersIO・gihyo.jp から本日の日本IT業界注目トピックをお届けします"
            };

            string blogSlug = input.JobId switch
            {
                "china_it_news_hardcore" => "china-it-news-hardcore",
                "china_it_news_general" => "china-it-news-general",
                _ => "it-news-briefing"
            };

            blogData = new BlogArticle
            {
                Title = blogTitle,
                Summary = blogSummary,
                Content = content,
                Slug = blogSlug
            };
        }

        if (blogData == null || string.IsNullOrEmpty(blogData.Content))
        {
            throw new Exception("AI failed to generate blog article or returned invalid format.");
        }

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
            CategoryId = input.CategoryId,
            AuthorName = input.Author,
            Now = DateTime.UtcNow
        });

        if (!string.IsNullOrEmpty(input.TagNamesStr))
        {
            var tagNames = input.TagNamesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tagName in tagNames)
            {
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

                await db.ExecuteAsync(
                    "INSERT OR IGNORE INTO PostTag (PostId, TagId) VALUES (@PostId, @TagId)",
                    new { PostId = postId, TagId = tagId.Value });
            }
        }

        Logger.LogInformation("Blog article created successfully: {Title} (ID: {PostId})", blogData.Title, postId);
    }

    private async Task<List<SiteArticles>> FetchRssArticlesAsync(SiteInfo[] newsSites, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");

        var result = new List<SiteArticles>();
        foreach (var site in newsSites)
        {
            try
            {
                Logger.LogInformation("Fetching RSS: {Site} -> {Url}", site.SiteName, site.FeedUrl);
                var xml = await client.GetStringAsync(site.FeedUrl, ct);
                var articles = ParseFeed(xml);
                Logger.LogInformation("RSS {Site}: {Count} articles fetched", site.SiteName, articles.Count);
                result.Add(new SiteArticles(site.SiteName, site.SiteUrl, articles));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to fetch RSS for {Site}", site.SiteName);
                result.Add(new SiteArticles(site.SiteName, site.SiteUrl, []));
            }
        }
        return result;
    }

    private static List<RssArticle> ParseFeed(string xml)
    {
        var articles = new List<RssArticle>();
        var doc = XDocument.Parse(xml);

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

    private string BuildNewsContent(string dateStr, List<SiteArticles> sites, NewsNarrative? narrative, string jobId)
    {
        var sb = new StringBuilder();
        bool isChina = jobId.StartsWith("china_");

        string titleOverview = isChina ? "## 今日IT/科技行业趋势概要" : "## 本日のIT業界トレンド概要";
        string fallbackOverview = isChina ? "今日中国IT/科技行业的最新消息与技术动态。" : "本日の日本IT業界における最新ニュースをお届けします。";
        string titleTopics = isChina ? "## 📰 媒体与社区最新动态" : "## 📰 各サイト注目トピック";
        string fallbackFetchError = isChina ? "今日文章获取失败。请访问" : "本日の記事取得に失敗しました。";
        string titleInsights = isChina ? "## 💡 行业启示与技术洞察" : "## 💡 開発者・エンジニアへの示唆";
        string fallbackInsights = isChina ? "关注最新技术动向与行业趋势。" : "最新の技術動向に注目してください。";
        string titleSummary = isChina ? "## 总结" : "## まとめ";
        string fallbackSummary = isChina ? "请继续关注中国IT与科技领域的最新发展。" : "引き続き日本IT業界の動向を注視してください。";

        sb.AppendLine(titleOverview);
        sb.AppendLine(narrative?.TrendOverview ?? fallbackOverview);
        sb.AppendLine();

        sb.AppendLine(titleTopics);
        sb.AppendLine();

        var emojis = new Dictionary<string, string>
        {
            ["Zenn"] = "🔷", ["Qiita"] = "🟢", ["DevelopersIO"] = "🔶", ["gihyo.jp"] = "📖",
            ["博客园新闻"] = "💻", ["开源中国"] = "🚀", ["Solidot"] = "🔒", ["美团技术团队"] = "🍔",
            ["36氪"] = "🦄", ["少数派"] = "💡", ["IT之家"] = "🏠", ["极客公园"] = "🌲"
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
                if (isChina)
                {
                    sb.AppendLine($"- {fallbackFetchError} [{site.SiteName} 官方网站]({site.SiteUrl}) 查看最新内容。");
                }
                else
                {
                    sb.AppendLine($"- {fallbackFetchError} [{site.SiteName} トップページ]({site.SiteUrl}) をご覧ください。");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine(titleInsights);
        if (narrative?.DeveloperInsights?.Count > 0)
            foreach (var ins in narrative.DeveloperInsights)
                sb.AppendLine($"- {ins}");
        else
            sb.AppendLine($"- {fallbackInsights}");
        sb.AppendLine();

        sb.AppendLine(titleSummary);
        sb.AppendLine(narrative?.Summary ?? fallbackSummary);

        return sb.ToString();
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Trim();
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? null : (s.Length <= max ? s : s[..max] + "…");
}
