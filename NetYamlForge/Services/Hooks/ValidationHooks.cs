using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// [汎用検証] 指定フィールドがメールアドレス形式であることを検証するフック。
/// </summary>
public class ValidateEmailHook : IEntityHook
{
    private readonly ILogger<ValidateEmailHook> _logger;

    public ValidateEmailHook(ILogger<ValidateEmailHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_email";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            if (!ctx.Values.TryGetValue(field, out var value) || value is not string email || string.IsNullOrWhiteSpace(email))
                continue;

            try
            {
                _ = new MailAddress(email);
            }
            catch
            {
                _logger.LogWarning("[Hook:validate_email] Invalid email '{Email}' in field '{Field}'", email, field);
                return Task.FromResult(HookResult.Abort($"フィールド '{field}' のメールアドレス形式が正しくありません。"));
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
            : new[] { "Email" };
    }
}

/// <summary>
/// [汎用検証] 指定フィールドが電話番号形式であることを検証するフック。
/// </summary>
public class ValidatePhoneHook : IEntityHook
{
    private readonly ILogger<ValidatePhoneHook> _logger;
    private static readonly Regex _phoneRegex = new(@"^\+?[0-9\s\-\(\)]{7,20}$", RegexOptions.Compiled);

    public ValidatePhoneHook(ILogger<ValidatePhoneHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_phone";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            if (!ctx.Values.TryGetValue(field, out var value) || value is not string phone || string.IsNullOrWhiteSpace(phone))
                continue;

            var cleaned = phone.Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "");
            if (!_phoneRegex.IsMatch(cleaned))
            {
                _logger.LogWarning("[Hook:validate_phone] Invalid phone '{Phone}' in field '{Field}'", phone, field);
                return Task.FromResult(HookResult.Abort($"フィールド '{field}' の電話番号形式が正しくありません。"));
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
            : new[] { "Phone" };
    }
}

/// <summary>
/// [汎用検証] 指定フィールドが URL 形式であることを検証するフック。
/// </summary>
public class ValidateUrlHook : IEntityHook
{
    private readonly ILogger<ValidateUrlHook> _logger;

    public ValidateUrlHook(ILogger<ValidateUrlHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_url";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            if (!ctx.Values.TryGetValue(field, out var value) || value is not string url || string.IsNullOrWhiteSpace(url))
                continue;

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                _logger.LogWarning("[Hook:validate_url] Invalid URL '{Url}' in field '{Field}'", url, field);
                return Task.FromResult(HookResult.Abort($"フィールド '{field}' の URL 形式が正しくありません。"));
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
            : new[] { "Url" };
    }
}

/// <summary>
/// [汎用検証] 指定フィールドが正規表現パターンにマッチすることを検証するフック。
/// </summary>
public class ValidateRegexHook : IEntityHook
{
    private readonly ILogger<ValidateRegexHook> _logger;

    public ValidateRegexHook(ILogger<ValidateRegexHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_regex";

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
            var pattern = parts[1].Trim();

            if (!ctx.Values.TryGetValue(field, out var value) || value is not string text || string.IsNullOrWhiteSpace(text))
                continue;

            if (!Regex.IsMatch(text, pattern))
            {
                _logger.LogWarning("[Hook:validate_regex] Field '{Field}' value '{Value}' does not match pattern '{Pattern}'", field, value, pattern);
                return Task.FromResult(HookResult.Abort($"フィールド '{field}' の値が指定パターンに一致しません。"));
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [汎用検証] 指定フィールドの値が最小値以上・最大値以下であることを検証するフック。
/// </summary>
public class ValidateRangeHook : IEntityHook
{
    private readonly ILogger<ValidateRangeHook> _logger;

    public ValidateRangeHook(ILogger<ValidateRangeHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_range";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return Task.FromResult(HookResult.Continue());

        var parts = config.Split(':');
        if (parts.Length != 3) return Task.FromResult(HookResult.Continue());

        var field = parts[0].Trim();
        if (!decimal.TryParse(parts[1], out var min) || !decimal.TryParse(parts[2], out var max))
            return Task.FromResult(HookResult.Continue());

        if (!ctx.Values.TryGetValue(field, out var value))
            return Task.FromResult(HookResult.Continue());

        var amount = value switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            int i => i,
            long l => l,
            _ when decimal.TryParse(value?.ToString(), out var parsed) => parsed,
            _ => min
        };

        if (amount < min || amount > max)
        {
            _logger.LogWarning("[Hook:validate_range] Field '{Field}' value {Value} out of range [{Min}, {Max}]", field, amount, min, max);
            return Task.FromResult(HookResult.Abort($"フィールド '{field}' の値は {min} 以上 {max} 以下である必要があります。"));
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [汎用検証] 指定フィールドの値が一意であることを検証するフック（DB 照会）。
/// </summary>
public class ValidateUniqueHook : IEntityHook
{
    private readonly ILogger<ValidateUniqueHook> _logger;

    public ValidateUniqueHook(ILogger<ValidateUniqueHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_unique";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return HookResult.Continue();

        var parts = config.Split(':');
        if (parts.Length < 2) return HookResult.Continue();

        var field = parts[0].Trim();
        var table = parts[1].Trim();

        if (!HookConstants.HookIdentifierRegex.IsMatch(field) || !HookConstants.HookIdentifierRegex.IsMatch(table))
        {
            _logger.LogWarning("[Hook:validate_unique] 無効な識別子が渡されました: field={Field} table={Table}", field, table);
            return HookResult.Continue();
        }

        if (!ctx.Values.TryGetValue(field, out var value) || value == null)
            return HookResult.Continue();

        var isUpdate = ctx.Operation == CrudOperation.Update;
        var whereClause = $"{field} = @value";
        if (isUpdate && ctx.Id.HasValue)
        {
            whereClause += $" AND Id = @id";
        }

#pragma warning disable DCS001
        var sql = $"SELECT COUNT(*) FROM {table} WHERE {whereClause}";
#pragma warning restore DCS001

        var param = new { value, id = ctx.Id };
        var count = isUpdate && ctx.Id.HasValue
            ? await db.ExecuteScalarAsync<int>(sql, param, tx)
            : await db.ExecuteScalarAsync<int>(sql, new { value }, tx);

        if (count > 0)
        {
            _logger.LogWarning("[Hook:validate_unique] Duplicate value '{Value}' for field '{Field}' in table '{Table}'", value, field, table);
            return HookResult.Abort($"フィールド '{field}' の値 '{value}' は既に存在します。");
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [汎用検証] 指定フィールドが必須であることを検証するフック（空白不可）。
/// </summary>
public class ValidateRequiredHook : IEntityHook
{
    private readonly ILogger<ValidateRequiredHook> _logger;

    public ValidateRequiredHook(ILogger<ValidateRequiredHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_required";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var fields = GetTargetFields(ctx);
        foreach (var field in fields)
        {
            if (!ctx.Values.TryGetValue(field, out var value) ||
                (value is string s && string.IsNullOrWhiteSpace(s)) ||
                value == null)
            {
                _logger.LogWarning("[Hook:validate_required] Required field '{Field}' is empty", field);
                return Task.FromResult(HookResult.Abort($"フィールド '{field}' は必須です。"));
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
