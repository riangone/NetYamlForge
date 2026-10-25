using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ApprovalFlow.Hooks;

/// <summary>
/// 审批通过动作处理器 (approve_request_handler)
/// </summary>
public sealed class ApproveRequestHandler : ICustomActionHandler
{
    public string Name => "approve_request_handler";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!int.TryParse(ctx.RecordId, out var id))
        {
            return ActionHandlerResult.Failure("无效的审批单编号");
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var approver = !string.IsNullOrWhiteSpace(ctx.UserName) ? ctx.UserName : "审批负责人";

        var affected = await db.ExecuteAsync(
            "UPDATE approval_request SET status = 'approved', approver = @Approver, updated_at = @Now WHERE id = @Id",
            new { Id = id, Approver = approver, Now = now }, tx);

        if (affected == 0)
        {
            return ActionHandlerResult.Failure("未找到对应的审批申请单");
        }

        await db.ExecuteAsync(
            "INSERT INTO approval_log (request_id, operator, action, comment, created_at) VALUES (@RequestId, @Operator, 'approve', '审批通过', @CreatedAt)",
            new { RequestId = id, Operator = approver, CreatedAt = now }, tx);

        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// 驳回申请动作处理器 (reject_request_handler)
/// </summary>
public sealed class RejectRequestHandler : ICustomActionHandler
{
    public string Name => "reject_request_handler";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!int.TryParse(ctx.RecordId, out var id))
        {
            return ActionHandlerResult.Failure("无效的审批单编号");
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var approver = !string.IsNullOrWhiteSpace(ctx.UserName) ? ctx.UserName : "审批负责人";

        var affected = await db.ExecuteAsync(
            "UPDATE approval_request SET status = 'rejected', approver = @Approver, updated_at = @Now WHERE id = @Id",
            new { Id = id, Approver = approver, Now = now }, tx);

        if (affected == 0)
        {
            return ActionHandlerResult.Failure("未找到对应的审批申请单");
        }

        await db.ExecuteAsync(
            "INSERT INTO approval_log (request_id, operator, action, comment, created_at) VALUES (@RequestId, @Operator, 'reject', '单据已驳回', @CreatedAt)",
            new { RequestId = id, Operator = approver, CreatedAt = now }, tx);

        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// 撤回单据动作处理器 (withdraw_request_handler)
/// </summary>
public sealed class WithdrawRequestHandler : ICustomActionHandler
{
    public string Name => "withdraw_request_handler";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!int.TryParse(ctx.RecordId, out var id))
        {
            return ActionHandlerResult.Failure("无效的审批单编号");
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var operatorName = !string.IsNullOrWhiteSpace(ctx.UserName) ? ctx.UserName : "申请人";

        var affected = await db.ExecuteAsync(
            "UPDATE approval_request SET status = 'withdrawn', updated_at = @Now WHERE id = @Id AND status = 'pending'",
            new { Id = id, Now = now }, tx);

        if (affected == 0)
        {
            return ActionHandlerResult.Failure("仅处于待审批 (pending) 状态的单据方可撤回");
        }

        await db.ExecuteAsync(
            "INSERT INTO approval_log (request_id, operator, action, comment, created_at) VALUES (@RequestId, @Operator, 'withdraw', '申请人自主撤回单据', @CreatedAt)",
            new { RequestId = id, Operator = operatorName, CreatedAt = now }, tx);

        return ActionHandlerResult.Success();
    }
}
