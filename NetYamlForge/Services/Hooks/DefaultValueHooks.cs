using System;
using System.Data;
using System.Threading.Tasks;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// [汎用変換] 指定フィールドにデフォルト値を設定するフック（値が空の場合のみ）。
/// </summary>
public class DefaultHook : IEntityHook
{
    public string Name => "default";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return Task.FromResult(HookResult.Continue());

        var rules = config.Split('|');
        foreach (var rule in rules)
        {
            var parts = rule.Split(':', 2);
            if (parts.Length != 2) continue;

            var field = parts[0].Trim();
            var defaultValue = parts[1].Trim();

            if (!ctx.Values.TryGetValue(field, out var value) ||
                value == null ||
                (value is string str && string.IsNullOrWhiteSpace(str)))
            {
                ctx.Values[field] = defaultValue;
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [汎用変換] 指定フィールドに現在日時を設定するフック。
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
/// [汎用変換] 指定フィールドに現在ユーザー名を設定するフック。
/// </summary>
public class CurrentUserHook : IEntityHook
{
    public string Name => "current_user";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            ctx.Values[field] = ctx.UserName ?? "unknown";
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
