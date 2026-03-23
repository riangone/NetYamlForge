// todo-app プロジェクト固有カスタムアクションハンドラー
//
// YAML での参照例 (entities/task.yml):
//   actions:
//     mark_done:
//       label: "完了にする"
//       scope: row
//       handler: mark_done
//     reopen:
//       label: "再オープン"
//       scope: row
//       handler: reopen_task
//       inputs:
//         - name: Reason
//           type: string
//           label: 再オープン理由
//           required: true
//     bulk_close_overdue:
//       label: "期限切れを一括クローズ"
//       scope: header
//       handler: bulk_close_overdue

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TodoApp.Hooks;

/// <summary>
/// タスクを「完了」ステータスに更新するアクションハンドラー。
/// YAML: actions.mark_done.handler = "mark_done"（scope: row）
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
/// YAML: actions.reopen.handler = "reopen_task"（scope: row）
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

/// <summary>
/// 期限切れのタスクをすべてキャンセルにするヘッダーアクションハンドラー。
/// YAML: actions.bulk_close_overdue.handler = "bulk_close_overdue"（scope: header）
/// </summary>
public class BulkCloseOverdueHandler : ICustomActionHandler
{
    public string Name => "bulk_close_overdue";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        await db.ExecuteAsync(
            @"UPDATE Task
              SET Status = 'cancelled'
              WHERE DueDate < @today
                AND Status NOT IN ('done', 'cancelled')",
            new { today },
            tx);

        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// CSV ファイルをアップロードしてタスクを一括インポートするハンドラー。
///
/// YAML 定義例 (entities/task.yml):
///   actions:
///     import_csv:
///       label: "CSV インポート"
///       scope: header
///       handler: import_tasks_csv
///       inputs:
///         - name: CsvFile
///           type: file
///           label: タスク CSV ファイル
///           required: true
///           allowedExtensions: ".csv"
///           maxSizeBytes: 5242880
///         - name: DefaultStatus
///           type: dropdown
///           label: インポート時のステータス
///           required: true
///           options: [todo, in_progress]
///
/// CSV フォーマット（1 行目をヘッダーとして扱う）:
///   Title,Priority,DueDate,AssignedTo
///   タスクA,high,2026-04-01,alice
/// </summary>
public class ImportTasksCsvHandler : ICustomActionHandler
{
    public string Name => "import_tasks_csv";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // アップロードされたファイルのパスを取得
        if (!ctx.Files.TryGetValue("CsvFile", out var csvPath) || string.IsNullOrWhiteSpace(csvPath))
            return ActionHandlerResult.Failure("CSV ファイルが見つかりません。");

        var defaultStatus = ctx.Inputs.TryGetValue("DefaultStatus", out var s) ? s?.ToString() ?? "todo" : "todo";

        // CSV を読み込む（1 行目 = ヘッダー）
        var lines = await File.ReadAllLinesAsync(csvPath);
        if (lines.Length < 2)
            return ActionHandlerResult.Failure("CSV にデータ行がありません。");

        var headers = ParseCsvLine(lines[0]);
        int colTitle    = Array.IndexOf(headers, "Title");
        int colPriority = Array.IndexOf(headers, "Priority");
        int colDueDate  = Array.IndexOf(headers, "DueDate");
        int colAssigned = Array.IndexOf(headers, "AssignedTo");

        if (colTitle < 0)
            return ActionHandlerResult.Failure("CSV に 'Title' 列が見つかりません。");

        int inserted = 0;
        var errors = new List<string>();

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            var cells = ParseCsvLine(lines[i]);
            var title = colTitle < cells.Length ? cells[colTitle].Trim() : "";
            if (string.IsNullOrWhiteSpace(title))
            {
                errors.Add($"行 {i + 1}: Title が空です。スキップしました。");
                continue;
            }

            var priority  = colPriority >= 0 && colPriority < cells.Length ? cells[colPriority].Trim() : "medium";
            var dueDate   = colDueDate  >= 0 && colDueDate  < cells.Length ? cells[colDueDate].Trim()  : null;
            var assignedTo = colAssigned >= 0 && colAssigned < cells.Length ? cells[colAssigned].Trim() : null;

            await db.ExecuteAsync(
                @"INSERT INTO Task (Title, Status, Priority, DueDate, AssignedTo, CreatedAt, UpdatedAt)
                  VALUES (@title, @status, @priority, @dueDate, @assignedTo, @now, @now)",
                new
                {
                    title,
                    status = defaultStatus,
                    priority,
                    dueDate = string.IsNullOrWhiteSpace(dueDate) ? null : dueDate,
                    assignedTo = string.IsNullOrWhiteSpace(assignedTo) ? null : assignedTo,
                    now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                },
                tx);

            inserted++;
        }

        if (errors.Count > 0)
        {
            // 部分成功の場合はメッセージを添えて成功扱い
            // （全件失敗は Failure を返してもよい）
        }

        return inserted == 0
            ? ActionHandlerResult.Failure("インポートできる行がありませんでした。")
            : ActionHandlerResult.Success();
    }

    /// <summary>RFC 4180 準拠の CSV 行を列配列にパースします。</summary>
    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                // クォートフィールド
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"'); i += 2;
                    }
                    else if (line[i] == '"')
                    {
                        i++; break;
                    }
                    else
                    {
                        sb.Append(line[i++]);
                    }
                }
                result.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                var end = line.IndexOf(',', i);
                if (end < 0) end = line.Length;
                result.Add(line[i..end]);
                i = end + 1;
            }
        }
        return result.ToArray();
    }
}
