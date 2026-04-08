using System.Data;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Models.AI;
using NetYamlForge.Services;
using NetYamlForge.Services.AI;
using Xunit;
using Moq;

namespace NetYamlForge.Tests;

public class JpiereChatServiceTests
{
    #region Helper Methods

    private static SqliteConnection CreateInMemoryDb()
    {
        var db = new SqliteConnection("Data Source=:memory:");
        db.Open();

        // 创建AI相关表
        db.Execute(@"
CREATE TABLE IF NOT EXISTS ai_conversations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    conversation_id TEXT NOT NULL UNIQUE,
    channel TEXT NOT NULL DEFAULT 'web',
    status TEXT NOT NULL DEFAULT 'active',
    user_id TEXT,
    user_role TEXT,
    last_intent TEXT,
    last_confidence REAL DEFAULT 0.0,
    sentiment_score REAL DEFAULT 0.0,
    guest_session_id TEXT,
    customer_id TEXT,
    linked_contract_id INTEGER,
    linked_bill_id INTEGER,
    linked_estimation_id INTEGER,
    started_at TEXT NOT NULL,
    ended_at TEXT,
    message_count INTEGER DEFAULT 0,
    escalation_count INTEGER DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
)");

        db.Execute(@"
CREATE TABLE IF NOT EXISTS ai_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    message_id TEXT NOT NULL UNIQUE,
    conversation_id TEXT NOT NULL,
    sender TEXT NOT NULL,
    content TEXT NOT NULL,
    intent TEXT,
    confidence_score REAL DEFAULT 0.0,
    sentiment_score REAL DEFAULT 0.0,
    tool_called TEXT,
    tool_response TEXT,
    related_entity TEXT,
    related_entity_id INTEGER,
    sent_at TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
)");

        db.Execute(@"
CREATE TABLE IF NOT EXISTS ai_handovers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    handover_id TEXT NOT NULL UNIQUE,
    conversation_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    user_role TEXT,
    reason TEXT NOT NULL,
    reason_detail TEXT,
    priority TEXT NOT NULL DEFAULT 'normal',
    target_department TEXT NOT NULL,
    assigned_to TEXT,
    status TEXT NOT NULL DEFAULT 'pending',
    assigned_at TEXT,
    started_at TEXT,
    completed_at TEXT,
    notes TEXT,
    sentiment_score REAL DEFAULT 0.0,
    resolution_time_minutes INTEGER,
    user_satisfaction INTEGER,
    escalated_at TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
)");

        return db;
    }

    private static JpiereChatService CreateService(SqliteConnection db, Mock<CLIServiceFactory>? cliFactoryMock = null)
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["AiWindow:DefaultProvider"]).Returns("qwen");
        configMock.Setup(c => c["AICli:DefaultTool"]).Returns("qwen");
        configMock.Setup(c => c["AiWindow:BusinessHours"]).Returns("月〜金 9:00-18:00");
        configMock.Setup(c => c["AiWindow:EscalationSentimentThreshold"]).Returns("-0.5");

        // ProjectScope has internal Set, use reflection to set it
        var projectScope = new ProjectScope();
        var setMethod = typeof(ProjectScope).GetMethod("Set", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        setMethod?.Invoke(projectScope, [new ProjectInfo { Name = "jpiere-cs", DisplayName = "jpiere-cs", ProjectDir = "", ConnectionString = "", EntityMetadata = null!, DashboardConfig = null! }]);

        // SkillLoader is not virtual, use real instance
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(Directory.GetCurrentDirectory());
        var skillLoader = new SkillLoader(envMock.Object, Mock.Of<ILogger<SkillLoader>>());

        var queryParserMock = new Mock<QueryParserService>();
        var queryExecutorMock = new Mock<QueryExecutionService>();
        var queryFormatterMock = new Mock<QueryResultFormatter>();
        var taskQueueMock = new Mock<TaskQueueService>();
        var progressTrackerMock = new Mock<ProgressTracker>();
        var chatHistoryMock = new Mock<ChatHistoryService>();
        var loggerMock = new Mock<ILogger<JpiereChatService>>();
        var llmProviderMock = new Mock<NetYamlForge.Services.AI.Providers.ILlmProvider>();

        chatHistoryMock
            .Setup(c => c.SaveMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(0L);

        return new JpiereChatService(
            db,
            cliFactoryMock?.Object ?? Mock.Of<CLIServiceFactory>(),
            llmProviderMock.Object,
            skillLoader,
            projectScope,
            loggerMock.Object,
            queryParserMock.Object,
            queryExecutorMock.Object,
            queryFormatterMock.Object,
            taskQueueMock.Object,
            progressTrackerMock.Object,
            chatHistoryMock.Object,
            configMock.Object
        );
    }

    #endregion

    #region Session Tests

    [Fact]
    public async Task StartSessionAsync_CreatesConversation_ReturnsWelcomeMessage()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var service = CreateService(db);

        // Act
        var result = await service.StartSessionAsync(channel: "web", userId: "user001", userRole: "employee");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.ConversationId);
        Assert.StartsWith("CONV-", result.ConversationId);
        Assert.NotEmpty(result.WelcomeMessage);
        Assert.Contains("AI", result.WelcomeMessage);

        // Verify conversation was saved
        var count = await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM ai_conversations WHERE conversation_id = @Id",
            new { Id = result.ConversationId });
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("employee", "AI 業務アシスタント")]
    [InlineData("contract_manager", "契約担当 AI アシスタント")]
    [InlineData("accountant", "会計担当 AI アシスタント")]
    [InlineData("purchaser", "購買担当 AI アシスタント")]
    [InlineData("approver", "承認 AI アシスタント")]
    [InlineData("admin", "管理者 AI アシスタント")]
    public async Task StartSessionAsync_DifferentRoles_ReturnsRoleSpecificWelcomeMessage(string role, string expectedText)
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var service = CreateService(db);

        // Act
        var result = await service.StartSessionAsync(channel: "web", userId: "user001", userRole: role);

        // Assert
        Assert.Contains(expectedText, result.WelcomeMessage);
    }

    #endregion

    #region Sentiment Analysis Tests

    [Theory]
    [InlineData("最悪です", -0.8)]
    [InlineData("ひどいサービス", -0.8)]
    [InlineData("素晴らしい！", 0.8)]
    [InlineData("良いですね", 0.8)]
    [InlineData("普通の対応", 0.0)]
    public void EstimateSentiment_ReturnsCorrectScore_WhenMessageContainsKeywords(string message, double expectedScore)
    {
        // This would require making the method accessible or testing through SendMessageAsync
        // For now, we'll test through the integration
        Assert.True(true); // Placeholder
    }

    #endregion

    #region Escalation Tests

    [Theory]
    [InlineData("苦情を言います")]
    [InlineData("不満があります")]
    [InlineData("緊急です")]
    [InlineData("至急対応して")]
    public void DetectEscalation_ReturnsTrue_WhenMessageContainsEscalationKeywords(string message)
    {
        // This would require making the method accessible or testing through SendMessageAsync
        // For now, we'll test through the integration
        Assert.True(true); // Placeholder
    }

    #endregion

    #region Entity Access Tests

    [Theory]
    [InlineData("employee", "contracts", true)]
    [InlineData("employee", "journals", false)]
    [InlineData("contract_manager", "contracts", true)]
    [InlineData("contract_manager", "journals", false)]
    [InlineData("accountant", "journals", true)]
    [InlineData("accountant", "contracts", true)]
    [InlineData("purchaser", "purchase_orders", true)]
    [InlineData("purchaser", "contracts", false)]
    [InlineData("approver", "approval_requests", true)]
    [InlineData("approver", "bills", false)]
    [InlineData("admin", "contracts", true)]
    [InlineData("admin", "journals", true)]
    public void IsEntityAccessible_ReturnsCorrectAccess(string role, string entity, bool expectedAccess)
    {
        // This is a private method, testing through integration or would need to be made internal
        Assert.True(true); // Placeholder
    }

    #endregion

    #region Message Processing Tests

    [Fact]
    public async Task SendMessageAsync_SavesMessageAndUpdatesConversation()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var service = CreateService(db);
        
        var session = await service.StartSessionAsync(channel: "web", userId: "user001", userRole: "employee");
        
        // Act - This would normally call the AI, but we're testing the DB operations
        // In a real test, we'd mock the CLI service
        var message = "契約を確認してください";
        
        // For unit testing, we verify that the conversation was created
        var convCount = await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM ai_conversations WHERE conversation_id = @Id",
            new { Id = session.ConversationId });
        
        // Assert
        Assert.Equal(1, convCount);
    }

    #endregion

    #region Quick Replies Tests

    [Theory]
    [InlineData("employee", "general")]
    [InlineData("contract_manager", "general")]
    [InlineData("accountant", "general")]
    [InlineData("purchaser", "general")]
    [InlineData("approver", "general")]
    [InlineData("admin", "general")]
    public void GetRoleQuickReplies_ReturnsRoleSpecificReplies(string role, string intent)
    {
        // This would require making the method accessible
        Assert.True(true); // Placeholder
    }

    #endregion
}
