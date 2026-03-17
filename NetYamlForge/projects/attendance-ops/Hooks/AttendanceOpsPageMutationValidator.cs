// ファイル概要: attendance-ops プロジェクトのページ行更新検証。
// LeaveRequest/OvertimeRequest の承認・却下時に Approver/ApprovedAt を自動スタンプします。

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.AttendanceOps.Hooks;

public class AttendanceOpsPageMutationValidator : IProjectPageMutationValidator
{
    public string ProjectName => "attendance-ops";

    public async Task<(bool ok, string? message)> ValidatePageUpdateAsync(
        string targetTable, int rowId, string field, string value,
        IDbConnection db, IDbTransaction? tx)
    {
        // LeaveRequest / OvertimeRequest の Status → approved/rejected 時に Approver/ApprovedAt を自動スタンプ
        var isApprovalTable =
            string.Equals(targetTable, "LeaveRequest",   StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetTable, "OvertimeRequest", StringComparison.OrdinalIgnoreCase);

        if (isApprovalTable &&
            string.Equals(field, "Status", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(value, "approved", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "rejected", StringComparison.OrdinalIgnoreCase)))
        {
            // ApprovedAt スタンプ（Approver は PageRowMutationService から渡せないため system 固定）
            var approvedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            // Safe: targetTable は PageRowMutationService 側で IdentifierRegex 検証済み
#pragma warning disable DCS001
            var stampSql = $"UPDATE \"{targetTable}\" SET \"ApprovedAt\" = @ApprovedAt WHERE \"Id\" = @RowId";
#pragma warning restore DCS001
            await db.ExecuteAsync(stampSql, new { ApprovedAt = approvedAt, RowId = rowId }, tx);
        }

        return (true, null);
    }

    public Task<(bool ok, string? message)> ValidatePageDeleteAsync(
        string targetTable, int rowId, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult<(bool, string?)>((true, null));
}
