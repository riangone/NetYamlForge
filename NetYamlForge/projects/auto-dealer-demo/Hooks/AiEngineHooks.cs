using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

public sealed class LinkGuestSessionsHook : IEntityHook
{
    private readonly ILogger<LinkGuestSessionsHook> _logger;
    public string Name => "link_guest_sessions";
    public LinkGuestSessionsHook(ILogger<LinkGuestSessionsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class SetCommunicationTimestampsHook : IEntityHook
{
    private readonly ILogger<SetCommunicationTimestampsHook> _logger;
    public string Name => "set_communication_timestamps";
    public SetCommunicationTimestampsHook(ILogger<SetCommunicationTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Data["created_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Task.FromResult(HookResult.Continue());
    }
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class UpdateCommunicationTimestampsHook : IEntityHook
{
    private readonly ILogger<UpdateCommunicationTimestampsHook> _logger;
    public string Name => "update_communication_timestamps";
    public UpdateCommunicationTimestampsHook(ILogger<UpdateCommunicationTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class SetDecisionTimestampsHook : IEntityHook
{
    private readonly ILogger<SetDecisionTimestampsHook> _logger;
    public string Name => "set_decision_timestamps";
    public SetDecisionTimestampsHook(ILogger<SetDecisionTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Data["created_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (string.IsNullOrEmpty(ctx.Data.GetValueOrDefault("decision_id")?.ToString()))
            ctx.Data["decision_id"] = "DEC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
        return Task.FromResult(HookResult.Continue());
    }
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class UpdateDecisionTimestampsHook : IEntityHook
{
    private readonly ILogger<UpdateDecisionTimestampsHook> _logger;
    public string Name => "update_decision_timestamps";
    public UpdateDecisionTimestampsHook(ILogger<UpdateDecisionTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class SetQuoteTimestampsHook : IEntityHook
{
    private readonly ILogger<SetQuoteTimestampsHook> _logger;
    public string Name => "set_quote_timestamps";
    public SetQuoteTimestampsHook(ILogger<SetQuoteTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Data["created_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ctx.Data["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Task.FromResult(HookResult.Continue());
    }
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class UpdateQuoteTimestampsHook : IEntityHook
{
    private readonly ILogger<UpdateQuoteTimestampsHook> _logger;
    public string Name => "update_quote_timestamps";
    public UpdateQuoteTimestampsHook(ILogger<UpdateQuoteTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Data["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Task.FromResult(HookResult.Continue());
    }
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class SyncThirdPartyToAuthUserHook : IEntityHook
{
    private readonly ILogger<SyncThirdPartyToAuthUserHook> _logger;
    public string Name => "sync_third_party_to_auth_user";
    public SyncThirdPartyToAuthUserHook(ILogger<SyncThirdPartyToAuthUserHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class ValidateThirdPartyEmailHook : IEntityHook
{
    private readonly ILogger<ValidateThirdPartyEmailHook> _logger;
    public string Name => "validate_third_party_email";
    public ValidateThirdPartyEmailHook(ILogger<ValidateThirdPartyEmailHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class ApproveAiDecisionHandler : ICustomActionHandler
{
    private readonly ILogger<ApproveAiDecisionHandler> _logger;
    public string Name => "approve_ai_decision";
    public ApproveAiDecisionHandler(ILogger<ApproveAiDecisionHandler> logger) => _logger = logger;
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("決定 ID が指定されていません。");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await db.ExecuteAsync("UPDATE ai_decisions SET status = 'approved', executed_at = @now WHERE decision_id = @id", new { now, id = ctx.RecordId }, tx);
        if (affected <= 0) return ActionHandlerResult.Failure("対象の AI 決定が見つかりません。");
        var decision = await db.QueryFirstOrDefaultAsync("SELECT entity_type, entity_id FROM ai_decisions WHERE decision_id = @id", new { id = ctx.RecordId }, tx) as IDictionary<string, object>;
        if (decision != null)
        {
            var entityType = decision.ContainsKey("entity_type") ? decision["entity_type"]?.ToString() : null;
            var entityId = decision.ContainsKey("entity_id") ? decision["entity_id"]?.ToString() : null;
            if (entityType == "ai_quotes") await db.ExecuteAsync("UPDATE ai_quotes SET status = 'approved' WHERE quote_id = @id", new { id = entityId }, tx);
            else if (entityType == "lead_nurturing_tasks") await db.ExecuteAsync("UPDATE lead_nurturing_tasks SET status = 'in_progress' WHERE task_id = @id", new { id = entityId }, tx);
        }
        return ActionHandlerResult.Success();
    }
}

public sealed class RejectAiDecisionHandler : ICustomActionHandler
{
    private readonly ILogger<RejectAiDecisionHandler> _logger;
    public string Name => "reject_ai_decision";
    public RejectAiDecisionHandler(ILogger<RejectAiDecisionHandler> logger) => _logger = logger;
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("決定 ID が指定されていません。");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await db.ExecuteAsync("UPDATE ai_decisions SET status = 'rejected', executed_at = @now WHERE decision_id = @id", new { now, id = ctx.RecordId }, tx);
        if (affected <= 0) return ActionHandlerResult.Failure("対象の AI 決定が見つかりません。");
        var decision = await db.QueryFirstOrDefaultAsync("SELECT entity_type, entity_id FROM ai_decisions WHERE decision_id = @id", new { id = ctx.RecordId }, tx) as IDictionary<string, object>;
        if (decision != null)
        {
            var entityType = decision.ContainsKey("entity_type") ? decision["entity_type"]?.ToString() : null;
            var entityId = decision.ContainsKey("entity_id") ? decision["entity_id"]?.ToString() : null;
            if (entityType == "ai_quotes") await db.ExecuteAsync("UPDATE ai_quotes SET status = 'rejected' WHERE quote_id = @id", new { id = entityId }, tx);
            else if (entityType == "lead_nurturing_tasks") await db.ExecuteAsync("UPDATE lead_nurturing_tasks SET status = 'cancelled' WHERE task_id = @id", new { id = entityId }, tx);
        }
        return ActionHandlerResult.Success();
    }
}

public sealed class ApproveQuoteHandler : ICustomActionHandler
{
    private readonly ILogger<ApproveQuoteHandler> _logger;
    public string Name => "approve_quote";
    public ApproveQuoteHandler(ILogger<ApproveQuoteHandler> logger) => _logger = logger;
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("見積 ID が指定されていません。");
        var affected = await db.ExecuteAsync("UPDATE ai_quotes SET status = 'approved' WHERE quote_id = @id", new { id = ctx.RecordId }, tx);
        return affected <= 0 ? ActionHandlerResult.Failure("対象の AI 見積が見つかりません。") : ActionHandlerResult.Success();
    }
}

public sealed class RejectQuoteHandler : ICustomActionHandler
{
    private readonly ILogger<RejectQuoteHandler> _logger;
    public string Name => "reject_quote";
    public RejectQuoteHandler(ILogger<RejectQuoteHandler> logger) => _logger = logger;
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("見積 ID が指定されていません。");
        var affected = await db.ExecuteAsync("UPDATE ai_quotes SET status = 'rejected' WHERE quote_id = @id", new { id = ctx.RecordId }, tx);
        return affected <= 0 ? ActionHandlerResult.Failure("対象 of AI 見積が見つかりません。") : ActionHandlerResult.Success();
    }
}

public sealed class AssignHandoverToMeHandler : ICustomActionHandler
{
    public string Name => "assign_handover_to_me";
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("No ID");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync("UPDATE ai_handovers SET assigned_to_user_id = @user, status = 'in_progress', assigned_at = @now WHERE handover_id = @id",
            new { user = ctx.UserName ?? "system", now, id = ctx.RecordId }, tx);
        return ActionHandlerResult.Success();
    }
}

public sealed class ResolveHandoverHandler : ICustomActionHandler
{
    public string Name => "resolve_handover";
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("No ID");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync("UPDATE ai_handovers SET status = 'resolved', resolved_at = @now WHERE handover_id = @id", new { now, id = ctx.RecordId }, tx);
        return ActionHandlerResult.Success();
    }
}
