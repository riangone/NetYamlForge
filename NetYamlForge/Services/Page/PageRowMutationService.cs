// ファイル概要: ページ（カスタムページ・セクション）単位の行単位ミューテーション処理を担当するサービス。
// 責務: 識別子の安全性検証 → プロジェクト固有バリデーター委譲 → SQL実行。
// SQL 生成・実行は IRowMutationRepository に委譲し、フック実行は HookExecutionService に委譲します。
// ビジネスロジックは projects/<name>/Hooks/<Name>PageMutationValidator.cs に実装すること。
// このファイルを変更する場面: 識別子検証・共通フロー変更のみ。

using System.Data;
using NetYamlForge.Models;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

public sealed class PageRowMutationService
{
    // IdentifierRegex は SqlSafetyGuard から取得（改善5.1：重複定義排除）
    private static readonly System.Text.RegularExpressions.Regex IdentifierRegex = SqlSafetyGuard.IdentifierRegex;

    private readonly IDbConnection _db;
    private readonly IProjectPageMutationValidatorRegistry _validatorRegistry;
    private readonly HookExecutionService _hookExecution;
    private readonly IRowMutationRepository _mutationRepo;
    private readonly ILogger<PageRowMutationService> _logger;

    public PageRowMutationService(
        IDbConnection db,
        IProjectPageMutationValidatorRegistry validatorRegistry,
        HookExecutionService hookExecution,
        IRowMutationRepository mutationRepo,
        ILogger<PageRowMutationService> logger)
    {
        _db = db;
        _validatorRegistry = validatorRegistry;
        _hookExecution = hookExecution;
        _mutationRepo = mutationRepo;
        _logger = logger;
    }

    public async Task<(bool ok, string? message)> UpdateRowAsync(
        string projectName,
        SectionDefinition section,
        int rowId,
        string field,
        string value,
        string? actorUserName,
        string? actorDisplayName)
    {
        if (string.IsNullOrWhiteSpace(section.TargetTable) || string.IsNullOrWhiteSpace(section.TargetPrimaryKey))
            return (false, "target_table または target_primary_key が設定されていません。");

        if (!IdentifierRegex.IsMatch(section.TargetTable)  ||
            !IdentifierRegex.IsMatch(section.TargetPrimaryKey) ||
            !IdentifierRegex.IsMatch(field))
            return (false, "無効な識別子です。");

        // プロジェクト固有の検証・副作用処理に委譲
        var validator = _validatorRegistry.Get(projectName);
        if (validator != null)
        {
            var result = await validator.ValidatePageUpdateAsync(
                section.TargetTable, rowId, field, value, _db, null);
            if (!result.ok) return result;
        }

        await _mutationRepo.UpdateAsync(
            section.TargetTable,
            section.TargetPrimaryKey,
            rowId,
            new[] { KeyValuePair.Create(field, (object?)value) });
        return (true, null);
    }

    public async Task<(bool ok, string? message)> InsertRowAsync(
        string projectName,
        SectionDefinition section,
        Dictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(section.TargetTable))
            return (false, "target_table が設定されていません。");
        if (!IdentifierRegex.IsMatch(section.TargetTable))
            return (false, "無効なテーブル名です。");

        var safeFields = fields
            .Where(kv => IdentifierRegex.IsMatch(kv.Key))
            .ToList();
        if (safeFields.Count == 0)
            return (false, "有効なフィールドがありません。");

        var ctx = new EntityHookContext
        {
            Entity = section.TargetTable,
            Operation = CrudOperation.Create,
            Values = safeFields.ToDictionary(f => f.Key, f => (object?)(string.IsNullOrWhiteSpace(f.Value) ? null : f.Value))
        };

        var beforeHooks = section.Hooks?.BeforeCreate ?? new();
        var beforeHookResult = await _hookExecution.RunBeforeAsync(beforeHooks, ctx, projectName, _db, null);
        if (beforeHookResult.Cancel) return (false, beforeHookResult.CancelMessage ?? "前処理によりキャンセルされました。");

        var insertFields = safeFields
            .Select(f => KeyValuePair.Create(f.Key,
                ctx.Values.TryGetValue(f.Key, out var v) ? v : (string.IsNullOrWhiteSpace(f.Value) ? null : (object?)f.Value)))
            .ToArray();

        await _mutationRepo.InsertAsync(section.TargetTable, insertFields);

        var afterHooks = section.Hooks?.AfterCreate ?? new();
        await _hookExecution.RunAfterAsync(afterHooks, ctx, projectName, _db, null);

        return (true, null);
    }

    public async Task<(bool ok, string? message)> UpdateAllFieldsAsync(
        string projectName,
        SectionDefinition section,
        int rowId,
        Dictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(section.TargetTable) || string.IsNullOrWhiteSpace(section.TargetPrimaryKey))
            return (false, "target_table または target_primary_key が設定されていません。");
        if (!IdentifierRegex.IsMatch(section.TargetTable) || !IdentifierRegex.IsMatch(section.TargetPrimaryKey))
            return (false, "無効な識別子です。");

        var safeFields = fields
            .Where(kv => IdentifierRegex.IsMatch(kv.Key) &&
                         !string.Equals(kv.Key, section.TargetPrimaryKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (safeFields.Count == 0)
            return (false, "更新対象フィールドがありません。");

        var ctx = new EntityHookContext
        {
            Entity = section.TargetTable,
            Operation = CrudOperation.Update,
            Id = rowId,
            Values = safeFields.ToDictionary(f => f.Key, f => (object?)(string.IsNullOrWhiteSpace(f.Value) ? null : f.Value))
        };

        var beforeHooks = section.Hooks?.BeforeUpdate ?? new();
        var beforeHookResult = await _hookExecution.RunBeforeAsync(beforeHooks, ctx, projectName, _db, null);
        if (beforeHookResult.Cancel) return (false, beforeHookResult.CancelMessage ?? "前処理によりキャンセルされました。");

        var updateFields = safeFields
            .Select(f => KeyValuePair.Create(f.Key,
                ctx.Values.TryGetValue(f.Key, out var v) ? v : (string.IsNullOrWhiteSpace(f.Value) ? null : (object?)f.Value)))
            .ToArray();

        await _mutationRepo.UpdateAsync(section.TargetTable, section.TargetPrimaryKey, rowId, updateFields);

        var afterHooks = section.Hooks?.AfterUpdate ?? new();
        await _hookExecution.RunAfterAsync(afterHooks, ctx, projectName, _db, null);

        return (true, null);
    }

    public async Task<(bool ok, string? message)> DeleteRowAsync(
        string projectName,
        SectionDefinition section,
        int rowId)
    {
        if (string.IsNullOrWhiteSpace(section.TargetTable) || string.IsNullOrWhiteSpace(section.TargetPrimaryKey))
            return (false, "target_table または target_primary_key が設定されていません。");

        if (!IdentifierRegex.IsMatch(section.TargetTable) ||
            !IdentifierRegex.IsMatch(section.TargetPrimaryKey))
            return (false, "無効な識別子です。");

        var ctx = new EntityHookContext
        {
            Entity = section.TargetTable,
            Operation = CrudOperation.Delete,
            Id = rowId,
            Values = new()
        };

        var beforeHooks = section.Hooks?.BeforeDelete ?? new();
        var beforeHookResult = await _hookExecution.RunBeforeAsync(beforeHooks, ctx, projectName, _db, null);
        if (beforeHookResult.Cancel) return (false, beforeHookResult.CancelMessage ?? "前処理によりキャンセルされました。");

        // プロジェクト固有の削除検証に委譲
        var validator = _validatorRegistry.Get(projectName);
        if (validator != null)
        {
            var result = await validator.ValidatePageDeleteAsync(
                section.TargetTable, rowId, _db, null);
            if (!result.ok) return result;
        }

        await _mutationRepo.DeleteAsync(section.TargetTable, section.TargetPrimaryKey, rowId);

        var afterHooks = section.Hooks?.AfterDelete ?? new();
        await _hookExecution.RunAfterAsync(afterHooks, ctx, projectName, _db, null);

        return (true, null);
    }
}
