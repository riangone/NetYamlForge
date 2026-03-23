// ファイル概要: エンティティCRUD操作のフック実行とトランザクション管理を担当するサービス。
// 責務: BeforeHook → DB書き込み → AfterHook をトランザクション内で一貫実行する。
// ビジネスロジックは持たない。SQL生成は DynamicCrudRepository、フック実装は各Hookクラスに委譲。
// このファイルを変更する場面: フックの優先順位変更・トランザクション境界の調整のみ。

using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

public sealed class EntityCrudExecutionService
{
    private readonly IDbConnection _db;
    private readonly IAuditLogService _audit;
    private readonly HookExecutionService _hookExecution;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<EntityCrudExecutionService> _logger;

    /// <summary>
    /// DIコンテナから直接フック依存を受け取るコンストラクタ。
    /// HookExecutionService を内部生成し、テストでも直接インスタンス化可能。
    /// </summary>
    [ActivatorUtilitiesConstructor]
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
        _hookExecution = new HookExecutionService(
            hookRegistry,
            projectHookRegistry,
            hookTelemetry,
            NullLogger<HookExecutionService>.Instance);
        _projectScope = projectScope;
        _logger = logger;
    }

    public async Task<HookResult> RunBeforeHookAsync(List<string>? hookNames, EntityHookContext ctx)
    {
        var phaseSw = Stopwatch.StartNew();
        var hookCount = hookNames?.Count(h => !string.IsNullOrWhiteSpace(h)) ?? 0;
        var result = await _hookExecution.RunBeforeAsync(
            hookNames,
            ctx,
            _projectScope.Current?.Name,
            _db,
            null);

        _logger.LogInformation(
            "crud_hooks_before entity={Entity} op={Operation} result={Result} hookCount={HookCount} totalMs={TotalMs}",
            ctx.Entity,
            ctx.Operation,
            result.Cancel ? "cancel" : "continue",
            hookCount,
            phaseSw.ElapsedMilliseconds);

        return result;
    }

    public async Task RunAfterHookAsync(List<string>? hookNames, EntityHookContext ctx, IDbTransaction tx)
    {
        var phaseSw = Stopwatch.StartNew();
        var hookCount = hookNames?.Count(h => !string.IsNullOrWhiteSpace(h)) ?? 0;

        await _hookExecution.RunAfterAsync(
            hookNames,
            ctx,
            _projectScope.Current?.Name,
            _db,
            tx);

        _logger.LogInformation(
            "crud_hooks_after entity={Entity} op={Operation} hookCount={HookCount} totalMs={TotalMs}",
            ctx.Entity,
            ctx.Operation,
            hookCount,
            phaseSw.ElapsedMilliseconds);
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

    /// <summary>
    /// カスタムアクションハンドラーをトランザクション内で実行します。
    /// ハンドラーが ActionHandlerResult.Failure を返した場合はロールバックします。
    /// </summary>
    public async Task<ActionHandlerResult> ExecuteActionAsync(ICustomActionHandler handler, CustomActionContext ctx)
    {
        if (_db.State != ConnectionState.Open)
            _db.Open();

        var result = ActionHandlerResult.Failure("実行前エラー");
        using var tx = _db.BeginTransaction();
        try
        {
            result = await handler.ExecuteAsync(ctx, _db, tx);
            if (result.Ok)
            {
                tx.Commit();
            }
            else
            {
                tx.Rollback();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "action_tx result=rollback action={Action}", ctx.Action);
            tx.Rollback();
            throw;
        }
        return result;
    }
}
