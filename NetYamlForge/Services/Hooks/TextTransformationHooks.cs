using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// [汎用変換] 指定フィールドの文字列をトリム（前後空白削除）するフック。
/// </summary>
public class TrimHook : IEntityHook
{
    public string Name => "trim";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            if (ctx.Values.TryGetValue(field, out var value) && value is string s)
            {
                ctx.Values[field] = s.Trim();
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    private static string[] GetTargetFields(EntityHookContext ctx)
    {
        return ctx.Data.TryGetValue("__hookConfig", out var config) && config is string s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "Name" };
    }
}

/// <summary>
/// [汎用変換] 指定フィールドの文字列を大文字化するフック。
/// </summary>
public class UppercaseHook : IEntityHook
{
    public string Name => "uppercase";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            if (ctx.Values.TryGetValue(field, out var value) && value is string s)
            {
                ctx.Values[field] = s.ToUpperInvariant();
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    private static string[] GetTargetFields(EntityHookContext ctx)
    {
        return ctx.Data.TryGetValue("__hookConfig", out var config) && config is string s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
    }
}

/// <summary>
/// [汎用変換] 指定フィールドの文字列を小文字化するフック。
/// </summary>
public class LowercaseHook : IEntityHook
{
    public string Name => "lowercase";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            if (ctx.Values.TryGetValue(field, out var value) && value is string s)
            {
                ctx.Values[field] = s.ToLowerInvariant();
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    private static string[] GetTargetFields(EntityHookContext ctx)
    {
        return ctx.Data.TryGetValue("__hookConfig", out var config) && config is string s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
    }
}

/// <summary>
/// [汎用変換] 指定フィールドの文字列の先頭を大文字にするフック（Title Case）。
/// </summary>
public class TitleCaseHook : IEntityHook
{
    private readonly CultureInfo _culture;

    public TitleCaseHook()
    {
        _culture = CultureInfo.CurrentCulture;
    }

    public string Name => "titlecase";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            if (ctx.Values.TryGetValue(field, out var value) && value is string s && s.Length > 0)
            {
                ctx.Values[field] = _culture.TextInfo.ToTitleCase(s.ToLower());
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    private static string[] GetTargetFields(EntityHookContext ctx)
    {
        return ctx.Data.TryGetValue("__hookConfig", out var config) && config is string s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
    }
}
