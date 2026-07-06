using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

/// <summary>
/// sales_leads 作成時に created_at / updated_at / status / lead_score を自動補完するフック。
/// </summary>
public class SetLeadTimestampsHook : IEntityHook
{
    public string Name => "set_lead_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            ctx.Values["created_at"] = now;
        if (!ctx.Values.ContainsKey("updated_at") || ctx.Values["updated_at"] == null)
            ctx.Values["updated_at"] = now;
        if (!ctx.Values.ContainsKey("status") || ctx.Values["status"] == null)
            ctx.Values["status"] = "new";
        if (!ctx.Values.ContainsKey("lead_score") || ctx.Values["lead_score"] == null)
            ctx.Values["lead_score"] = 50;

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// sales_leads 更新時に updated_at を自動更新するフック。
/// </summary>
public class UpdateLeadTimestampHook : IEntityHook
{
    public string Name => "update_lead_updated_at";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Values["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// lead_activities 作成時に created_at・created_by を自動補完するフック。
/// </summary>
public class SetLeadActivityTimestampsHook : IEntityHook
{
    public string Name => "set_lead_activity_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            ctx.Values["created_at"] = now;
        if (string.IsNullOrEmpty(ctx.Values.GetValueOrDefault("created_by")?.ToString()) && ctx.UserName != null)
            ctx.Values["created_by"] = ctx.UserName;
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// sales_leads のリードスコアを 0-100 の範囲にクランプするフック。
/// </summary>
public class CalculateLeadScoreHook : IEntityHook
{
    public string Name => "calculate_lead_score";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("lead_score", out var scoreVal) && scoreVal != null)
        {
            if (int.TryParse(scoreVal.ToString(), out var s))
                ctx.Values["lead_score"] = Math.Clamp(s, 0, 100);
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
