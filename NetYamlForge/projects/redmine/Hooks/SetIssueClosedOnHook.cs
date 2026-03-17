// 責務: チケットのステータスがクローズ状態になったとき closed_on を自動設定する。
// issue.afterUpdate で実行。is_closed=1 のステータスに変更された場合のみ closed_on を UPDATE する。

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Redmine.Hooks;

/// <summary>
/// [Redmine] チケット更新後、ステータスがクローズ状態に変わった場合に closed_on を自動設定する。
/// is_closed=0 の状態に戻った場合は closed_on を NULL にクリアする。
///
/// entities.yml での使用例:
///   hooks:
///     afterUpdate: [set_issue_closed_on]
/// </summary>
public sealed class SetIssueClosedOnHook : IEntityHook
{
    private readonly ILogger<SetIssueClosedOnHook> _logger;

    public string Name => "set_issue_closed_on";

    public SetIssueClosedOnHook(ILogger<SetIssueClosedOnHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("status_id", out var statusIdObj) || statusIdObj == null)
            return;
        if (!int.TryParse(statusIdObj.ToString(), out var statusId))
            return;
        if (!ctx.Values.TryGetValue("id", out var idObj) || idObj == null)
            return;
        if (!int.TryParse(idObj.ToString(), out var issueId))
            return;

        // ステータスの is_closed フラグを確認
        var isClosed = await db.ExecuteScalarAsync<int>(
            "SELECT is_closed FROM issue_status WHERE id = @StatusId",
            new { StatusId = statusId }, tx);

        if (isClosed == 1)
        {
            // クローズ: closed_on を現在時刻に設定
#pragma warning disable DCS001
            await db.ExecuteAsync(
                "UPDATE issue SET closed_on = @Now WHERE id = @IssueId AND closed_on IS NULL",
                new { Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), IssueId = issueId }, tx);
#pragma warning restore DCS001
            _logger.LogInformation("[Hook:set_issue_closed_on] チケット#{IssueId} クローズ日時を設定", issueId);
        }
        else
        {
            // 再オープン: closed_on をクリア
#pragma warning disable DCS001
            await db.ExecuteAsync(
                "UPDATE issue SET closed_on = NULL WHERE id = @IssueId",
                new { IssueId = issueId }, tx);
#pragma warning restore DCS001
            _logger.LogInformation("[Hook:set_issue_closed_on] チケット#{IssueId} 再オープン・クローズ日時をクリア", issueId);
        }
    }
}
