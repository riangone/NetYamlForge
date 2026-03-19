// 交互记录钩子 - 完成时自动设置时间
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ContactManager.Hooks;

/// <summary>
/// 交互记录完成时间自动设置钩子
/// </summary>
public sealed class SetCompletedTimeHook : IEntityHook
{
    public string Name => "set_completed_time";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Update)
            return Task.FromResult(HookResult.Continue());

        // 如果状态改为 completed 且没有完成时间，自动设置
        if (ctx.Values.TryGetValue("status", out var status) &&
            status?.ToString() == "completed" &&
            (!ctx.Values.TryGetValue("completedAt", out var completedAt) ||
             string.IsNullOrWhiteSpace(completedAt?.ToString())))
        {
            ctx.Values["completedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
