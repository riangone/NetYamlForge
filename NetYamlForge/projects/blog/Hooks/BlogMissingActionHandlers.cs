// 自動生成スタブ実装: NetYamlForge.Projects.Blog.Hooks
using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;
using Dapper;

namespace NetYamlForge.Projects.Blog.Hooks;

public sealed class ApproveCommentHandler : ICustomActionHandler
{
    private readonly ILogger<ApproveCommentHandler> _logger;
    public string Name => "approve_comment";

    public ApproveCommentHandler(ILogger<ApproveCommentHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (int.TryParse(ctx.RecordId, out int id))
        {
            await db.ExecuteAsync("UPDATE Comment SET Status = 'approved' WHERE Id = @Id", new { Id = id }, tx);
            _logger.LogInformation("Approved comment {Id}", id);
            return ActionHandlerResult.Success();
        }
        return ActionHandlerResult.Failure("Invalid RecordId");
    }
}

public sealed class ArchivePostHandler : ICustomActionHandler
{
    private readonly ILogger<ArchivePostHandler> _logger;
    public string Name => "archive_post";

    public ArchivePostHandler(ILogger<ArchivePostHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (int.TryParse(ctx.RecordId, out int id))
        {
            await db.ExecuteAsync("UPDATE Post SET Status = 'archived' WHERE Id = @Id", new { Id = id }, tx);
            _logger.LogInformation("Archived post {Id}", id);
            return ActionHandlerResult.Success();
        }
        return ActionHandlerResult.Failure("Invalid RecordId");
    }
}

public sealed class BulkApproveCommentsHandler : ICustomActionHandler
{
    private readonly ILogger<BulkApproveCommentsHandler> _logger;
    public string Name => "bulk_approve_comments";

    public BulkApproveCommentsHandler(ILogger<BulkApproveCommentsHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var count = await db.ExecuteAsync("UPDATE Comment SET Status = 'approved' WHERE Status = 'pending'", null, tx);
        _logger.LogInformation("Bulk approved {Count} comments", count);
        return ActionHandlerResult.Success();
    }
}

public sealed class BulkDeleteSpamCommentsHandler : ICustomActionHandler
{
    private readonly ILogger<BulkDeleteSpamCommentsHandler> _logger;
    public string Name => "bulk_delete_spam_comments";

    public BulkDeleteSpamCommentsHandler(ILogger<BulkDeleteSpamCommentsHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var count = await db.ExecuteAsync("DELETE FROM Comment WHERE Status = 'spam'", null, tx);
        _logger.LogInformation("Bulk deleted {Count} spam comments", count);
        return ActionHandlerResult.Success();
    }
}

public sealed class MarkCommentSpamHandler : ICustomActionHandler
{
    private readonly ILogger<MarkCommentSpamHandler> _logger;
    public string Name => "mark_comment_spam";

    public MarkCommentSpamHandler(ILogger<MarkCommentSpamHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (int.TryParse(ctx.RecordId, out int id))
        {
            await db.ExecuteAsync("UPDATE Comment SET Status = 'spam' WHERE Id = @Id", new { Id = id }, tx);
            _logger.LogInformation("Marked comment {Id} as spam", id);
            return ActionHandlerResult.Success();
        }
        return ActionHandlerResult.Failure("Invalid RecordId");
    }
}

public sealed class PublishPostHandler : ICustomActionHandler
{
    private readonly ILogger<PublishPostHandler> _logger;
    public string Name => "publish_post";

    public PublishPostHandler(ILogger<PublishPostHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (int.TryParse(ctx.RecordId, out int id))
        {
            await db.ExecuteAsync("UPDATE Post SET Status = 'published', PublishedAt = @Now WHERE Id = @Id", new { Id = id, Now = DateTime.UtcNow }, tx);
            _logger.LogInformation("Published post {Id}", id);
            return ActionHandlerResult.Success();
        }
        return ActionHandlerResult.Failure("Invalid RecordId");
    }
}

public sealed class UnpublishPostHandler : ICustomActionHandler
{
    private readonly ILogger<UnpublishPostHandler> _logger;
    public string Name => "unpublish_post";

    public UnpublishPostHandler(ILogger<UnpublishPostHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (int.TryParse(ctx.RecordId, out int id))
        {
            await db.ExecuteAsync("UPDATE Post SET Status = 'draft', PublishedAt = NULL WHERE Id = @Id", new { Id = id }, tx);
            _logger.LogInformation("Unpublished post {Id}", id);
            return ActionHandlerResult.Success();
        }
        return ActionHandlerResult.Failure("Invalid RecordId");
    }
}
