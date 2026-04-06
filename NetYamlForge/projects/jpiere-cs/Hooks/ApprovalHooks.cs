using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.JpiereCs.Hooks;

/// <summary>
/// 発注書作成時の自動承認申請作成 Hook
/// </summary>
public class PurchaseOrderApprovalHook : IEntityHook
{
    public string Name => "purchase_order_approval";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("DocStatus", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "CO") return;
        if (!ctx.Values.TryGetValue("Id", out var idObj) || idObj == null) return;

        var orderId = Convert.ToInt32(idObj);
        var order = await db.QuerySingleAsync<Dictionary<string, object?>>(
            "SELECT * FROM purchase_orders WHERE id = @id", new { id = orderId }, tx);

        var grandTotal = DictHelper.Get<double>(order, "GrandTotal", "grand_total", 0.0);

        var existingApproval = await db.QueryFirstOrDefaultAsync<Dictionary<string, object?>>(
            "SELECT id FROM approval_requests WHERE source_table = 'purchase_orders' AND source_id = @orderId",
            new { orderId }, tx);
        if (existingApproval != null) return;

        int totalSteps;
        if (grandTotal < 100000)
        {
            await db.ExecuteAsync("UPDATE purchase_orders SET ApprovalStatus = 'APPROVED' WHERE id = @orderId", new { orderId }, tx);
            return;
        }
        else if (grandTotal < 1000000)
        {
            totalSteps = 1;
        }
        else
        {
            totalSteps = 2;
        }

        var requestId = await db.ExecuteScalarAsync<long>(@"
            INSERT INTO approval_requests (source_table, source_id, requester, current_step, total_steps, status, grand_total)
            VALUES ('purchase_orders', @orderId, @requester, 1, @totalSteps, 'PENDING', @grandTotal);
            SELECT last_insert_rowid()",
            new { orderId, requester = ctx.UserName ?? "system", totalSteps, grandTotal }, tx);

        if (totalSteps >= 1)
            await db.ExecuteAsync("INSERT INTO approval_steps (request_id, step_no, approver_role, label, status) VALUES (@requestId, 1, 'manager', '上長承認', 'PENDING')", new { requestId }, tx);

        if (totalSteps >= 2)
            await db.ExecuteAsync("INSERT INTO approval_steps (request_id, step_no, approver_role, label, status) VALUES (@requestId, 2, 'director', '取締役承認', 'PENDING')", new { requestId }, tx);

        await db.ExecuteAsync("UPDATE purchase_orders SET ApprovalStatus = 'PENDING' WHERE id = @orderId", new { orderId }, tx);
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());
}

/// <summary>
/// 承認ステップ確定時の処理
/// </summary>
public class ApprovalStepCompleteHook : IEntityHook
{
    public string Name => "approval_step_complete";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("Status", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "APPROVED") return;
        if (!ctx.Values.TryGetValue("RequestId", out var reqIdObj) || reqIdObj == null) return;
        if (!ctx.Values.TryGetValue("StepNo", out var stepNoObj) || stepNoObj == null) return;

        var requestId = Convert.ToInt32(reqIdObj);
        var stepNo = Convert.ToInt32(stepNoObj);

        var approvalRequest = await db.QuerySingleAsync<Dictionary<string, object?>>(
            "SELECT * FROM approval_requests WHERE id = @id", new { id = requestId }, tx);

        var totalSteps = DictHelper.Get<int>(approvalRequest, "TotalSteps", "total_steps", 1);
        var sourceTable = DictHelper.GetStr(approvalRequest, "SourceTable", "source_table") ?? string.Empty;
        var sourceId = DictHelper.Get<int>(approvalRequest, "SourceId", "source_id", 0);

        await db.ExecuteAsync("UPDATE approval_requests SET current_step = @stepNo WHERE id = @requestId", new { stepNo, requestId }, tx);
        await db.ExecuteAsync("UPDATE approval_steps SET status = 'APPROVED' WHERE request_id = @requestId AND step_no = @stepNo", new { requestId, stepNo }, tx);

        var pendingSteps = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM approval_steps WHERE request_id = @requestId AND status = 'PENDING'", new { requestId }, tx);

        if (pendingSteps == 0)
        {
            await db.ExecuteAsync("UPDATE approval_requests SET status = 'APPROVED' WHERE id = @requestId", new { requestId }, tx);

            if (sourceTable == "purchase_orders" && sourceId > 0)
            {
                await db.ExecuteAsync("UPDATE purchase_orders SET ApprovalStatus = 'APPROVED' WHERE id = @sourceId", new { sourceId }, tx);
            }
        }
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());
}

/// <summary>
/// 承認却下時の処理
/// </summary>
public class ApprovalRejectHook : IEntityHook
{
    public string Name => "approval_reject";

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var newStatus = ctx.Values.TryGetValue("Status", out var statusObj) ? statusObj?.ToString() : null;
        if (newStatus != "REJECTED") return;
        if (!ctx.Values.TryGetValue("RequestId", out var reqIdObj) || reqIdObj == null) return;

        var requestId = Convert.ToInt32(reqIdObj);
        var approvalRequest = await db.QuerySingleAsync<Dictionary<string, object?>>(
            "SELECT * FROM approval_requests WHERE id = @id", new { id = requestId }, tx);

        var sourceTable = DictHelper.GetStr(approvalRequest, "SourceTable", "source_table") ?? string.Empty;
        var sourceId = DictHelper.Get<int>(approvalRequest, "SourceId", "source_id", 0);

        await db.ExecuteAsync("UPDATE approval_requests SET status = 'REJECTED' WHERE id = @requestId", new { requestId }, tx);

        if (sourceTable == "purchase_orders" && sourceId > 0)
        {
            await db.ExecuteAsync("UPDATE purchase_orders SET ApprovalStatus = 'REJECTED' WHERE id = @sourceId", new { sourceId }, tx);
        }
    }

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());
}
