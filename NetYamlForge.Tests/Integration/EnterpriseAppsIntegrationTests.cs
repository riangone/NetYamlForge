using System.Data;
using System.Net;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NetYamlForge.Services.Connection;
using NetYamlForge.Services.Hooks;
using Xunit;

namespace NetYamlForge.Tests.Integration;

/// <summary>
/// 企业级孵化应用（approval-flow 与 analytics-dashboard）端到端业务与集成测试
/// </summary>
public class EnterpriseAppsIntegrationTests : IClassFixture<AgentStudioWebApplicationFactory>
{
    private readonly AgentStudioWebApplicationFactory _factory;

    public EnterpriseAppsIntegrationTests(AgentStudioWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApprovalFlow_HomePage_RendersSuccessfully_WithMetricsAndStream()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/approval-flow/Page/HomePage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawHtml = await response.Content.ReadAsStringAsync();
        var html = System.Net.WebUtility.HtmlDecode(rawHtml);
        Assert.True(html.Contains("企业表单审批流中心"), $"页面未包含预期标题。实际 HTML 前 500 字符:\n{html[..Math.Min(html.Length, 500)]}");
        Assert.Contains("待处理审批流", html);
        Assert.Contains("全量单据流水", html);
    }

    [Fact]
    public async Task ApprovalFlow_ActionHandlers_ExecuteWorkflowAndWriteAuditLog()
    {
        using var scope = _factory.Services.CreateScope();
        var connMgr = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        using var db = await connMgr.GetConnectionAsync("approval-flow");

        // 1. 创建一条待审批测试单据
        var insertId = await db.ExecuteScalarAsync<int>(@"
            INSERT INTO approval_request (title, category, applicant, department, amount, urgency, status, created_at, updated_at)
            VALUES ('集成测试服务器采购申请', '设备采购', 'Tester QA', '质量保证部', 8800.0, 'high', 'pending', datetime('now'), datetime('now'));
            SELECT last_insert_rowid();");

        Assert.True(insertId > 0);

        // 2. 模拟触发 approve_request_handler 审批通过
        var approveHandler = new NetYamlForge.Projects.ApprovalFlow.Hooks.ApproveRequestHandler();
        var ctxApprove = new CustomActionContext
        {
            Project = "approval-flow",
            Entity = "approval_request",
            Action = "approve_request",
            RecordId = insertId.ToString(),
            UserName = "Senior Manager Alice"
        };

        var resultApprove = await approveHandler.ExecuteAsync(ctxApprove, db, tx: null);
        Assert.True(resultApprove.Ok);

        // 3. 验证数据库状态更新与审计流水回写
        var rowApproved = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT status, approver FROM approval_request WHERE id = @Id",
            new { Id = insertId });
        Assert.Equal("approved", (string)rowApproved.status);
        Assert.Equal("Senior Manager Alice", (string)rowApproved.approver);

        var auditLog = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT action, operator FROM approval_log WHERE request_id = @ReqId ORDER BY id DESC LIMIT 1",
            new { ReqId = insertId });
        Assert.NotNull(auditLog);
        var auditDict = (IDictionary<string, object?>)auditLog;
        Assert.Equal("approve", auditDict["action"]?.ToString());
        Assert.Equal("Senior Manager Alice", auditDict["operator"]?.ToString());

        // 4. 测试驳回处理器 (reject_request_handler)
        var rejectHandler = new NetYamlForge.Projects.ApprovalFlow.Hooks.RejectRequestHandler();
        var ctxReject = new CustomActionContext
        {
            Project = "approval-flow",
            Entity = "approval_request",
            Action = "reject_request",
            RecordId = insertId.ToString(),
            UserName = "Director Bob"
        };
        var resultReject = await rejectHandler.ExecuteAsync(ctxReject, db, tx: null);
        Assert.True(resultReject.Ok);

        var rowRejected = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT status FROM approval_request WHERE id = @Id",
            new { Id = insertId });
        Assert.Equal("rejected", (string)rowRejected.status);
    }

    [Fact]
    public async Task AnalyticsDashboard_HomePage_RendersSuccessfully_WithKpiMetrics()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/analytics-dashboard/Page/HomePage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawHtml = await response.Content.ReadAsStringAsync();
        var html = System.Net.WebUtility.HtmlDecode(rawHtml);
        Assert.Contains("企业经营分析与决策看板", html);
        Assert.Contains("核心战略指标", html);
        Assert.Contains("主力业务线与增长引擎", html);
        Assert.Contains("经营明细与转化总览", html);
    }
}
