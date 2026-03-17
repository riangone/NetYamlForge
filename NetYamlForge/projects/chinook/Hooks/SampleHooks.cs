// プロジェクト固有フックのサンプル
// このディレクトリにプロジェクト専用のフッククラスを配置します。
// フレームワーク共通のフックと分離され、このプロジェクト内でのみ使用されます。

using System.Data;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace NetYamlForge.Projects.Chinook.Hooks;

/// <summary>
/// [サンプル] Chinook プロジェクト固有のフック。
/// 例：Track の再生時間を秒に変換するフック。
/// 
/// entities.yml での使用例:
///   hooks:
///     beforeCreate: "track_convert_duration"
/// </summary>
public class TrackConvertDurationHook : IEntityHook
{
    private readonly ILogger<TrackConvertDurationHook> _logger;

    public TrackConvertDurationHook(ILogger<TrackConvertDurationHook> logger)
    {
        _logger = logger;
    }

    public string Name => "track_convert_duration";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 毫秒を秒に変換するサンプル
        if (ctx.Values.TryGetValue("Milliseconds", out var msObj) && msObj is int ms)
        {
            ctx.Values["Seconds"] = ms / 1000;
            _logger.LogDebug("Track: {Milliseconds}ms → {Seconds}秒", ms, ms / 1000);
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// [サンプル] Invoice 作成時に自動で顧客にメールを送信するフック。
/// 
/// entities.yml での使用例:
///   hooks:
///     afterCreate: "invoice_send_welcome_email"
/// </summary>
public class InvoiceSendWelcomeEmailHook : IEntityHook
{
    private readonly ILogger<InvoiceSendWelcomeEmailHook> _logger;

    public InvoiceSendWelcomeEmailHook(ILogger<InvoiceSendWelcomeEmailHook> logger)
    {
        _logger = logger;
    }

    public string Name => "invoice_send_welcome_email";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 実際のメール送信ロジック
        _logger.LogInformation("Invoice 作成完了：顧客にメールを送信します");
        
        // 例：メール送信サービス呼び出し
        // await _emailService.SendAsync(...);
        
        await Task.CompletedTask;
    }
}
