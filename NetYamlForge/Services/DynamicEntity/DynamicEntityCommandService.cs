// ファイル概要: エンティティの Create / Update / Delete コマンドを統括するサービスです。
// フック実行（BeforeHook → DB操作 → AfterHook）とトランザクション管理を
// EntityCrudExecutionService に委譲し、結果を CommandResult 型で返します。
// このファイルを変更する場面: 新たな CRUD 操作（例: Upsert）追加時のみ。

using NetYamlForge.Services.Hooks;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetYamlForge.Services;

/// <summary>
/// エンティティ CRUD コマンドのファサード。
/// DynamicEntityController から呼ばれ、Create / Update / Delete の
/// フック実行・トランザクション管理・監査ログ記録を一括で処理します。
/// </summary>
public class DynamicEntityCommandService
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

    private static bool IsSensitiveField(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return false;
        var lower = fieldName.ToLowerInvariant();
        return lower.Contains("password") || 
               lower.Contains("pwd") || 
               lower.Contains("secret") || 
               lower.Contains("ssn") || 
               lower.Contains("token") || 
               lower.Contains("key");
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
    public virtual async Task<CommandResult<int>> CreateAsync(
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

            // 1. 自动计算审计日志的 JSON Diff (新建数据所有字段的 old 为 null)
            var diff = new Dictionary<string, object>();
            var changedFields = new Dictionary<string, object>();
            foreach (var kv in hookCtx.Values)
            {
                var isSensitive = IsSensitiveField(kv.Key);
                changedFields[kv.Key] = new { old = (object?)null, @new = isSensitive ? "[REDACTED]" : kv.Value };
            }
            diff["changed_fields"] = changedFields;

            await _crudExecutionService.WriteCrudAuditAsync("create", entity, JsonSerializer.Serialize(diff), userName, tx);
            await _crudExecutionService.RunAfterHookAsync(afterHooks, hookCtx, tx);
        });

        return CommandResult<int>.Success(newId);
    }

    /// <summary>
    /// 既存レコードを更新します。
    /// 更新行数が 0 の場合（楽観的排他競合 or レコード消失）は ConcurrencyConflict エラーを返します。
    /// </summary>
    public virtual async Task<CommandResult> UpdateAsync(
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

        // 变更追踪前置：查询旧记录
        IDictionary<string, object?>? beforeRow = null;
        if (keyValue != null)
        {
            beforeRow = await _repo.GetByIdAsync(entity, keyValue);
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

                // 2. 自动计算审计日志的 JSON Diff
                var diff = new Dictionary<string, object>();
                var changedFields = new Dictionary<string, object>();
                if (beforeRow != null)
                {
                    foreach (var kv in hookCtx.Values)
                    {
                        beforeRow.TryGetValue(kv.Key, out var oldVal);
                        
                        var isChanged = false;
                        if (oldVal == null && kv.Value != null) isChanged = true;
                        else if (oldVal != null && kv.Value == null) isChanged = true;
                        else if (oldVal != null && !oldVal.Equals(kv.Value)) isChanged = true;

                        if (isChanged)
                        {
                            var isSensitive = IsSensitiveField(kv.Key);
                            changedFields[kv.Key] = new 
                            { 
                                old = isSensitive ? "[REDACTED]" : oldVal, 
                                @new = isSensitive ? "[REDACTED]" : kv.Value 
                            };
                        }
                    }
                }
                diff["changed_fields"] = changedFields;

                await _crudExecutionService.WriteCrudAuditAsync("update", entity, JsonSerializer.Serialize(diff), userName, tx);
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
    public virtual async Task<CommandResult> DeleteAsync(
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

        // 变更追踪前置：查询旧记录
        IDictionary<string, object?>? beforeRow = null;
        if (keyValue != null)
        {
            beforeRow = await _repo.GetByIdAsync(entity, keyValue);
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

                // 3. 自动计算审计日志的 JSON Diff (删除数据所有字段的 new 为 null)
                var diff = new Dictionary<string, object>();
                var changedFields = new Dictionary<string, object>();
                if (beforeRow != null)
                {
                    foreach (var kv in beforeRow)
                    {
                        var isSensitive = IsSensitiveField(kv.Key);
                        changedFields[kv.Key] = new 
                        { 
                            old = isSensitive ? "[REDACTED]" : kv.Value, 
                            @new = (object?)null 
                        };
                    }
                }
                diff["changed_fields"] = changedFields;

                await _crudExecutionService.WriteCrudAuditAsync("delete", entity, JsonSerializer.Serialize(diff), userName, tx);
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
