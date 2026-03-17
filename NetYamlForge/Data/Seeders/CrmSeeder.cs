// ファイル概要: Salesforce-CRM プロジェクト向けの CRM 初期シードデータを投入するシーダーです。
// SlaPolicy（SLA 方針）と AutomationRule（自動化ルール）のデフォルトレコードを作成します。
// 既にレコードが存在する場合はスキップ（IGNORE INTO）します。

using System.Data;
using Dapper;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// CRM関連のシードデータ初期化。
/// SlaPolicy（SLA方針）と AutomationRule（自動化ルール）をシードします。
/// Salesforce-CRM プロジェクト専用。
/// </summary>
public class CrmSeeder
{
    /// <summary>
    /// CRM SLA方針と自動化ルールをシードします。
    /// </summary>
    public async Task EnsureCrmPoliciesAndRulesAsync(
        IDbConnection conn,
        string projectName,
        ILogger logger)
    {
        if (!string.Equals(projectName, "salesforce-crm", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // SLA ポリシーのシード
        await conn.ExecuteAsync(
            @"INSERT OR IGNORE INTO CrmSlaPolicy(PolicyName, TargetEntity, TargetStatus, DueHours, Priority, IsActive, CreatedAt, UpdatedAt)
              VALUES
              ('DelayedOrderHigh','Order','Delayed',24,'High',1,@Now,@Now),
              ('CancelledOrderCritical','Order','Cancelled',4,'Critical',1,@Now,@Now)",
            new { Now = now });

        // 自動化ルールのシード
        await conn.ExecuteAsync(
            @"INSERT OR IGNORE INTO CrmAutomationRule(RuleName, TriggerCondition, Action, IsActive, CreatedAt, UpdatedAt)
              VALUES
              ('OrderDelayedEscalation','Order.Status=Delayed','CreateCase+Notify',1,@Now,@Now),
              ('LeadQualificationNudge','LeadStage=New AND LastTouch>14d','CreateTaskActivity',1,@Now,@Now),
              ('HighFreightApproval','Order.Freight>1000','CreateApprovalRequest',1,@Now,@Now)",
            new { Now = now });

        logger.LogInformation("CRM SLA方針と自動化ルール を設定済み");
    }
}
