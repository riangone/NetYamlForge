// ファイル概要: RBAC（ロールベースアクセス制御）の初期シードデータを投入するシーダーです。
// AdminOps / SalesRep 等の共通ロールを AppUserRole に、
// Salesforce-CRM プロジェクト向けの細粒度権限を AppRolePermission に登録します。
// 既にロールが存在する場合はスキップ（IGNORE INTO）します。

using System.Data;
using Dapper;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// RBAC（ロールベースアクセス制御）の初期シード。
/// 共通ロール（AdminOps、SalesRep等）と権限（AppRolePermission）を作成します。
/// Salesforce-CRM用の詳細権限設定も含みます。
/// </summary>
public class RbacSeeder
{
    /// <summary>
    /// 共通ロール定義（AppUserRole）をシードします。
    /// </summary>
    public async Task EnsureRbacRolesAsync(IDbConnection conn, ILogger logger)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            @"INSERT OR IGNORE INTO AppUserRole(UserName, RoleName, CreatedAt) VALUES(@UserName, @RoleName, @CreatedAt)",
            new[]
            {
                new { UserName = "admin", RoleName = "AdminOps", CreatedAt = now },
                new { UserName = "crm_admin_ops", RoleName = "AdminOps", CreatedAt = now },
                new { UserName = "crm_approver", RoleName = "AdminOps", CreatedAt = now },
                new { UserName = "crm_sales_rep", RoleName = "SalesRep", CreatedAt = now },
                new { UserName = "crm_service_agent", RoleName = "ServiceAgent", CreatedAt = now },
                new { UserName = "crm_marketing_user", RoleName = "Marketing", CreatedAt = now },
                new { UserName = "crm_readonly_user", RoleName = "ReadOnly", CreatedAt = now }
            });

        logger.LogInformation("RBAC ロール を設定済み");
    }

    /// <summary>
    /// Salesforce-CRM プロジェクト用の詳細権限設定をシードします。
    /// </summary>
    public async Task EnsureSalesforcePermissionsAsync(
        IDbConnection conn,
        string projectName,
        ILogger logger)
    {
        if (!string.Equals(projectName, "salesforce-crm", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        async Task UpsertPermission(string role, string resourceType, string resourceName, int canRead, int canWrite)
        {
            await conn.ExecuteAsync(
                @"INSERT INTO AppRolePermission(ProjectName, RoleName, ResourceType, ResourceName, CanRead, CanWrite, CreatedAt, UpdatedAt)
                  VALUES(@ProjectName, @RoleName, @ResourceType, @ResourceName, @CanRead, @CanWrite, @Now, @Now)
                  ON CONFLICT(ProjectName, RoleName, ResourceType, ResourceName)
                  DO UPDATE SET CanRead = excluded.CanRead, CanWrite = excluded.CanWrite, UpdatedAt = excluded.UpdatedAt",
                new
                {
                    ProjectName = projectName,
                    RoleName = role,
                    ResourceType = resourceType,
                    ResourceName = resourceName,
                    CanRead = canRead,
                    CanWrite = canWrite,
                    Now = now
                });
        }

        // デフォルト権限
        await UpsertPermission("AdminOps", "page", "*", 1, 1);
        await UpsertPermission("ReadOnly", "page", "*", 1, 0);
        await UpsertPermission("SalesRep", "page", "*", 0, 0);
        await UpsertPermission("ServiceAgent", "page", "*", 0, 0);
        await UpsertPermission("Marketing", "page", "*", 0, 0);

        // SalesRep 権限
        foreach (var page in new[]
        {
            "OperationWorkbench", "LeadInbox", "LeadDetail360", "LeadCommandCenter",
            "OpportunityWorkspace", "OpportunityDetail", "PipelineBoard",
            "QuoteBuilder", "OrderManagement", "ActivityConsole", "ForecastConsole",
            "AuditTrail", "ExecutiveCockpit"
        })
        {
            await UpsertPermission("SalesRep", "page", page, 1, 1);
        }

        // ServiceAgent 権限
        foreach (var page in new[]
        {
            "OperationWorkbench", "CaseQueue", "CaseDetail", "ServiceDesk360",
            "SlaMonitor", "OmniChannelConsole", "KnowledgeBase", "AuditTrail"
        })
        {
            await UpsertPermission("ServiceAgent", "page", page, 1, 1);
        }

        // Marketing 権限
        foreach (var page in new[]
        {
            "OperationWorkbench", "CampaignPlanner", "CampaignMember",
            "AttributionDashboard", "LeadInbox", "AuditTrail"
        })
        {
            await UpsertPermission("Marketing", "page", page, 1, 1);
        }

        logger.LogInformation("Salesforce-CRM 権限を設定済み");
    }
}
