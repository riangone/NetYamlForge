// todo-app プロジェクト固有フック
// Task エンティティの入力検証・自動補完フックのサンプル実装です。
//
// YAML での参照例 (entities/task.yml):
//   hooks:
//     beforeCreate: [normalize_title, validate_due_date]
//     afterCreate:  [audit_log]
//     beforeUpdate: [normalize_title, validate_due_date, set_completed_at]
//     afterUpdate:  [audit_log]

using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TodoApp.Hooks;

/// <summary>
/// タスクのタイトルを正規化するフック（前後の空白を除去）。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class NormalizeTitleHook : IEntityHook
{
    public string Name => "normalize_title";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("Title", out var title) && title is string s)
        {
            ctx.Values["Title"] = s.Trim();
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 期限日（DueDate）が過去でないことを検証するフック。
/// 値が空または未指定の場合はスキップします。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateTaskDueDateHook : IEntityHook
{
    public string Name => "validate_due_date";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("DueDate", out var dueDateVal) || dueDateVal == null)
            return Task.FromResult(HookResult.Continue());

        // ValueConverter が date 型を DateTime オブジェクトに変換済みの場合を優先処理
        if (dueDateVal is DateTime dt)
        {
            if (dt.Date < DateTime.Today)
                return Task.FromResult(HookResult.Abort("期限日には本日以降の日付を入力してください。"));
            return Task.FromResult(HookResult.Continue());
        }

        var dueDateStr = dueDateVal.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dueDateStr))
            return Task.FromResult(HookResult.Continue());

        if (DateOnly.TryParse(dueDateStr, out var dueDate) && dueDate < DateOnly.FromDateTime(DateTime.Today))
            return Task.FromResult(HookResult.Abort("期限日には本日以降の日付を入力してください。"));

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// ステータスが 'done' に変更された際に CompletedAt を自動設定するフック。
/// 既に CompletedAt が設定されている場合は上書きしません。
/// YAML: beforeUpdate に指定します。
/// </summary>
public class SetCompletedAtHook : IEntityHook
{
    public string Name => "set_completed_at";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("Status", out var statusVal))
            return Task.FromResult(HookResult.Continue());

        var status = statusVal?.ToString() ?? string.Empty;
        if (!status.Equals("done", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(HookResult.Continue());

        // CompletedAt が未設定の場合のみ今日の日付をセット
        var hasCompleted = ctx.Values.TryGetValue("CompletedAt", out var completedAt)
            && completedAt != null
            && !string.IsNullOrWhiteSpace(completedAt.ToString());

        if (!hasCompleted)
        {
            ctx.Values["CompletedAt"] = DateTime.Today.ToString("yyyy-MM-dd");
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
