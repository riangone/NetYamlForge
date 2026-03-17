// ファイル概要: フック実行のテレメトリ（計測・記録）インターフェースと実装を定義します。
// EntityCrudExecutionService が各フック実行後に HookExecutionTelemetryEvent を記録し、
// ログ分析やパフォーマンス監視に使用します。
// テスト時はモックで差し替え可能です（IHookExecutionTelemetry 経由）。

using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

/// <summary>フック実行テレメトリの記録インターフェース。</summary>
public interface IHookExecutionTelemetry
{
    /// <summary>フック実行イベントを記録します。</summary>
    void Record(HookExecutionTelemetryEvent evt);
}

/// <summary>
/// フック1回の実行情報を保持するイミュータブルレコード。
/// EntityCrudExecutionService が BeforeAsync / AfterAsync の実行直後に生成して記録します。
/// </summary>
/// <param name="Phase">"before" または "after"</param>
/// <param name="Source">
/// フックの出処:
/// "framework"（CommonHooks 等）、
/// "project"（プロジェクト固有フック）、
/// "missing"（フック名が未登録）
/// </param>
/// <param name="HookName">フック名（例: "validate_email", "trim"）</param>
/// <param name="Entity">対象エンティティ名（YAML キー）</param>
/// <param name="Operation">CRUD 操作種別（Create / Update / Delete）</param>
/// <param name="Result">
/// 実行結果:
/// "continue"（正常）、"cancel"（HookResult.Abort）、
/// "error"（例外）、"skipped_not_found"（未登録スキップ）
/// </param>
/// <param name="DurationMs">フック実行時間（ミリ秒）</param>
/// <param name="CancelMessage">キャンセル時のメッセージ（Result="cancel" の場合）</param>
/// <param name="Exception">例外オブジェクト（Result="error" の場合）</param>
public sealed record HookExecutionTelemetryEvent(
    string Phase,
    string Source,
    string HookName,
    string Entity,
    CrudOperation Operation,
    string Result,
    long DurationMs,
    string? CancelMessage = null,
    Exception? Exception = null);

/// <summary>
/// IHookExecutionTelemetry の実装。構造化ログとして記録します。
/// 例外ありの場合は LogWarning、正常の場合は LogInformation を使用します。
/// ログキー: hook_exec（ログ分析ツールでのフィルタリングに利用可能）
/// </summary>
public sealed class HookExecutionTelemetryLogger : IHookExecutionTelemetry
{
    private readonly ILogger<HookExecutionTelemetryLogger> _logger;

    public HookExecutionTelemetryLogger(ILogger<HookExecutionTelemetryLogger> logger)
    {
        _logger = logger;
    }

    public void Record(HookExecutionTelemetryEvent evt)
    {
        if (evt.Exception != null)
        {
            // 例外発生（フック内部エラー）は警告レベルで記録
            _logger.LogWarning(
                evt.Exception,
                "hook_exec phase={Phase} source={Source} hook={Hook} entity={Entity} op={Operation} result={Result} durationMs={DurationMs}",
                evt.Phase,
                evt.Source,
                evt.HookName,
                evt.Entity,
                evt.Operation,
                evt.Result,
                evt.DurationMs);
            return;
        }

        _logger.LogInformation(
            "hook_exec phase={Phase} source={Source} hook={Hook} entity={Entity} op={Operation} result={Result} durationMs={DurationMs}",
            evt.Phase,
            evt.Source,
            evt.HookName,
            evt.Entity,
            evt.Operation,
            evt.Result,
            evt.DurationMs);
    }
}
