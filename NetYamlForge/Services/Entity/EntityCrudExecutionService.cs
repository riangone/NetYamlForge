// ファイル概要: エンティティCRUD操作のフック実行とトランザクション管理を担当するサービス。
// 責務: BeforeHook → DB書き込み → AfterHook をトランザクション内で一貫実行する。
// ビジネスロジックは持たない。SQL生成は DynamicCrudRepository、フック実装は各Hookクラスに委譲。
// このファイルを変更する場面: フックの優先順位変更・トランザクション境界の調整のみ。

using System.Data;
using System.Diagnostics;
using System.Text.Json;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

public sealed class EntityCrudExecutionService
{
    private readonly IDbConnection _db;
    private readonly IAuditLogService _audit;
    private readonly IEntityHookRegistry _hookRegistry;
    private readonly IProjectHookRegistry _projectHookRegistry;
    private readonly IHookExecutionTelemetry _hookTelemetry;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<EntityCrudExecutionService> _logger;

    public EntityCrudExecutionService(
        IDbConnection db,
        IAuditLogService audit,
        IEntityHookRegistry hookRegistry,
        IProjectHookRegistry projectHookRegistry,
        IHookExecutionTelemetry hookTelemetry,
        ProjectScope projectScope,
        ILogger<EntityCrudExecutionService> logger)
    {
        _db = db;
        _audit = audit;
        _hookRegistry = hookRegistry;
        _projectHookRegistry = projectHookRegistry;
        _hookTelemetry = hookTelemetry;
        _projectScope = projectScope;
        _logger = logger;
    }

    public async Task<HookResult> RunBeforeHookAsync(List<string>? hookNames, EntityHookContext ctx)
    {
        if (hookNames == null || hookNames.Count == 0)
        {
            return HookResult.Continue();
        }

        var phaseSw = Stopwatch.StartNew();
        int hookCount = 0;
        string phaseResult = "continue";

        foreach (var hookName in hookNames)
        {
            if (string.IsNullOrWhiteSpace(hookName))
            {
                continue;
            }

            var projectName = _projectScope.Current?.Name;
            if (!string.IsNullOrEmpty(projectName))
            {
                var projectHook = _projectHookRegistry.Find(projectName, hookName, ctx);
                if (projectHook != null)
                {
                    _logger.LogDebug("プロジェクトフック '{Hook}' を実行 (Project={Project})", hookName, projectName);
                    var sw = Stopwatch.StartNew();
                    HookResult result;
                    try
                    {
                        result = await projectHook.BeforeAsync(ctx, _db, null);
                    }
                    catch (Exception ex)
                    {
                        _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                            Phase: "before",
                            Source: "project",
                            HookName: hookName,
                            Entity: ctx.Entity,
                            Operation: ctx.Operation,
                            Result: "error",
                            DurationMs: sw.ElapsedMilliseconds,
                            Exception: ex));
                        throw;
                    }

                    hookCount++;
                    _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                        Phase: "before",
                        Source: "project",
                        HookName: hookName,
                        Entity: ctx.Entity,
                        Operation: ctx.Operation,
                        Result: result.Cancel ? "cancel" : "continue",
                        DurationMs: sw.ElapsedMilliseconds,
                        CancelMessage: result.CancelMessage));
                    if (result.Cancel)
                    {
                        phaseResult = "cancel";
                        _logger.LogInformation(
                            "crud_hooks_before entity={Entity} op={Operation} result=cancel hookCount={HookCount} totalMs={TotalMs}",
                            ctx.Entity, ctx.Operation, hookCount, phaseSw.ElapsedMilliseconds);
                        return result;
                    }
                    continue;
                }
            }

            var frameworkHook = _hookRegistry.Find(hookName, ctx);
            if (frameworkHook != null)
            {
                _logger.LogDebug("フレームワークフック '{Hook}' を実行", hookName);
                var sw = Stopwatch.StartNew();
                HookResult result;
                try
                {
                    result = await frameworkHook.BeforeAsync(ctx, _db, null);
                }
                catch (Exception ex)
                {
                    _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                        Phase: "before",
                        Source: "framework",
                        HookName: hookName,
                        Entity: ctx.Entity,
                        Operation: ctx.Operation,
                        Result: "error",
                        DurationMs: sw.ElapsedMilliseconds,
                        Exception: ex));
                    throw;
                }

                hookCount++;
                _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                    Phase: "before",
                    Source: "framework",
                    HookName: hookName,
                    Entity: ctx.Entity,
                    Operation: ctx.Operation,
                    Result: result.Cancel ? "cancel" : "continue",
                    DurationMs: sw.ElapsedMilliseconds,
                    CancelMessage: result.CancelMessage));
                if (result.Cancel)
                {
                    phaseResult = "cancel";
                    _logger.LogInformation(
                        "crud_hooks_before entity={Entity} op={Operation} result=cancel hookCount={HookCount} totalMs={TotalMs}",
                        ctx.Entity, ctx.Operation, hookCount, phaseSw.ElapsedMilliseconds);
                    return result;
                }
                continue;
            }

            _logger.LogWarning("フック '{Name}' が登録されていません — スキップします", hookName);
            _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                Phase: "before",
                Source: "missing",
                HookName: hookName,
                Entity: ctx.Entity,
                Operation: ctx.Operation,
                Result: "skipped_not_found",
                DurationMs: 0));
        }

        _logger.LogInformation(
            "crud_hooks_before entity={Entity} op={Operation} result={Result} hookCount={HookCount} totalMs={TotalMs}",
            ctx.Entity, ctx.Operation, phaseResult, hookCount, phaseSw.ElapsedMilliseconds);
        return HookResult.Continue();
    }

    public async Task RunAfterHookAsync(List<string>? hookNames, EntityHookContext ctx, IDbTransaction tx)
    {
        if (hookNames == null || hookNames.Count == 0)
        {
            return;
        }

        var phaseSw = Stopwatch.StartNew();
        int hookCount = 0;

        foreach (var hookName in hookNames)
        {
            if (string.IsNullOrWhiteSpace(hookName))
            {
                continue;
            }

            var projectName = _projectScope.Current?.Name;
            if (!string.IsNullOrEmpty(projectName))
            {
                var projectHook = _projectHookRegistry.Find(projectName, hookName, ctx);
                if (projectHook != null)
                {
                    _logger.LogDebug("プロジェクトフック '{Hook}' を実行 (Project={Project})", hookName, projectName);
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await projectHook.AfterAsync(ctx, _db, tx);
                    }
                    catch (Exception ex)
                    {
                        _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                            Phase: "after",
                            Source: "project",
                            HookName: hookName,
                            Entity: ctx.Entity,
                            Operation: ctx.Operation,
                            Result: "error",
                            DurationMs: sw.ElapsedMilliseconds,
                            Exception: ex));
                        throw;
                    }

                    hookCount++;
                    _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                        Phase: "after",
                        Source: "project",
                        HookName: hookName,
                        Entity: ctx.Entity,
                        Operation: ctx.Operation,
                        Result: "continue",
                        DurationMs: sw.ElapsedMilliseconds));
                    continue;
                }
            }

            var frameworkHook = _hookRegistry.Find(hookName, ctx);
            if (frameworkHook != null)
            {
                _logger.LogDebug("フレームワークフック '{Hook}' を実行", hookName);
                var sw = Stopwatch.StartNew();
                try
                {
                    await frameworkHook.AfterAsync(ctx, _db, tx);
                }
                catch (Exception ex)
                {
                    _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                        Phase: "after",
                        Source: "framework",
                        HookName: hookName,
                        Entity: ctx.Entity,
                        Operation: ctx.Operation,
                        Result: "error",
                        DurationMs: sw.ElapsedMilliseconds,
                        Exception: ex));
                    throw;
                }

                hookCount++;
                _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                    Phase: "after",
                    Source: "framework",
                    HookName: hookName,
                    Entity: ctx.Entity,
                    Operation: ctx.Operation,
                    Result: "continue",
                    DurationMs: sw.ElapsedMilliseconds));
                continue;
            }

            _logger.LogWarning("フック '{Name}' が登録されていません — スキップします", hookName);
            _hookTelemetry.Record(new HookExecutionTelemetryEvent(
                Phase: "after",
                Source: "missing",
                HookName: hookName,
                Entity: ctx.Entity,
                Operation: ctx.Operation,
                Result: "skipped_not_found",
                DurationMs: 0));
        }

        _logger.LogInformation(
            "crud_hooks_after entity={Entity} op={Operation} hookCount={HookCount} totalMs={TotalMs}",
            ctx.Entity, ctx.Operation, hookCount, phaseSw.ElapsedMilliseconds);
    }

    public async Task TryWriteHookRejectAuditAsync(
        string entity,
        CrudOperation operation,
        string? keyValue,
        List<string>? hookNames,
        string? reason,
        string? userName)
    {
        try
        {
            var hooks = hookNames == null || hookNames.Count == 0
                ? Array.Empty<string>()
                : hookNames.Where(h => !string.IsNullOrWhiteSpace(h)).ToArray();
            var keyText = string.IsNullOrWhiteSpace(keyValue) ? "(new)" : keyValue;
            var reasonText = reason ?? "-";
            var reasonCode = HookRejectReasonClassifier.Classify(reasonText);
            var detailObj = new
            {
                type = "hook_rejected",
                project = _projectScope.Current?.Name,
                entity,
                operation = operation.ToString(),
                key = keyText,
                hooks,
                hooksCsv = hooks.Length == 0 ? "-" : string.Join(",", hooks),
                reason = reasonText,
                reasonCode,
                at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            var detail = JsonSerializer.Serialize(detailObj);
            await _audit.WriteAsync("hook_rejected", entity, detail, userName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hook reject audit write failed. entity={Entity}, op={Operation}", entity, operation);
        }
    }

    public async Task WriteCrudAuditAsync(string action, string entity, string detail, string? userName, IDbTransaction tx)
    {
        await _audit.WriteAsync(action, entity, detail, userName, _db, tx);
    }

    public async Task ExecuteCrudTransactionAsync(Func<IDbTransaction, Task> action)
    {
        if (_db.State != ConnectionState.Open)
        {
            _db.Open();
        }

        var sw = Stopwatch.StartNew();
        using var tx = _db.BeginTransaction();
        try
        {
            await action(tx);
            tx.Commit();
            _logger.LogInformation(
                "crud_tx result=commit durationMs={DurationMs} project={Project}",
                sw.ElapsedMilliseconds,
                _projectScope.Current?.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "crud_tx result=rollback durationMs={DurationMs} project={Project}",
                sw.ElapsedMilliseconds,
                _projectScope.Current?.Name);
            tx.Rollback();
            throw;
        }
    }
}
