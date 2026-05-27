// 自動生成スタブ実装: NetYamlForge.Projects.Blog.Hooks
using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Blog.Hooks;

public sealed class AuditLogHook : IEntityHook
{
    private readonly ILogger<AuditLogHook> _logger;
    public string Name => "audit_log";

    public AuditLogHook(ILogger<AuditLogHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        _logger.LogInformation("AuditLog: Action on entity {Entity} with ID {Id} by {UserName}", ctx.Entity, ctx.Id, ctx.UserName);
        return Task.CompletedTask;
    }
}

public sealed class GenerateSlugHook : IEntityHook
{
    private readonly ILogger<GenerateSlugHook> _logger;
    public string Name => "generate_slug";

    public GenerateSlugHook(ILogger<GenerateSlugHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Title", out var titleObj) && titleObj is string title)
        {
            if (!ctx.Values.TryGetValue("Slug", out var slugObj) || string.IsNullOrWhiteSpace(slugObj as string))
            {
                var slug = title.ToLower().Replace(" ", "-").Replace("?", "").Replace("!", "").Replace("/", "-");
                ctx.Values["Slug"] = slug;
                _logger.LogInformation("Generated slug: {Slug}", slug);
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class SanitizeContentHook : IEntityHook
{
    private readonly ILogger<SanitizeContentHook> _logger;
    public string Name => "sanitize_content";

    public SanitizeContentHook(ILogger<SanitizeContentHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Content", out var contentObj) && contentObj is string content)
        {
            // Simple XSS sanitization stub
            ctx.Values["Content"] = content.Replace("<script>", "").Replace("</script>", "");
            _logger.LogInformation("Sanitized content");
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

public sealed class SendCommentNotifyHook : IEntityHook
{
    private readonly ILogger<SendCommentNotifyHook> _logger;
    public string Name => "send_comment_notify";

    public SendCommentNotifyHook(ILogger<SendCommentNotifyHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("PostId", out var postId))
        {
            _logger.LogInformation("Sending notification for new comment on PostId: {PostId}", postId);
        }
        return Task.CompletedTask;
    }
}

public sealed class SetPublishedAtHook : IEntityHook
{
    private readonly ILogger<SetPublishedAtHook> _logger;
    public string Name => "set_published_at";

    public SetPublishedAtHook(ILogger<SetPublishedAtHook> logger) => _logger = logger;

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Status", out var statusObj) && statusObj?.ToString() == "published")
        {
            if (!ctx.Values.ContainsKey("PublishedAt") || ctx.Values["PublishedAt"] == null)
            {
                ctx.Values["PublishedAt"] = DateTime.UtcNow;
                _logger.LogInformation("Set PublishedAt to current UTC time");
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
