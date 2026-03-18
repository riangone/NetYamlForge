// ファイル概要: フック名解決（プロジェクト固有→フレームワークの2段階）と実行を担うサービスです。
// DynamicEntityCommandService（EntityCrudExecutionService 経由）と PageRowMutationService の両方が
// 持っていた重複ロジックを一元化します。テレメトリ記録も本サービスで行います。
// このファイルを変更する場面: フック解決優先順位の変更・テレメトリ追加のみ。

using System.Data;
using System.Diagnostics;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

/// <summary>
/// フック名解決（プロジェクト固有レジストリ → フレームワークレジストリ）と
/// BeforeAsync / AfterAsync 実行を担う共有サービス。
/// </summary>
public sealed class HookExecutionService
{
    private readonly IEntityHookRegistry _hookRegistry;
    private readonly IProjectHookRegistry _projectHookRegistry;
    private readonly IHookExecutionTelemetry _hookTelemetry;
    private readonly ILogger<HookExecutionService> _logger;

    public HookExecutionService(
        IEntityHookRegistry hookRegistry,
        IProjectHookRegistry projectHookRegistry,
        IHookExecutionTelemetry hookTelemetry,
        ILogger<HookExecutionService> logger)
    {
        _hookRegistry = hookRegistry;
        _projectHookRegistry = projectHookRegistry;
        _hookTelemetry = hookTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Before フックリストを実行します。
    /// Cancel が返った場合は即座に中断し HookResult.Abort を返します。
    /// </summary>
    public async Task<HookResult> RunBeforeAsync(
        List<string>? hookNames,
        EntityHookContext ctx,
        string? projectName,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (hookNames == null || hookNames.Count == 0)
            return HookResult.Continue();

        foreach (var name in hookNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            var (hook, source) = ResolveWithSource(projectName, name, ctx);
            if (hook == null)
            {
                _logger.LogWarning("フック '{Name}' が登録されていません — スキップします", name);
                _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                    Phase: "before", Source: "missing", HookName: name,
                    Entity: ctx.Entity, Operation: ctx.Operation,
                    Result: "skipped_not_found", DurationMs: 0));
                continue;
            }

            var sw = Stopwatch.StartNew();
            HookResult result;
            try
            {
                result = await hook.BeforeAsync(ctx, db, tx);
            }
            catch (Exception ex)
            {
                _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                    Phase: "before", Source: source, HookName: name,
                    Entity: ctx.Entity, Operation: ctx.Operation,
                    Result: "error", DurationMs: sw.ElapsedMilliseconds, Exception: ex));
                throw;
            }

            _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                Phase: "before", Source: source, HookName: name,
                Entity: ctx.Entity, Operation: ctx.Operation,
                Result: result.Cancel ? "cancel" : "continue",
                DurationMs: sw.ElapsedMilliseconds,
                CancelMessage: result.CancelMessage));

            if (result.Cancel) return result;
        }

        return HookResult.Continue();
    }

    /// <summary>
    /// After フックリストを実行します。
    /// </summary>
    public async Task RunAfterAsync(
        List<string>? hookNames,
        EntityHookContext ctx,
        string? projectName,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (hookNames == null || hookNames.Count == 0) return;

        foreach (var name in hookNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            var (hook, source) = ResolveWithSource(projectName, name, ctx);
            if (hook == null)
            {
                _logger.LogWarning("フック '{Name}' が登録されていません — スキップします", name);
                _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                    Phase: "after", Source: "missing", HookName: name,
                    Entity: ctx.Entity, Operation: ctx.Operation,
                    Result: "skipped_not_found", DurationMs: 0));
                continue;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                await hook.AfterAsync(ctx, db, tx);
            }
            catch (Exception ex)
            {
                _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                    Phase: "after", Source: source, HookName: name,
                    Entity: ctx.Entity, Operation: ctx.Operation,
                    Result: "error", DurationMs: sw.ElapsedMilliseconds, Exception: ex));
                throw;
            }

            _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                Phase: "after", Source: source, HookName: name,
                Entity: ctx.Entity, Operation: ctx.Operation,
                Result: "continue", DurationMs: sw.ElapsedMilliseconds));
        }
    }

    /// <summary>
    /// フック名からフックを解決し、出処（"project" / "framework" / null）も返す。
    /// </summary>
    private (IEntityHook? hook, string source) ResolveWithSource(
        string? projectName, string hookName, EntityHookContext ctx)
    {
        if (!string.IsNullOrEmpty(projectName))
        {
            var ph = _projectHookRegistry.Find(projectName, hookName, ctx);
            if (ph != null) return (ph, "project");
        }

        var fh = _hookRegistry.Find(hookName, ctx);
        return fh != null ? (fh, "framework") : (null, "missing");
    }
}
