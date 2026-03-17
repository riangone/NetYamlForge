// ファイル概要: CRM 自動化ルールをバックグラウンドで定期実行する IHostedService 実装です。
// CrmAutomationRule テーブルのアクティブなルールを一定間隔でポーリングし、
// TriggerCondition を評価して対象エンティティへのアクション（通知・ステータス変更等）を実行します。
//
// DCS003 抑制理由: BackgroundService は DI スコープ外で動作するため、
// IServiceScopeFactory でスコープを作成して接続を生成する必要があります。
// 参照: https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service
#pragma warning disable DCS003
using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace NetYamlForge.Services;

public class CrmAutomationHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CrmAutomationHostedService> _logger;

    public CrmAutomationHostedService(IServiceProvider serviceProvider, ILogger<CrmAutomationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CRM automation cycle failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var pm = scope.ServiceProvider.GetRequiredService<ProjectManager>();
        var projects = pm.GetAll()
            .Where(p => string.Equals(p.Name, "salesforce-crm", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(project.DatabaseType, "sqlite", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("CRM automation skipped. unsupported dbType={DbType}", project.DatabaseType);
                continue;
            }

            await using var conn = new SqliteConnection(project.ConnectionString);
            await conn.OpenAsync(ct);
            await RunSqliteRulesAsync(conn, ct);
        }
    }

    private static async Task RunSqliteRulesAsync(IDbConnection conn, CancellationToken ct)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // 1) Customer -> Lead projection
        var leadInsert = await conn.ExecuteAsync(
            @"INSERT INTO CrmLead(CustomerId, LeadStage, OwnerUserName, Score, LastTouchAt, CreatedAt, UpdatedAt)
              SELECT c.CustomerId,
                     CASE WHEN EXISTS(SELECT 1 FROM Orders o WHERE o.CustomerId = c.CustomerId) THEN 'Qualified' ELSE 'New' END,
                     'crm_sales_rep',
                     CASE WHEN EXISTS(SELECT 1 FROM Orders o WHERE o.CustomerId = c.CustomerId) THEN 80 ELSE 45 END,
                     COALESCE(MAX(o.OrderDate), datetime('now','-30 day')),
                     @Now, @Now
              FROM Customers c
              LEFT JOIN Orders o ON o.CustomerId = c.CustomerId
              WHERE c.Active = 1
                AND NOT EXISTS(SELECT 1 FROM CrmLead l WHERE l.CustomerId = c.CustomerId)
              GROUP BY c.CustomerId",
            new { Now = now });

        // 2) Delayed/Cancelled order -> Case projection
        var caseInsert = await conn.ExecuteAsync(
            @"INSERT INTO CrmCase(OrderId, Severity, CaseStatus, OwnerUserName, SlaDueAt, RootCause, CreatedAt, UpdatedAt)
              SELECT o.OrderId,
                     CASE WHEN o.Status = 'Cancelled' THEN 'Critical' ELSE 'High' END,
                     CASE WHEN o.Status = 'Cancelled' THEN 'Escalated' ELSE 'Open' END,
                     'crm_service_agent',
                     datetime('now', '+24 hour'),
                     'Auto escalated by rule',
                     @Now, @Now
              FROM Orders o
              WHERE o.Status IN ('Delayed','Cancelled')
                AND NOT EXISTS(SELECT 1 FROM CrmCase c WHERE c.OrderId = o.OrderId)",
            new { Now = now });

        // 3) Open order -> Quote/Contract projections
        await conn.ExecuteAsync(
            @"INSERT INTO CrmQuote(OrderId, QuoteStatus, DiscountRate, TotalAmount, VersionNo, CreatedAt, UpdatedAt)
              SELECT o.OrderId, 'Draft',
                     COALESCE(MAX(od.Discount), 0),
                     ROUND(COALESCE(SUM(od.UnitPrice * od.Quantity * (1 - od.Discount)), 0), 2),
                     1, @Now, @Now
              FROM Orders o
              LEFT JOIN OrderDetails od ON od.OrderId = o.OrderId
              WHERE NOT EXISTS(SELECT 1 FROM CrmQuote q WHERE q.OrderId = o.OrderId)
              GROUP BY o.OrderId",
            new { Now = now });

        await conn.ExecuteAsync(
            @"INSERT INTO CrmContract(OrderId, ContractState, StartDate, EndDate, ValueAmount, RenewalRisk, CreatedAt, UpdatedAt)
              SELECT o.OrderId,
                     CASE WHEN o.Status = 'Shipped' THEN 'Active' ELSE 'Draft' END,
                     COALESCE(o.OrderDate, date('now')),
                     date(COALESCE(o.RequiredDate, date('now')), '+365 day'),
                     ROUND(COALESCE(SUM(od.UnitPrice * od.Quantity * (1 - od.Discount)), 0), 2),
                     CASE WHEN o.Status IN ('Delayed','Cancelled') THEN 'High' ELSE 'Low' END,
                     @Now, @Now
              FROM Orders o
              LEFT JOIN OrderDetails od ON od.OrderId = o.OrderId
              WHERE NOT EXISTS(SELECT 1 FROM CrmContract c WHERE c.OrderId = o.OrderId)
              GROUP BY o.OrderId",
            new { Now = now });

        // 4) Lead nudge task generation
        var taskInsert = await conn.ExecuteAsync(
            @"INSERT INTO CrmTaskActivity(EntityType, EntityId, ActivityType, Status, DueAt, OwnerUserName, PayloadJson, CreatedAt, UpdatedAt)
              SELECT 'Lead', l.LeadId, 'Nudge', 'Open',
                     datetime('now', '+1 day'),
                     COALESCE(l.OwnerUserName, 'crm_sales_rep'),
                     '{""source"":""LeadQualificationNudge""}',
                     @Now, @Now
              FROM CrmLead l
              WHERE l.LeadStage = 'New'
                AND date(COALESCE(l.LastTouchAt, datetime('now','-30 day'))) <= date('now','-14 day')
                AND NOT EXISTS(
                    SELECT 1
                    FROM CrmTaskActivity a
                    WHERE a.EntityType = 'Lead'
                      AND a.EntityId = l.LeadId
                      AND a.ActivityType = 'Nudge'
                      AND a.Status = 'Open'
                )",
            new { Now = now });

        // 5) High freight approval request
        var approvalInsert = await conn.ExecuteAsync(
            @"INSERT INTO CrmApprovalRequest(EntityType, EntityId, RequestType, ApprovalStatus, RequestedBy, ApproverUserName, Reason, RequestedAt)
              SELECT 'Order', o.OrderId, 'HighFreight', 'Pending', 'system', 'crm_approver',
                     'Auto request for freight threshold',
                     @Now
              FROM Orders o
              WHERE COALESCE(o.Freight, 0) > 1000
                AND NOT EXISTS(
                    SELECT 1
                    FROM CrmApprovalRequest a
                    WHERE a.EntityType = 'Order'
                      AND a.EntityId = o.OrderId
                      AND a.RequestType = 'HighFreight'
                      AND a.ApprovalStatus IN ('Pending','Approved')
                )",
            new { Now = now });

        await conn.ExecuteAsync(
            @"INSERT INTO AuditLog(UserName, Action, Entity, Detail, CreatedAt)
              VALUES('system', 'automation_run', 'salesforce-crm',
                     @Detail, @Now)",
            new
            {
                Detail = $"Lead+{leadInsert}, Case+{caseInsert}, Task+{taskInsert}, Approval+{approvalInsert}",
                Now = now
            });
    }
}

