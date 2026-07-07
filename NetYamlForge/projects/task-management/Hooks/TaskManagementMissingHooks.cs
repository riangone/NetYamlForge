using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TaskManagement.Hooks;

/// <summary>
/// 指定フィールドに現在日時を設定するフック。
/// </summary>
public class NowHook : IEntityHook
{
    public string Name => "now";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            ctx.Values[field] = DateTime.Now;
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    private static string[] GetTargetFields(EntityHookContext ctx)
    {
        return ctx.Data.TryGetValue("__hookConfig", out var config) && config is string s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "CreatedAt" };
    }
}

/// <summary>
/// 指定フィールドに現在ユーザー名を設定するフック。
/// </summary>
public class CurrentUserHook : IEntityHook
{
    public string Name => "current_user";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            ctx.Values[field] = ctx.UserName ?? "system";
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    private static string[] GetTargetFields(EntityHookContext ctx)
    {
        return ctx.Data.TryGetValue("__hookConfig", out var config) && config is string s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "CreatedBy" };
    }
}
