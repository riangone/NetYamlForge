// 責務: チケット更新時にステータス遷移の有効性を検証する。
// 不正な遷移（例: 完了 → 新規 など）を Abort で止める。

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Redmine.Hooks;

/// <summary>
/// [Redmine] チケットのステータス遷移を検証するフック。
/// クローズ済みチケット（is_closed=1）は「解決済み」「フィードバック」以外へのみ再オープン可能。
/// 有効な遷移: 新規(1)→進行中(2)→解決済み(3)→フィードバック(4)→完了(5) / 新規→却下(6)
///
/// entities.yml での使用例:
///   hooks:
///     beforeUpdate: [validate_issue_status_transition]
/// </summary>
public sealed class ValidateIssueStatusTransitionHook : IEntityHook
{
    private readonly ILogger<ValidateIssueStatusTransitionHook> _logger;

    public string Name => "validate_issue_status_transition";

    // 許可される遷移マップ: fromStatusId → [toStatusId, ...]
    // -1 は「任意のステータスから」を意味する
    private static readonly Dictionary<int, HashSet<int>> AllowedTransitions = new()
    {
        [1] = new HashSet<int> { 1, 2, 6 },       // 新規 → 進行中, 却下
        [2] = new HashSet<int> { 1, 2, 3, 4, 6 }, // 進行中 → 新規, 解決済み, フィードバック, 却下
        [3] = new HashSet<int> { 2, 3, 4, 5 },    // 解決済み → 進行中, フィードバック, 完了
        [4] = new HashSet<int> { 2, 3, 4, 5 },    // フィードバック → 進行中, 解決済み, 完了
        [5] = new HashSet<int> { 1, 2 },           // 完了 → 新規, 進行中（再オープン）
        [6] = new HashSet<int> { 1, 2 },           // 却下 → 新規, 進行中（再オープン）
    };

    public ValidateIssueStatusTransitionHook(ILogger<ValidateIssueStatusTransitionHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // status_id が変更された場合のみチェック
        if (!ctx.Values.TryGetValue("status_id", out var newStatusIdObj) || newStatusIdObj == null)
            return HookResult.Continue();
        if (!int.TryParse(newStatusIdObj.ToString(), out var newStatusId))
            return HookResult.Continue();

        // 編集対象チケットのIDを取得
        if (!ctx.Values.TryGetValue("id", out var idObj) || idObj == null)
            return HookResult.Continue();
        if (!int.TryParse(idObj.ToString(), out var issueId))
            return HookResult.Continue();

        // 現在のステータスをDBから取得
        var currentStatusId = await db.ExecuteScalarAsync<int?>(
            "SELECT status_id FROM issue WHERE id = @IssueId",
            new { IssueId = issueId }, tx);

        if (currentStatusId == null)
            return HookResult.Continue();

        // 同じステータスへの変更は常に許可
        if (currentStatusId == newStatusId)
            return HookResult.Continue();

        // 遷移マップに基づいてチェック
        if (AllowedTransitions.TryGetValue(currentStatusId.Value, out var allowed)
            && allowed.Contains(newStatusId))
        {
            _logger.LogDebug(
                "[Hook:validate_issue_status_transition] 遷移許可: チケット#{IssueId} {From}→{To}",
                issueId, currentStatusId, newStatusId);
            return HookResult.Continue();
        }

        // 遷移マップに現在のステータスがない場合は許可（マスタ追加時の互換性）
        if (!AllowedTransitions.ContainsKey(currentStatusId.Value))
            return HookResult.Continue();

        _logger.LogWarning(
            "[Hook:validate_issue_status_transition] 遷移禁止: チケット#{IssueId} {From}→{To}",
            issueId, currentStatusId, newStatusId);

        var currentName = await db.ExecuteScalarAsync<string>(
            "SELECT name FROM issue_status WHERE id = @Id",
            new { Id = currentStatusId }, tx) ?? $"ステータス{currentStatusId}";
        var newName = await db.ExecuteScalarAsync<string>(
            "SELECT name FROM issue_status WHERE id = @Id",
            new { Id = newStatusId }, tx) ?? $"ステータス{newStatusId}";

        return HookResult.Abort(
            $"ステータス遷移「{currentName} → {newName}」は許可されていません。");
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
        => Task.CompletedTask;
}
