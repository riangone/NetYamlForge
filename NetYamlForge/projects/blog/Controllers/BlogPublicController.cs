// ブログ公開コントローラー：ログイン不要のコメント投稿エンドポイントを提供します。
using System.Data;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services;

namespace NetYamlForge.Projects.Blog.Controllers;

[AllowAnonymous]
[Route("{project}/BlogPublic")]
public sealed class BlogPublicController : Controller
{
    private readonly IDbConnection _db;
    private readonly ILogger<BlogPublicController> _logger;
    private readonly ProjectManager _projectManager;
    private readonly ProjectScope _projectScope;

    public BlogPublicController(
        IDbConnection db,
        ILogger<BlogPublicController> logger,
        ProjectManager projectManager,
        ProjectScope projectScope)
    {
        _db = db;
        _logger = logger;
        _projectManager = projectManager;
        _projectScope = projectScope;
    }

    /// <summary>
    /// ブログの公開トップページへのショートカット (/blog)
    /// </summary>
    [HttpGet("/blog")]
    public IActionResult Index()
    {
        // プロジェクトスコープを強制的に blog に設定
        if (!_projectScope.IsSet && _projectManager.TryGet("blog", out var blogProject))
        {
            _projectScope.Set(blogProject!);
        }
        
        // 公開用の BlogHome ページへリダイレクト
        // クエリ文字列を保持するように修正
        var queryString = Request.QueryString.Value;
        return Redirect(Url.Content("~/blog/Page/BlogHome" + queryString));
    }

    [HttpPost("IncrementViewCount")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> IncrementViewCount([FromForm] int postId)
    {
        try
        {
            await _db.ExecuteAsync(
                "UPDATE Post SET ViewCount = COALESCE(ViewCount, 0) + 1 WHERE Id = @Id",
                new { Id = postId });
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment view count for post {PostId}", postId);
            return StatusCode(500);
        }
    }

    // POST /{project}/BlogPublic/SubmitComment
    [HttpPost("SubmitComment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitComment(
        [FromForm] int postId,
        [FromForm] string? authorName,
        [FromForm] string? authorEmail,
        [FromForm] string? content)
    {
        var project = RouteData.Values["project"]?.ToString() ?? "blog";
        var returnUrl = $"/{project}/Page/PostDetail?id={postId}#comments";

        if (string.IsNullOrWhiteSpace(authorName))
        {
            authorName = "匿名用户";
        }
        else if (authorName.Trim().Length > 100)
        {
            TempData["CommentError"] = "名称请控制在100字符以内。";
            return Redirect(returnUrl);
        }

        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 2000)
        {
            TempData["CommentError"] = "请输入评论内容（2000字以内）。";
            return Redirect(returnUrl);
        }

        // 投稿先の記事が公開されているか確認
        var postExists = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Post WHERE Id = @Id AND Status = 'published'",
            new { Id = postId });

        if (postExists == 0)
        {
            TempData["CommentError"] = "コメント先の記事が見つかりません。";
            return Redirect(returnUrl);
        }

        try
        {
            await _db.ExecuteAsync(
                @"INSERT INTO Comment (PostId, AuthorName, AuthorEmail, Content, Status, CreatedAt)
                  VALUES (@PostId, @AuthorName, @AuthorEmail, @Content, 'pending', datetime('now'))",
                new
                {
                    PostId      = postId,
                    AuthorName  = authorName.Trim(),
                    AuthorEmail = string.IsNullOrWhiteSpace(authorEmail) ? null : authorEmail.Trim(),
                    Content     = content.Trim()
                });

            TempData["CommentSuccess"] = "コメントを送信しました。管理者による承認後に公開されます。";
            _logger.LogInformation("Comment submitted for post {PostId} by {AuthorName}", postId, authorName.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save comment for post {PostId}", postId);
            TempData["CommentError"] = "コメントの送信に失敗しました。しばらくしてから再度お試しください。";
        }

        return Redirect(returnUrl);
    }
}
