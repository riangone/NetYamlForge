using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

/// <summary>
/// lead_nurturing_tasks 作成時に created_at / updated_at / due_date を自動設定するフック。
/// </summary>
public class SetNurturingTaskTimestampsHook : IEntityHook
{
    public string Name => "set_nurturing_task_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            ctx.Values["created_at"] = now;
        if (!ctx.Values.ContainsKey("updated_at") || ctx.Values["updated_at"] == null)
            ctx.Values["updated_at"] = now;

        if (!ctx.Values.ContainsKey("due_date") || ctx.Values["due_date"] == null)
        {
            if (ctx.Values.TryGetValue("task_type", out var taskType))
            {
                var days = taskType?.ToString() switch
                {
                    "test_drive_invite" => 1,
                    "followup_call" => 1,
                    "price_alert" => 2,
                    "special_offer" => 3,
                    _ => 3
                };
                ctx.Values["due_date"] = DateTime.Now.AddDays(days).ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                ctx.Values["due_date"] = DateTime.Now.AddDays(3).ToString("yyyy-MM-dd HH:mm:ss");
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// lead_nurturing_tasks 更新時に updated_at を自動更新し、完了時に completed_at を設定するフック。
/// </summary>
public class UpdateNurturingTaskTimestampsHook : IEntityHook
{
    public string Name => "update_nurturing_task_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Values["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (ctx.Values.TryGetValue("status", out var status) && status?.ToString() == "completed")
        {
            ctx.Values["completed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// lead_nurturing_tasks の優先度スコアを計算するフック。
/// </summary>
public class CalculateNurturingPriorityHook : IEntityHook
{
    public string Name => "calculate_nurturing_priority";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var basePriority = ctx.Values.TryGetValue("task_type", out var taskType)
            ? GetBasePriority(taskType?.ToString())
            : 50;

        var leadScore = 0;
        if (ctx.Values.TryGetValue("lead_score", out var scoreVal) && scoreVal != null)
        {
            if (int.TryParse(scoreVal.ToString(), out var s))
            {
                leadScore = s;
            }
        }

        var finalPriority = Math.Min(100, basePriority + (leadScore / 10));
        ctx.Values["priority_score"] = finalPriority;

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    private static int GetBasePriority(string? taskType) => taskType switch
    {
        "test_drive_invite" => 70,
        "competitor_counter" => 75,
        "followup_call" => 60,
        "price_alert" => 65,
        "special_offer" => 55,
        "send_info" => 50,
        _ => 50
    };
}

public sealed class CompleteNurturingTaskHandler : ICustomActionHandler
{
    public string Name => "complete_nurturing_task";
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("No ID");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync("UPDATE lead_nurturing_tasks SET status = 'completed', completed_at = @now WHERE task_id = @id", new { now, id = ctx.RecordId }, tx);
        return ActionHandlerResult.Success();
    }
}

public sealed class CancelNurturingTaskHandler : ICustomActionHandler
{
    public string Name => "cancel_nurturing_task";
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("No ID");
        await db.ExecuteAsync("UPDATE lead_nurturing_tasks SET status = 'cancelled' WHERE task_id = @id", new { id = ctx.RecordId }, tx);
        return ActionHandlerResult.Success();
    }
}
