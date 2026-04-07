using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.ProjectHooks.JpiereCs;
using Xunit;

namespace NetYamlForge.Tests;

public class JpiereAIHooksTests
{
    #region Helper Methods

    private static SqliteConnection CreateInMemoryDb()
    {
        var db = new SqliteConnection("Data Source=:memory:");
        db.Open();

        db.Execute(@"
CREATE TABLE IF NOT EXISTS ai_conversations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    conversation_id TEXT NOT NULL UNIQUE,
    status TEXT NOT NULL DEFAULT 'active',
    sentiment_score REAL DEFAULT 0.0,
    started_at TEXT NOT NULL,
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
    sentiment_score REAL DEFAULT 0.0,
    sent_at TEXT NOT NULL,
    created_at TEXT NOT NULL
)");

        db.Execute(@"
CREATE TABLE IF NOT EXISTS ai_handovers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    handover_id TEXT NOT NULL UNIQUE,
    conversation_id TEXT NOT NULL,
    reason TEXT NOT NULL,
    priority TEXT NOT NULL DEFAULT 'normal',
    target_department TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    escalated_at TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
)");

        db.Execute(@"
CREATE TABLE IF NOT EXISTS todos (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    todo_id TEXT NOT NULL UNIQUE,
    title TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'OPEN',
    priority TEXT NOT NULL DEFAULT 'MEDIUM',
    assigned_to TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
)");

        return db;
    }

    #endregion

    #region ValidateAiConversationHook Tests

    [Fact]
    public async Task ValidateAiConversationHook_ValidData_DoesNotThrow()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new ValidateAiConversationHook();
        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = "CONV-20260407-001",
            ["sentiment_score"] = 0.5,
            ["last_confidence"] = 0.8
        };

        // Act & Assert - Should not throw
        await hook.ExecuteAsync("beforeCreate", data, db);
        Assert.True(true);
    }

    [Fact]
    public async Task ValidateAiConversationHook_InvalidConversationId_ThrowsException()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new ValidateAiConversationHook();
        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = "INVALID-ID"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => 
            hook.ExecuteAsync("beforeCreate", data, db));
        Assert.Contains("CONV-", ex.Message);
    }

    [Theory]
    [InlineData(-1.5)]
    [InlineData(1.5)]
    public async Task ValidateAiConversationHook_InvalidSentimentScore_ThrowsException(double sentimentScore)
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new ValidateAiConversationHook();
        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = "CONV-20260407-001",
            ["sentiment_score"] = sentimentScore
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => 
            hook.ExecuteAsync("beforeCreate", data, db));
        Assert.Contains("感情スコア", ex.Message);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public async Task ValidateAiConversationHook_InvalidConfidence_ThrowsException(double confidence)
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new ValidateAiConversationHook();
        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = "CONV-20260407-001",
            ["last_confidence"] = confidence
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => 
            hook.ExecuteAsync("beforeCreate", data, db));
        Assert.Contains("信頼度", ex.Message);
    }

    #endregion

    #region SetConversationTimestampsHook Tests

    [Fact]
    public async Task SetConversationTimestampsHook_BeforeCreate_SetsTimestamps()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new SetConversationTimestampsHook();
        var data = new Dictionary<string, object?>();

        // Act
        await hook.ExecuteAsync("beforeCreate", data, db);

        // Assert
        Assert.NotNull(data["started_at"]);
        Assert.NotNull(data["created_at"]);
        Assert.NotNull(data["updated_at"]);
        Assert.Equal("active", data["status"]);
        Assert.Equal(0, data["message_count"]);
        Assert.Equal(0, data["escalation_count"]);
    }

    [Fact]
    public async Task SetConversationTimestampsHook_BeforeUpdate_OnlyUpdatesUpdatedAt()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new SetConversationTimestampsHook();
        var data = new Dictionary<string, object?>();

        // Act
        await hook.ExecuteAsync("beforeUpdate", data, db);

        // Assert
        Assert.NotNull(data["updated_at"]);
        Assert.False(data.ContainsKey("started_at"));
        Assert.False(data.ContainsKey("created_at"));
    }

    #endregion

    #region AutoEscalationHook Tests

    [Fact]
    public async Task AutoEscalationHook_LowSentiment_UpdatesConversationStatus()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new AutoEscalationHook();
        
        // Create a conversation
        var convId = "CONV-20260407-001";
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync(@"
            INSERT INTO ai_conversations (conversation_id, status, started_at, created_at, updated_at)
            VALUES (@Id, 'active', @Now, @Now, @Now)",
            new { Id = convId, Now = now });

        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = convId,
            ["sentiment_score"] = -0.8
        };

        // Act
        await hook.ExecuteAsync("afterCreate", data, db);

        // Assert
        var status = await db.QuerySingleAsync<string>(
            "SELECT status FROM ai_conversations WHERE conversation_id = @Id",
            new { Id = convId });
        Assert.Equal("escalated", status);
    }

    [Fact]
    public async Task AutoEscalationHook_NormalSentiment_DoesNotUpdate()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new AutoEscalationHook();
        
        var convId = "CONV-20260407-001";
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync(@"
            INSERT INTO ai_conversations (conversation_id, status, started_at, created_at, updated_at)
            VALUES (@Id, 'active', @Now, @Now, @Now)",
            new { Id = convId, Now = now });

        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = convId,
            ["sentiment_score"] = 0.3
        };

        // Act
        await hook.ExecuteAsync("afterCreate", data, db);

        // Assert
        var status = await db.QuerySingleAsync<string>(
            "SELECT status FROM ai_conversations WHERE conversation_id = @Id",
            new { Id = convId });
        Assert.Equal("active", status);
    }

    #endregion

    #region AutoCreateTodoFromAiHook Tests

    [Fact]
    public async Task AutoCreateTodoFromAiHook_ContractExpiryIntent_CreatesTodo()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new AutoCreateTodoFromAiHook();
        
        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = "CONV-20260407-001",
            ["user_id"] = "user001",
            ["user_role"] = "employee",
            ["last_intent"] = "contract_expiry_alert"
        };

        // Act
        await hook.ExecuteAsync("afterCreate", data, db);

        // Assert
        var todoCount = await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM todos WHERE assigned_to = @UserId",
            new { UserId = "user001" });
        Assert.Equal(1, todoCount);
    }

    [Fact]
    public async Task AutoCreateTodoFromAiHook_UnbilledContractIntent_CreatesTodo()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new AutoCreateTodoFromAiHook();
        
        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = "CONV-20260407-001",
            ["user_id"] = "user001",
            ["user_role"] = "contract_manager",
            ["last_intent"] = "unbilled_contract"
        };

        // Act
        await hook.ExecuteAsync("afterCreate", data, db);

        // Assert
        var todoCount = await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM todos WHERE assigned_to = @UserId",
            new { UserId = "user001" });
        Assert.Equal(1, todoCount);
    }

    [Fact]
    public async Task AutoCreateTodoFromAiHook_GeneralIntent_DoesNotCreateTodo()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new AutoCreateTodoFromAiHook();
        
        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = "CONV-20260407-001",
            ["user_id"] = "user001",
            ["user_role"] = "employee",
            ["last_intent"] = "general"
        };

        // Act
        await hook.ExecuteAsync("afterCreate", data, db);

        // Assert
        var todoCount = await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM todos");
        Assert.Equal(0, todoCount);
    }

    #endregion

    #region UpdateSentimentTrendHook Tests

    [Fact]
    public async Task UpdateSentimentTrendHook_AfterUpdate_CalculatesAverageSentiment()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var hook = new UpdateSentimentTrendHook();
        
        var convId = "CONV-20260407-001";
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        
        // Create conversation
        await db.ExecuteAsync(@"
            INSERT INTO ai_conversations (conversation_id, sentiment_score, started_at, created_at, updated_at)
            VALUES (@Id, 0.0, @Now, @Now, @Now)",
            new { Id = convId, Now = now });

        // Create messages with different sentiments
        await db.ExecuteAsync(@"
            INSERT INTO ai_messages (message_id, conversation_id, sender, content, sentiment_score, sent_at, created_at)
            VALUES 
                ('MSG-001', @ConvId, 'user', 'メッセージ1', 0.5, @Now, @Now),
                ('MSG-002', @ConvId, 'ai', 'メッセージ2', -0.3, @Now, @Now),
                ('MSG-003', @ConvId, 'user', 'メッセージ3', 0.8, @Now, @Now)",
            new { ConvId = convId, Now = now });

        var data = new Dictionary<string, object?>
        {
            ["conversation_id"] = convId
        };

        // Act
        await hook.ExecuteAsync("afterUpdate", data, db);

        // Assert
        var avgSentiment = await db.QuerySingleAsync<double>(
            "SELECT sentiment_score FROM ai_conversations WHERE conversation_id = @Id",
            new { Id = convId });
        // Average of 0.5, -0.3, 0.8 = 1.0 / 3 = 0.333...
        Assert.Equal(0.333, avgSentiment, 2);
    }

    #endregion
}
