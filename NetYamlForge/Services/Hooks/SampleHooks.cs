// ファイル概要: entities.yml の hooks 設定から参照できるサンプルフック実装集です。
// 新しいフックを追加する際はこのファイルを参考にして IEntityHook を実装し、
// Program.cs に builder.Services.AddSingleton<IEntityHook, YourHook>() を追加してください。

using System.Data;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// [サンプル前処理] Customer の Email ドメインを検証するフック。
/// "blocked.example.com" ドメインのメールは登録・更新を拒否します。
///
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "customer_email_domain"
///     beforeUpdate: "customer_email_domain"
/// </summary>
public class CustomerEmailDomainHook : IEntityHook
{
    private readonly ILogger<CustomerEmailDomainHook> _logger;

    // 拒否するドメインリスト（実運用では設定ファイルや DB から読む）
    private static readonly HashSet<string> BlockedDomains =
        new(StringComparer.OrdinalIgnoreCase) { "blocked.example.com", "spam.test" };

    public CustomerEmailDomainHook(ILogger<CustomerEmailDomainHook> logger)
    {
        _logger = logger;
    }

    public string Name => "customer_email_domain";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("Email", out var emailObj) || emailObj is not string email)
        {
            return Task.FromResult(HookResult.Continue());
        }

        var atIndex = email.LastIndexOf('@');
        if (atIndex < 0)
        {
            return Task.FromResult(HookResult.Abort("メールアドレスの形式が正しくありません。"));
        }

        var domain = email[(atIndex + 1)..];
        if (BlockedDomains.Contains(domain))
        {
            _logger.LogWarning("[Hook] Blocked email domain '{Domain}' by user '{User}'", domain, ctx.UserName);
            return Task.FromResult(HookResult.Abort($"メールドメイン '{domain}' は登録できません。"));
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [サンプル前処理] Invoice の Total 最小金額（$0.01）を検証するフック。
/// 0 以下の金額は登録・更新を拒否します。
///
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "invoice_minimum_total"
///     beforeUpdate: "invoice_minimum_total"
/// </summary>
public class InvoiceMinimumTotalHook : IEntityHook
{
    private const decimal MinimumTotal = 0.01m;

    public string Name => "invoice_minimum_total";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("Total", out var totalObj))
        {
            return Task.FromResult(HookResult.Continue());
        }

        decimal amount = totalObj switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            _ when decimal.TryParse(totalObj?.ToString(), out var parsed) => parsed,
            _ => MinimumTotal  // 変換不能なら検証スキップ
        };

        if (amount < MinimumTotal)
        {
            return Task.FromResult(
                HookResult.Abort($"Invoice の合計金額は ${MinimumTotal:F2} 以上である必要があります。（入力値: ${amount:F2}）"));
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [サンプル後処理] DB 書き込み完了後にアプリログへ記録する汎用フック。
/// どのエンティティでも使用できます。
///
/// entities.yml での使用例:
///   hooks:
///     afterCreate: "console_log_after"
///     afterUpdate: "console_log_after"
/// </summary>
public class ConsoleLogAfterHook : IEntityHook
{
    private readonly ILogger<ConsoleLogAfterHook> _logger;

    public ConsoleLogAfterHook(ILogger<ConsoleLogAfterHook> logger)
    {
        _logger = logger;
    }

    public string Name => "console_log_after";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        _logger.LogInformation(
            "[Hook:after] {Operation} completed — entity={Entity}, id={Id}, user={User}, fields={Fields}",
            ctx.Operation,
            ctx.Entity,
            ctx.Id?.ToString() ?? "(new)",
            ctx.UserName ?? "unknown",
            string.Join(", ", ctx.Values.Keys));

        return Task.CompletedTask;
    }
}

/// <summary>
/// [サンプル前後処理組み合わせ] Customer 作成時に FirstName の先頭を大文字に正規化する前処理フック。
/// HookResult.Abort() は返さずに値を書き換えるパターンのサンプルです。
///
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "customer_name_normalize"
///     beforeUpdate: "customer_name_normalize"
/// </summary>
public class CustomerNameNormalizeHook : IEntityHook
{
    public string Name => "customer_name_normalize";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        foreach (var field in new[] { "FirstName", "LastName" })
        {
            if (ctx.Values.TryGetValue(field, out var v) && v is string s && s.Length > 0)
            {
                ctx.Values[field] = char.ToUpperInvariant(s[0]) + s[1..];
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
