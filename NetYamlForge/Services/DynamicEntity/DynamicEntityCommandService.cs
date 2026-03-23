// ファイル概要: エンティティの Create / Update / Delete コマンドを統括するサービスです。
// フック実行（BeforeHook → DB操作 → AfterHook）とトランザクション管理を
// EntityCrudExecutionService に委譲し、結果を CommandResult 型で返します。
// このファイルを変更する場面: 新たな CRUD 操作（例: Upsert）追加時のみ。

using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

/// <summary>
/// エンティティ CRUD コマンドのファサード。
/// DynamicEntityController から呼ばれ、Create / Update / Delete の
/// フック実行・トランザクション管理・監査ログ記録を一括で処理します。
/// </summary>
public sealed class DynamicEntityCommandService
{
    private const string ConcurrencyConflictCode = CommandErrorCodes.ConcurrencyConflictOrNotFound;
    private const string ConcurrencyConflictMessage = "対象データが更新済みか、既に削除されています。";

    private readonly IDynamicCrudRepository _repo;
    private readonly EntityCrudExecutionService _crudExecutionService;

    public DynamicEntityCommandService(
        IDynamicCrudRepository repo,
        EntityCrudExecutionService crudExecutionService)
    {
        _repo = repo;
        _crudExecutionService = crudExecutionService;
    }

    /// <summary>
    /// 新規レコードを作成します。
    /// BeforeHook → INSERT（トランザクション） → AfterHook の順で実行されます。
    /// </summary>
    /// <param name="entity">エンティティ名（YAML キー）</param>
    /// <param name="values">フォーム変換済みの値マップ</param>
    /// <param name="beforeHooks">entities.yml の hooks.beforeCreate フック名リスト</param>
    /// <param name="afterHooks">entities.yml の hooks.afterCreate フック名リスト</param>
    /// <param name="userName">操作ユーザー名（監査ログ用）</param>
    /// <returns>成功時は新規 ID、失敗時はエラーコードとメッセージ</returns>
    public async Task<CommandResult<int>> CreateAsync(
        string entity,
        IDictionary<string, object?> values,
        List<string>? beforeHooks,
        List<string>? afterHooks,
        string? userName)
    {
        var hookCtx = new EntityHookContext
        {
            Entity = entity,
            Operation = CrudOperation.Create,
            Values = new Dictionary<string, object?>(values),
            UserName = userName
        };

        var beforeHookResult = await _crudExecutionService.RunBeforeHookAsync(beforeHooks, hookCtx);
        if (beforeHookResult.Cancel)
        {
            await _crudExecutionService.TryWriteHookRejectAuditAsync(
                entity,
                CrudOperation.Create,
                null,
                beforeHooks,
                beforeHookResult.CancelMessage,
                userName);
            return CommandResult<int>.Failure(
                CommandErrorCodes.HookRejectedBeforeCreate,
                beforeHookResult.CancelMessage ?? "前処理によりキャンセルされました。");
        }

        var newId = 0;
        await _crudExecutionService.ExecuteCrudTransactionAsync(async tx =>
        {
            newId = await _repo.InsertAsync(entity, hookCtx.Values, tx);
            hookCtx.Id = newId;
            await _crudExecutionService.WriteCrudAuditAsync("create", entity, $"Created {entity}", userName, tx);
            await _crudExecutionService.RunAfterHookAsync(afterHooks, hookCtx, tx);
        });

        return CommandResult<int>.Success(newId);
    }

    /// <summary>
    /// 既存レコードを更新します。
    /// 更新行数が 0 の場合（楽観的排他競合 or レコード消失）は ConcurrencyConflict エラーを返します。
    /// </summary>
    public async Task<CommandResult> UpdateAsync(
        string entity,
        string keyName,
        string? keyValue,
        IDictionary<string, object?> values,
        List<string>? beforeHooks,
        List<string>? afterHooks,
        string? userName)
    {
        var hookCtx = new EntityHookContext
        {
            Entity = entity,
            Operation = CrudOperation.Update,
            Id = int.TryParse(keyValue, out var intId) ? intId : null,
            KeyValues = new Dictionary<string, object?> { [keyName] = keyValue },
            Values = new Dictionary<string, object?>(values),
            UserName = userName
        };

        var beforeHookResult = await _crudExecutionService.RunBeforeHookAsync(beforeHooks, hookCtx);
        if (beforeHookResult.Cancel)
        {
            await _crudExecutionService.TryWriteHookRejectAuditAsync(
                entity,
                CrudOperation.Update,
                keyValue,
                beforeHooks,
                beforeHookResult.CancelMessage,
                userName);
            return CommandResult.Failure(
                CommandErrorCodes.HookRejectedBeforeUpdate,
                beforeHookResult.CancelMessage ?? "前処理によりキャンセルされました。");
        }

        try
        {
            await _crudExecutionService.ExecuteCrudTransactionAsync(async tx =>
            {
                var affected = await _repo.UpdateAsync(entity, keyValue ?? string.Empty, hookCtx.Values, tx);
                if (affected <= 0)
                {
                    throw new NoRowsAffectedException("update");
                }

                await _crudExecutionService.WriteCrudAuditAsync("update", entity, $"Updated {entity} {keyName}={keyValue}", userName, tx);
                await _crudExecutionService.RunAfterHookAsync(afterHooks, hookCtx, tx);
            });
        }
        catch (NoRowsAffectedException)
        {
            return CommandResult.Failure(ConcurrencyConflictCode, ConcurrencyConflictMessage);
        }

        return CommandResult.Success();
    }

    /// <summary>
    /// レコードを削除（またはソフトデリート）します。
    /// 影響行数が 0 の場合は ConcurrencyConflict エラーを返します。
    /// </summary>
    public async Task<CommandResult> DeleteAsync(
        string entity,
        string keyName,
        string? keyValue,
        List<string>? beforeHooks,
        List<string>? afterHooks,
        string? userName)
    {
        var hookCtx = new EntityHookContext
        {
            Entity = entity,
            Operation = CrudOperation.Delete,
            Id = int.TryParse(keyValue, out var intId) ? intId : null,
            KeyValues = new Dictionary<string, object?> { [keyName] = keyValue },
            Values = new Dictionary<string, object?>(),
            UserName = userName
        };

        var beforeHookResult = await _crudExecutionService.RunBeforeHookAsync(beforeHooks, hookCtx);
        if (beforeHookResult.Cancel)
        {
            await _crudExecutionService.TryWriteHookRejectAuditAsync(
                entity,
                CrudOperation.Delete,
                keyValue,
                beforeHooks,
                beforeHookResult.CancelMessage,
                userName);
            return CommandResult.Failure(
                CommandErrorCodes.HookRejectedBeforeDelete,
                beforeHookResult.CancelMessage ?? "前処理により削除がキャンセルされました。");
        }

        try
        {
            await _crudExecutionService.ExecuteCrudTransactionAsync(async tx =>
            {
                var affected = await _repo.DeleteAsync(entity, keyValue ?? string.Empty, tx);
                if (affected <= 0)
                {
                    throw new NoRowsAffectedException("delete");
                }

                await _crudExecutionService.WriteCrudAuditAsync("delete", entity, $"Deleted {entity} {keyName}={keyValue}", userName, tx);
                await _crudExecutionService.RunAfterHookAsync(afterHooks, hookCtx, tx);
            });
        }
        catch (NoRowsAffectedException)
        {
            return CommandResult.Failure(ConcurrencyConflictCode, ConcurrencyConflictMessage);
        }

        return CommandResult.Success();
    }

    /// <summary>
    /// カスタムアクションの before フックを実行します。
    /// </summary>
    public Task<HookResult> RunBeforeHooksForActionAsync(List<string> hookNames, EntityHookContext ctx)
        => _crudExecutionService.RunBeforeHookAsync(hookNames, ctx);

    /// <summary>
    /// カスタムアクションハンドラーをトランザクション内で実行します。
    /// </summary>
    public Task<ActionHandlerResult> ExecuteActionAsync(ICustomActionHandler handler, CustomActionContext ctx)
        => _crudExecutionService.ExecuteActionAsync(handler, ctx);

    // UPDATE/DELETE で影響行数が 0 だった場合にスローする内部例外。
    // ExecuteCrudTransactionAsync の外で catch して ConcurrencyConflict エラーに変換する。
    private sealed class NoRowsAffectedException : Exception
    {
        public NoRowsAffectedException(string operation)
            : base($"No rows affected for {operation}.")
        {
        }
    }
}
