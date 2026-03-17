// ファイル概要: ページ（カスタムページ・セクション）単位の行単位ミューテーション処理を担当するサービス。
// 責務: 識別子の安全性検証 → プロジェクト固有バリデーター委譲 → SQL実行。
// ビジネスロジックは projects/<name>/Hooks/<Name>PageMutationValidator.cs に実装すること。
// このファイルを変更する場面: 識別子検証・共通フロー変更のみ。

using System.Data;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Services;

public sealed class PageRowMutationService
{
    // IdentifierRegex は SqlSafetyGuard から取得（改善5.1：重複定義排除）
    private static readonly System.Text.RegularExpressions.Regex IdentifierRegex = SqlSafetyGuard.IdentifierRegex;

    private readonly IDbConnection _db;
    private readonly IProjectPageMutationValidatorRegistry _validatorRegistry;
    private readonly IEntityHookRegistry _hookRegistry;
    private readonly IProjectHookRegistry _projectHookRegistry;
    private readonly ILogger<PageRowMutationService> _logger;

    public PageRowMutationService(
        IDbConnection db,
        IProjectPageMutationValidatorRegistry validatorRegistry,
        IEntityHookRegistry hookRegistry,
        IProjectHookRegistry projectHookRegistry,
        ILogger<PageRowMutationService> logger)
    {
        _db = db;
        _validatorRegistry = validatorRegistry;
        _hookRegistry = hookRegistry;
        _projectHookRegistry = projectHookRegistry;
        _logger = logger;
    }

    /// <summary>
    /// フック名からフックを解決する。プロジェクト固有レジストリを優先し、なければ共通レジストリを参照する。
    /// </summary>
    private IEntityHook? ResolveHook(string projectName, string hookName, EntityHookContext ctx)
        => _projectHookRegistry.Find(projectName, hookName, ctx)
           ?? _hookRegistry.Find(hookName, ctx);

    /// <summary>
    /// セクション定義のフックリストを実行する。
    /// Before フックが Abort を返した場合は処理を中断する。
    /// </summary>
    private async Task<(bool ok, string? message)> RunBeforeHooksAsync(
        string projectName,
        List<string> hookNames,
        EntityHookContext ctx,
        IDbTransaction? tx)
    {
        foreach (var name in hookNames)
        {
            var hook = ResolveHook(projectName, name, ctx);
            if (hook == null) continue;
            var result = await hook.BeforeAsync(ctx, _db, tx);
            if (result.Cancel)
                return (false, result.CancelMessage ?? $"フック '{name}' が処理を中断しました。");
        }
        return (true, null);
    }

    private async Task RunAfterHooksAsync(
        string projectName,
        List<string> hookNames,
        EntityHookContext ctx,
        IDbTransaction? tx)
    {
        foreach (var name in hookNames)
        {
            var hook = ResolveHook(projectName, name, ctx);
            if (hook == null) continue;
            await hook.AfterAsync(ctx, _db, tx);
        }
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

        // Safe: TargetTable/field/TargetPrimaryKey は IdentifierRegex で検証済み
#pragma warning disable DCS001
        var sql = $"UPDATE \"{section.TargetTable}\" SET \"{field}\" = @Value WHERE \"{section.TargetPrimaryKey}\" = @RowId";
#pragma warning restore DCS001
        await _db.ExecuteAsync(sql, new { Value = value, RowId = rowId });
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
        var beforeResult = await RunBeforeHooksAsync(projectName, beforeHooks, ctx, null);
        if (!beforeResult.ok) return beforeResult;

        var cols = string.Join(", ", safeFields.Select(f => $"\"{f.Key}\""));
        var parms = string.Join(", ", safeFields.Select(f => $"@{f.Key}"));

        // Safe: TargetTable/col names validated by IdentifierRegex
#pragma warning disable DCS001
        var sql = $"INSERT INTO \"{section.TargetTable}\" ({cols}) VALUES ({parms})";
#pragma warning restore DCS001
        var param = new DynamicParameters();
        foreach (var f in safeFields)
            param.Add(f.Key, ctx.Values.TryGetValue(f.Key, out var v) ? v : (string.IsNullOrWhiteSpace(f.Value) ? null : (object)f.Value));

        await _db.ExecuteAsync(sql, param);

        var afterHooks = section.Hooks?.AfterCreate ?? new();
        await RunAfterHooksAsync(projectName, afterHooks, ctx, null);

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
        var beforeResult = await RunBeforeHooksAsync(projectName, beforeHooks, ctx, null);
        if (!beforeResult.ok) return beforeResult;

        var setSql = string.Join(", ", safeFields.Select(f => $"\"{f.Key}\" = @{f.Key}"));

        // Safe: TargetTable/field/TargetPrimaryKey validated by IdentifierRegex
#pragma warning disable DCS001
        var sql = $"UPDATE \"{section.TargetTable}\" SET {setSql} WHERE \"{section.TargetPrimaryKey}\" = @_RowId";
#pragma warning restore DCS001
        var param = new DynamicParameters();
        foreach (var f in safeFields)
            param.Add(f.Key, ctx.Values.TryGetValue(f.Key, out var v) ? v : (string.IsNullOrWhiteSpace(f.Value) ? null : (object)f.Value));
        param.Add("_RowId", rowId);

        await _db.ExecuteAsync(sql, param);

        var afterHooks = section.Hooks?.AfterUpdate ?? new();
        await RunAfterHooksAsync(projectName, afterHooks, ctx, null);

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
        var beforeResult = await RunBeforeHooksAsync(projectName, beforeHooks, ctx, null);
        if (!beforeResult.ok) return beforeResult;

        // プロジェクト固有の削除検証に委譲
        var validator = _validatorRegistry.Get(projectName);
        if (validator != null)
        {
            var result = await validator.ValidatePageDeleteAsync(
                section.TargetTable, rowId, _db, null);
            if (!result.ok) return result;
        }

        // Safe: TargetTable/TargetPrimaryKey は IdentifierRegex で検証済み
#pragma warning disable DCS001
        var sql = $"DELETE FROM \"{section.TargetTable}\" WHERE \"{section.TargetPrimaryKey}\" = @RowId";
#pragma warning restore DCS001
        await _db.ExecuteAsync(sql, new { RowId = rowId });

        var afterHooks = section.Hooks?.AfterDelete ?? new();
        await RunAfterHooksAsync(projectName, afterHooks, ctx, null);

        return (true, null);
    }
}
