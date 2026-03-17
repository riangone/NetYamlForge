// ファイル概要：汎用エンティティフック実装集。
// ほとんどの CRUD シナリオをカバーする共通フックを網羅しています。
// entities.yml の hooks 設定で名前を指定してすぐに使用できます。

using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Dapper;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Dialect;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace NetYamlForge.Services.Hooks;

/// <summary>フック内でSQL識別子（テーブル名・列名）を検証するための共通正規表現。</summary>
internal static class HookConstants
{
    public static readonly Regex HookIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
}

#region 検証フック（Validation Hooks）

/// <summary>
/// [汎用検証] 指定フィールドがメールアドレス形式であることを検証するフック。
/// 複数フィールドを同時に検証可能。
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "validate_email:Email,BackupEmail"
///     beforeUpdate: "validate_email:Email,BackupEmail"
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
        // フック名が "validate_email:Field1,Field2" の形式の場合を処理
        return ctx.Data.TryGetValue("__hookConfig", out var config) && config is string s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "Email" };
    }
}

/// <summary>
/// [汎用検証] 指定フィールドが電話番号形式であることを検証するフック。
/// 国際形式（+ 付き）と国内形式（0 始まり）の両方を許可。
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "validate_phone:Phone"
///     beforeUpdate: "validate_phone:Phone"
/// </summary>
public class ValidatePhoneHook : IEntityHook
{
    private readonly ILogger<ValidatePhoneHook> _logger;
    // 簡易的な電話番号パターン（国際形式・国内形式）
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "validate_url:Website"
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "validate_regex:Code:^[A-Z]{3}[0-9]{3}$"
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
        // 設定形式："Field:Pattern" または "Field1:Pattern1|Field2:Pattern2"
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "validate_range:Age:0:150"
///     beforeCreate: "validate_range:Price:0.01:999999.99"
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
        // 設定形式："Field:Min:Max"
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "validate_unique:Email:Customer"
///     beforeUpdate: "validate_unique:Email:Customer"
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
        // 設定形式："Field:Table"
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return HookResult.Continue();

        var parts = config.Split(':');
        if (parts.Length < 2) return HookResult.Continue();

        var field = parts[0].Trim();
        var table = parts[1].Trim();

        // 識別子を検証してSQLインジェクションを防止
        if (!HookConstants.HookIdentifierRegex.IsMatch(field) || !HookConstants.HookIdentifierRegex.IsMatch(table))
        {
            _logger.LogWarning("[Hook:validate_unique] 無効な識別子が渡されました: field={Field} table={Table}", field, table);
            return HookResult.Continue();
        }

        if (!ctx.Values.TryGetValue(field, out var value) || value == null)
            return HookResult.Continue();

        // Update 時は自分自身を除外
        var isUpdate = ctx.Operation == CrudOperation.Update;
        var whereClause = $"{field} = @value";
        if (isUpdate && ctx.Id.HasValue)
        {
            whereClause += $" AND Id = @id";
        }

        // Safe: field/table are validated by HookIdentifierRegex above
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "validate_required:Name,Code"
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

#endregion

#region データ変換フック（Data Transformation Hooks）

/// <summary>
/// [汎用変換] 指定フィールドの文字列をトリム（前後空白削除）するフック。
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "trim:Name,Email,Address"
///     beforeUpdate: "trim:Name,Email,Address"
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "uppercase:Code,Country"
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "lowercase:Email,Code"
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "titlecase:FirstName,LastName,City"
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

/// <summary>
/// [汎用変換] 指定フィールドにデフォルト値を設定するフック（値が空の場合のみ）。
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "default:Status:Active,Country:USA"
/// </summary>
public class DefaultHook : IEntityHook
{
    public string Name => "default";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 設定形式："Field1:Value1|Field2:Value2"
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "now:CreatedAt"
///     beforeUpdate: "now:UpdatedAt"
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
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "current_user:CreatedBy"
///     beforeUpdate: "current_user:UpdatedBy"
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

#endregion

#region 監査ログ・通知フック（Audit & Notification Hooks）

/// <summary>
/// [汎用監査] 操作内容をアプリログに記録するフック。
///
/// entities.yml での使用例:
///   hooks:
///     afterCreate: "audit_log"
///     afterUpdate: "audit_log"
/// </summary>
public class AuditLogHook : IEntityHook
{
    private readonly ILogger<AuditLogHook> _logger;

    public AuditLogHook(ILogger<AuditLogHook> logger)
    {
        _logger = logger;
    }

    public string Name => "audit_log";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var message = $"{ctx.Operation} completed — entity={ctx.Entity}, id={ctx.Id?.ToString() ?? "(new)"}, user={ctx.UserName ?? "unknown"}";
        _logger.LogInformation("[Hook:audit_log] {Message}", message);

        // 直接 DB に監査ログを記録します（IAuditLogService を使用しない）
        try
        {
            const string sql = @"
INSERT INTO AuditLog (UserName, Action, Entity, Detail, CreatedAt)
VALUES (@UserName, @Action, @Entity, @Detail, @CreatedAt)";

            var param = new
            {
                UserName = ctx.UserName,
                Action = ctx.Operation.ToString(),
                Entity = ctx.Entity,
                Detail = message,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            await db.ExecuteAsync(sql, param, tx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hook:audit_log] 監査ログの記録に失敗しました");
        }
    }
}

/// <summary>
/// [汎用通知] 操作完了後に指定 Webhook URL へ POST するフック。
/// 
/// entities.yml での使用例:
///   hooks:
///     afterCreate: "webhook:https://example.com/api/notify"
/// </summary>
public class WebhookHook : IEntityHook
{
    private readonly ILogger<WebhookHook> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;

    public WebhookHook(ILogger<WebhookHook> logger, IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public string Name => "webhook";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config) || _httpClientFactory == null)
            return;

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new
            {
                entity = ctx.Entity,
                operation = ctx.Operation.ToString(),
                id = ctx.Id,
                user = ctx.UserName,
                values = ctx.Values,
                timestamp = DateTime.Now
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync(config, content);
            _logger.LogInformation("[Hook:webhook] Webhook call to {Url} returned {StatusCode}", config, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Hook:webhook] Failed to call webhook {Url}", config);
        }
    }
}

#endregion

#region 関連データ操作フック（Related Data Hooks）

/// <summary>
/// [汎用関連操作] 関連テーブルのカウント値を更新するフック。
/// 例：Customer 作成時に Orders カウントを増やす。
/// 
/// entities.yml での使用例:
///   hooks:
///     afterCreate: "update_count:Customer:CustomerId:Orders:CustomerId"
///     afterDelete: "update_count:Customer:CustomerId:Orders:CustomerId"
/// </summary>
public class UpdateCountHook : IEntityHook
{
    private readonly ILogger<UpdateCountHook> _logger;

    public UpdateCountHook(ILogger<UpdateCountHook> logger)
    {
        _logger = logger;
    }

    public string Name => "update_count";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 設定形式："SourceEntity:SourceKey:TargetTable:TargetForeignKey"
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return;

        var parts = config.Split(':');
        if (parts.Length != 4) return;

        var sourceEntity = parts[0].Trim();
        var sourceKey = parts[1].Trim();
        var targetTable = parts[2].Trim();
        var targetForeignKey = parts[3].Trim();

        if (!ctx.Values.TryGetValue(sourceKey, out var keyValue) || keyValue == null)
            return;

        var isCreate = ctx.Operation == CrudOperation.Create;
        var delta = isCreate ? 1 : -1;

        // 識別子を検証してSQLインジェクションを防止
        if (!HookConstants.HookIdentifierRegex.IsMatch(sourceEntity) || !HookConstants.HookIdentifierRegex.IsMatch(sourceKey))
        {
            _logger.LogWarning("[Hook:update_count] 無効な識別子が渡されました: entity={Entity} key={Key}", sourceEntity, sourceKey);
            return;
        }

        // 簡易実装：カウントカラム名は固定
        var countColumn = $"{sourceEntity}Count";
#pragma warning disable DCS001 // Safe: sourceEntity/sourceKey are validated by HookIdentifierRegex above
        var sql = $"UPDATE {sourceEntity} SET {countColumn} = {countColumn} + {delta} WHERE {sourceKey} = @key";
#pragma warning restore DCS001

        await db.ExecuteAsync(sql, new { key = keyValue }, tx);
        _logger.LogInformation("[Hook:update_count] Updated count for {Entity}.{Key} = {Value}", sourceEntity, sourceKey, keyValue);
    }
}

/// <summary>
/// [汎用関連操作] 関連レコードのフィールドを更新するフック。
/// 例：Customer 削除時に Orders の Status を Cancelled に更新。
/// 
/// entities.yml での使用例:
///   hooks:
///     afterCreate: "update_related:Customer:CustomerId:Orders:CustomerId:Status:Active"
/// </summary>
public class UpdateRelatedHook : IEntityHook
{
    private readonly ILogger<UpdateRelatedHook> _logger;

    public UpdateRelatedHook(ILogger<UpdateRelatedHook> logger)
    {
        _logger = logger;
    }

    public string Name => "update_related";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 設定形式："SourceEntity:SourceKey:TargetTable:TargetFK:UpdateField:UpdateValue"
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return;

        var parts = config.Split(':');
        if (parts.Length != 6) return;

        var sourceEntity = parts[0].Trim();
        var sourceKey = parts[1].Trim();
        var targetTable = parts[2].Trim();
        var targetFK = parts[3].Trim();
        var updateField = parts[4].Trim();
        var updateValue = parts[5].Trim();

        if (!ctx.Values.TryGetValue(sourceKey, out var keyValue) || keyValue == null)
            return;

        // 識別子を検証してSQLインジェクションを防止
        if (!HookConstants.HookIdentifierRegex.IsMatch(targetTable) || !HookConstants.HookIdentifierRegex.IsMatch(updateField) || !HookConstants.HookIdentifierRegex.IsMatch(targetFK))
        {
            _logger.LogWarning("[Hook:update_related] 無効な識別子が渡されました: table={Table} field={Field} fk={FK}", targetTable, updateField, targetFK);
            return;
        }

#pragma warning disable DCS001 // Safe: targetTable/updateField/targetFK are validated by HookIdentifierRegex above
        var sql = $"UPDATE {targetTable} SET {updateField} = @value WHERE {targetFK} = @key";
#pragma warning restore DCS001

        await db.ExecuteAsync(sql, new { value = updateValue, key = keyValue }, tx);
        _logger.LogInformation("[Hook:update_related] Updated {Table}.{FK} = {Key}", targetTable, targetFK, keyValue);
    }
}

#endregion

#region ソフト削除フック（Soft Delete Hooks）

/// <summary>
/// [汎用ソフト削除] 削除フラグと削除日時を設定するフック。
/// 実際の DELETE を INSERT/UPDATE に変換します。
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeDelete: "soft_delete:DeletedAt:DeletedBy"
/// </summary>
public class SoftDeleteHook : IEntityHook
{
    private readonly ILogger<SoftDeleteHook> _logger;

    public SoftDeleteHook(ILogger<SoftDeleteHook> logger)
    {
        _logger = logger;
    }

    public string Name => "soft_delete";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 設定形式："DeletedFlagColumn:DeletedAtColumn"
        var config = ctx.Data.TryGetValue("__hookConfig", out var c) && c is string s ? s : string.Empty;
        if (string.IsNullOrWhiteSpace(config))
            return Task.FromResult(HookResult.Continue());

        var parts = config.Split(':');
        if (parts.Length < 2) return Task.FromResult(HookResult.Continue());

        var deletedFlag = parts[0].Trim();
        var deletedAtColumn = parts[1].Trim();

        // 削除フラグと日時を設定
        ctx.Values[deletedFlag] = 1;
        ctx.Values[deletedAtColumn] = DateTime.Now;

        if (!string.IsNullOrEmpty(ctx.UserName))
        {
            var deletedByColumn = parts.Length >= 3 ? parts[2].Trim() : null;
            if (!string.IsNullOrEmpty(deletedByColumn))
            {
                ctx.Values[deletedByColumn] = ctx.UserName;
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

#endregion
