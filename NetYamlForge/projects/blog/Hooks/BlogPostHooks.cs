// Blog プロジェクト固有のエンティティフック実装
// ブログ CMS 固有の CRUD 前後処理を定義します。

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Blog.Hooks;

/// <summary>
/// Blog 固有：記事作成時にスラッグを自動生成するフック。
/// entities.yml の hooks.beforeCreate で使用します。
/// </summary>
public class BlogPostSlugGeneratorHook : IEntityHook
{
    private readonly ILogger<BlogPostSlugGeneratorHook> _logger;

    public string Name => "blog_post_slug_generator";

    public BlogPostSlugGeneratorHook(ILogger<BlogPostSlugGeneratorHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return Task.FromResult(HookResult.Continue());

        // タイトルからスラッグを自動生成
        if (ctx.Values.TryGetValue("title", out var titleObj) && titleObj is string title)
        {
            if (!ctx.Values.ContainsKey("slug") || string.IsNullOrEmpty(ctx.Values["slug"]?.ToString()))
            {
                var slug = GenerateSlug(title);
                ctx.Values["slug"] = slug;
                _logger.LogInformation(
                    "[Blog] 記事タイトル「{Title}」からスラッグ「{Slug}」を自動生成しました",
                    title, slug);
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }

    private static string GenerateSlug(string title)
    {
        return title.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("--", "-")
            .Trim('-');
    }
}

/// <summary>
/// Blog 固有：記事公開時に通知を送信するフック。
/// entities.yml の hooks.afterCreate で使用します。
/// </summary>
public class BlogPostPublishedNotificationHook : IEntityHook
{
    private readonly ILogger<BlogPostPublishedNotificationHook> _logger;

    public string Name => "blog_post_published_notification";

    public BlogPostPublishedNotificationHook(ILogger<BlogPostPublishedNotificationHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.FromResult(HookResult.Continue());
    }

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return;

        // 記事が公開状態の場合、通知を送信
        if (ctx.Values.TryGetValue("status", out var statusObj) && statusObj?.ToString() == "published")
        {
            if (ctx.Id is int postId)
            {
                var sql = "SELECT title, author FROM posts WHERE post_id = @PostId";
                var post = await (tx != null ?
                    db.QueryFirstOrDefaultAsync<PostRow>(sql, new { PostId = postId }, tx) :
                    db.QueryFirstOrDefaultAsync<PostRow>(sql, new { PostId = postId }));

                if (post != null)
                {
                    _logger.LogInformation(
                        "[Blog] 記事「{Title}」（著者：{Author}）が公開されました。通知を送信します。",
                        post.Title, post.Author);
                }
            }
        }

        await Task.CompletedTask;
    }

    private class PostRow
    {
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
    }
}

/// <summary>
/// Blog 固有：コメント作成時にスパムチェックを行うフック。
/// entities.yml の hooks.beforeCreate で使用します。
/// </summary>
public class BlogCommentSpamCheckHook : IEntityHook
{
    private readonly ILogger<BlogCommentSpamCheckHook> _logger;

    public string Name => "blog_comment_spam_check";

    public BlogCommentSpamCheckHook(ILogger<BlogCommentSpamCheckHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return Task.FromResult(HookResult.Continue());

        // コメント内容にスパムキーワードが含まれていないかチェック
        if (ctx.Values.TryGetValue("content", out var contentObj) && contentObj is string content)
        {
            var spamKeywords = new[] { "viagra", "casino", "lottery", "免费", "抽奖" };
            foreach (var keyword in spamKeywords)
            {
                if (content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "[Blog] コメントにスパムキーワード「{Keyword}」が含まれていました",
                        keyword);
                    return Task.FromResult(HookResult.Abort("スパムキーワードが含まれています。"));
                }
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
