// todo-app プロジェクト固有カスタムアクションハンドラー
//
// YAML での参照例 (entities/task.yml):
//   actions:
//     mark_done:
//       label: "完了にする"
//       handler: mark_done
//     reopen:
//       label: "再オープン"
//       handler: reopen_task
//       inputs:
//         - name: Reason
//           type: string
//           label: 再オープン理由
//           required: true

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TodoApp.Hooks;

/// <summary>
/// タスクを「完了」ステータスに更新するアクションハンドラー。
/// YAML: actions.mark_done.handler = "mark_done"
/// </summary>
public class MarkDoneHandler : ICustomActionHandler
{
    public string Name => "mark_done";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("レコード ID が指定されていません。");

        if (!int.TryParse(ctx.RecordId, out var id))
            return ActionHandlerResult.Failure("無効なレコード ID です。");

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var affected = await db.ExecuteAsync(
            "UPDATE Task SET Status = 'done', CompletedAt = @today WHERE Id = @id",
            new { today, id },
            tx);

        if (affected <= 0)
            return ActionHandlerResult.Failure("対象タスクが見つかりません。");

        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// タスクを「pending」ステータスに戻すアクションハンドラー。
/// YAML: actions.reopen.handler = "reopen_task"
/// inputs: Reason（必須）
/// </summary>
public class ReopenTaskHandler : ICustomActionHandler
{
    public string Name => "reopen_task";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("レコード ID が指定されていません。");

        if (!int.TryParse(ctx.RecordId, out var id))
            return ActionHandlerResult.Failure("無効なレコード ID です。");

        var reason = ctx.Inputs.TryGetValue("Reason", out var r) ? r?.ToString() : null;
        if (string.IsNullOrWhiteSpace(reason))
            return ActionHandlerResult.Failure("再オープン理由を入力してください。");

        var description = $"[再オープン: {reason}]";
        var affected = await db.ExecuteAsync(
            @"UPDATE Task
              SET Status = 'pending',
                  CompletedAt = NULL,
                  Description = CASE WHEN Description IS NULL OR Description = ''
                                     THEN @desc
                                     ELSE Description || ' ' || @desc END
              WHERE Id = @id",
            new { desc = description, id },
            tx);

        if (affected <= 0)
            return ActionHandlerResult.Failure("対象タスクが見つかりません。");

        return ActionHandlerResult.Success();
    }
}
