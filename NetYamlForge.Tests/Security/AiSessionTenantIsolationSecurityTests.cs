using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NetYamlForge.Exceptions;
using NetYamlForge.Services;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.SlotFilling;
using NetYamlForge.Services.AI.ToolValidation;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.BatchJob;
using Xunit;

namespace NetYamlForge.Tests.Security;

[Trait("Category", "Security")]
public class AiSessionTenantIsolationSecurityTests
{
    private static SlotFillingManager CreateSlotFillingManager(
        ISlotSessionStore sessionStore,
        IConversationFsmStore fsmStore,
        ProjectScope? projectScope = null)
    {
        var logger = NullLogger<SlotFillingManager>.Instance;
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var loaderMock = new Mock<IAiScenarioYamlLoader>();
        loaderMock.Setup(l => l.GetConfig(It.IsAny<string>())).Returns(new AiScenarioConfig());
        var resolver = new ScenarioResolver();
        var options = Microsoft.Extensions.Options.Options.Create(new SlotFillingOptions());

#pragma warning disable DCS003 // テスト用共有インメモリ SQLite 接続の初期化
        var sharedDbName = $"mem_{Guid.NewGuid():N}";
        var connStr = $"Data Source={sharedDbName};Mode=Memory;Cache=Shared";
        var masterConn = new SqliteConnection(connStr);
        masterConn.Open();
        masterConn.Execute("CREATE TABLE IF NOT EXISTS ai_conversations (conversation_id TEXT PRIMARY KEY, context_data TEXT, updated_at TEXT);");

        var dbFactoryMock = new Mock<IDbConnectionFactory>();
        dbFactoryMock.Setup(f => f.CreateConnection(It.IsAny<string>())).Returns(() =>
        {
            GC.KeepAlive(masterConn);
            return new SqliteConnection(connStr);
        });
#pragma warning restore DCS003

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IDbConnectionFactory))).Returns(dbFactoryMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(sf => sf.CreateScope()).Returns(scopeMock.Object);

        return new SlotFillingManager(
            logger,
            scopeFactoryMock.Object,
            loaderMock.Object,
            sessionStore,
            fsmStore,
            resolver,
            options,
            projectScope);
    }

    private static Mock<ToolCallValidator> CreateValidatorMock() =>
        new(NullLogger<ToolCallValidator>.Instance, new Mock<IAiScenarioYamlLoader>().Object);

    [Fact]
    public async Task SlotFillingManager_WhenProjectIdNotSpecifiedAndScopeNotSet_ThrowsTenantContextMissingException()
    {
        var sessionStore = new InMemorySlotSessionStore();
        var fsmStore = new InMemoryConversationFsmStore();
        var manager = CreateSlotFillingManager(sessionStore, fsmStore, projectScope: null);

        var ex = await Assert.ThrowsAsync<TenantContextMissingException>(() =>
            manager.GetSessionAsync("conv-123", "test_scenario", projectId: null));

        Assert.Contains("fail-closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SlotFillingManager_WhenProjectScopeSet_ResolvesCorrectTenant()
    {
        var sessionStore = new InMemorySlotSessionStore();
        var fsmStore = new InMemoryConversationFsmStore();
        var projectScope = new ProjectScope();
        projectScope.Set(new ProjectInfo { Name = "tenant-alpha" });

        var manager = CreateSlotFillingManager(sessionStore, fsmStore, projectScope);

        // Does not throw, resolves to tenant-alpha
        var session = await manager.GetSessionAsync("conv-123", "test_scenario", projectId: null);

        Assert.NotNull(session);
        Assert.Equal("conv-123", session.ConversationId);
        // Verify key in sessionStore is prefixed with tenant-alpha
        Assert.True(sessionStore.TryGet("tenant-alpha:conv-123:test_scenario", out _));
    }

    [Fact]
    public async Task SlotFillingManager_ConcurrentTenants_MaintainStrictIsolationInSessionStore()
    {
        var sessionStore = new InMemorySlotSessionStore();
        var fsmStore = new InMemoryConversationFsmStore();
        var manager = CreateSlotFillingManager(sessionStore, fsmStore, projectScope: null);

        // Two tenants using identical conversationId and scenario
        const string convId = "shared-conversation-id";
        const string scenario = "inquiry";

        var sessionTenantA = await manager.GetSessionAsync(convId, scenario, projectId: "tenant-a");
        var sessionTenantB = await manager.GetSessionAsync(convId, scenario, projectId: "tenant-b");

        sessionTenantA.Slots["customer_name"] = new SlotInfo { Name = "customer_name", Value = "Alice" };
        sessionTenantB.Slots["customer_name"] = new SlotInfo { Name = "customer_name", Value = "Bob" };

        var loadedA = await manager.GetSessionAsync(convId, scenario, projectId: "tenant-a");
        var loadedB = await manager.GetSessionAsync(convId, scenario, projectId: "tenant-b");

        Assert.Equal("Alice", loadedA.Slots["customer_name"].Value);
        Assert.Equal("Bob", loadedB.Slots["customer_name"].Value);
        Assert.NotSame(loadedA, loadedB);
    }

    [Fact]
    public async Task AiToolOrchestrator_WhenTenantContextMissing_FailsClosedAndWritesAuditLog()
    {
        var validatorMock = CreateValidatorMock();
        var slotManagerMock = new Mock<ISlotFillingManager>();
        var toolRegistryMock = new Mock<IToolRegistry>();
        var auditMock = new Mock<IAuditLogService>();
        var logger = NullLogger<AiToolOrchestrator>.Instance;

        var orchestrator = new AiToolOrchestrator(
            validatorMock.Object,
            slotManagerMock.Object,
            toolRegistryMock.Object,
            logger,
            projectScope: null,
            auditLogService: auditMock.Object);

        var toolCall = JsonNode.Parse("{\"function\": {\"name\": \"query_data\", \"arguments\": \"{}\"}}")!;

        var result = await orchestrator.ValidateAndExecuteToolAsync(
            toolCall,
            conversationId: "conv-1",
            projectId: null!);

        Assert.False(result.IsSuccess);
        Assert.Contains("无法确定租户上下文", result.ErrorMessage);

        auditMock.Verify(a => a.WriteAsync(
            "AI_SECURITY_VIOLATION",
            "AiTool",
            It.Is<string>(d => d.Contains("Tenant context missing")),
            null,
            null,
            null), Times.Once);
    }

    [Fact]
    public async Task AiToolOrchestrator_CrossTenantAccess_FailsClosedAndWritesAuditLog()
    {
        var validatorMock = CreateValidatorMock();
        var slotManagerMock = new Mock<ISlotFillingManager>();
        var toolRegistryMock = new Mock<IToolRegistry>();
        var auditMock = new Mock<IAuditLogService>();
        var logger = NullLogger<AiToolOrchestrator>.Instance;

        var projectScope = new ProjectScope();
        projectScope.Set(new ProjectInfo { Name = "tenant-a" });

        var orchestrator = new AiToolOrchestrator(
            validatorMock.Object,
            slotManagerMock.Object,
            toolRegistryMock.Object,
            logger,
            projectScope: projectScope,
            auditLogService: auditMock.Object);

        var toolCall = JsonNode.Parse("{\"function\": {\"name\": \"query_data\", \"arguments\": \"{}\"}}")!;

        // Active tenant is tenant-a, but request attempts tenant-b
        var result = await orchestrator.ValidateAndExecuteToolAsync(
            toolCall,
            conversationId: "conv-1",
            projectId: "tenant-b");

        Assert.False(result.IsSuccess);
        Assert.Contains("不允许跨租户调用 Tool", result.ErrorMessage);

        auditMock.Verify(a => a.WriteAsync(
            "AI_SECURITY_VIOLATION",
            "AiTool",
            It.Is<string>(d => d.Contains("Cross-tenant access violation")),
            null,
            null,
            null), Times.Once);
    }

    [Fact]
    public async Task AiToolOrchestrator_GetSessionStateAsync_WhenContextMissing_ThrowsTenantContextMissingException()
    {
        var validatorMock = CreateValidatorMock();
        var slotManagerMock = new Mock<ISlotFillingManager>();
        var toolRegistryMock = new Mock<IToolRegistry>();
        var auditMock = new Mock<IAuditLogService>();
        var logger = NullLogger<AiToolOrchestrator>.Instance;

        var orchestrator = new AiToolOrchestrator(
            validatorMock.Object,
            slotManagerMock.Object,
            toolRegistryMock.Object,
            logger,
            projectScope: null,
            auditLogService: auditMock.Object);

        await Assert.ThrowsAsync<TenantContextMissingException>(() =>
            orchestrator.GetSessionStateAsync("conv-1", projectId: null));

        auditMock.Verify(a => a.WriteAsync(
            "AI_SECURITY_VIOLATION",
            "AiSessionState",
            It.Is<string>(d => d.Contains("Tenant context missing")),
            null,
            null,
            null), Times.Once);
    }

    [Fact]
    public async Task AiToolOrchestrator_GetSessionStateAsync_CrossTenant_ThrowsInvalidOperationException()
    {
        var validatorMock = CreateValidatorMock();
        var slotManagerMock = new Mock<ISlotFillingManager>();
        var toolRegistryMock = new Mock<IToolRegistry>();
        var auditMock = new Mock<IAuditLogService>();
        var logger = NullLogger<AiToolOrchestrator>.Instance;

        var projectScope = new ProjectScope();
        projectScope.Set(new ProjectInfo { Name = "tenant-a" });

        var orchestrator = new AiToolOrchestrator(
            validatorMock.Object,
            slotManagerMock.Object,
            toolRegistryMock.Object,
            logger,
            projectScope: projectScope,
            auditLogService: auditMock.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.GetSessionStateAsync("conv-1", projectId: "tenant-b"));

        auditMock.Verify(a => a.WriteAsync(
            "AI_SECURITY_VIOLATION",
            "AiSessionState",
            It.Is<string>(d => d.Contains("Cross-tenant access violation")),
            null,
            null,
            null), Times.Once);
    }
}
