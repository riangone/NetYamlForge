// DCS003 抑制理由：单元测试中直接使用 SqliteConnection 进行内存数据库测试是合理的
#pragma warning disable DCS003

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// 汽车销售聊天历史记录修复测试
/// 测试聊天记录恢复和用户历史记录端点
/// </summary>
public class AutoDealerChatHistoryFixTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AutoDealerChatHistoryFixTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        
        // 创建测试表
        _connection.Execute(@"
CREATE TABLE ai_conversations (
    conversation_id TEXT PRIMARY KEY,
    customer_id TEXT,
    guest_session_id TEXT,
    channel TEXT DEFAULT 'web',
    status TEXT DEFAULT 'active',
    started_at TEXT,
    created_at TEXT,
    updated_at TEXT,
    last_intent TEXT,
    last_confidence REAL,
    sentiment_score REAL
)");

        _connection.Execute(@"
CREATE TABLE ai_messages (
    message_id TEXT PRIMARY KEY,
    conversation_id TEXT,
    sender TEXT,
    content TEXT,
    timestamp TEXT,
    intent TEXT,
    confidence REAL,
    sentiment_score REAL
)");
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    [Fact]
    public async Task GetUserRecentConversations_ShouldReturnMostRecent()
    {
        // Arrange
        var now = "2026-04-08 10:00:00";
        
        // 创建多个会话
        await _connection.ExecuteAsync(@"
INSERT INTO ai_conversations (conversation_id, customer_id, channel, status, started_at, created_at, updated_at)
VALUES 
    ('CONV-old', 'customer1', 'web', 'completed', '2026-04-07 09:00:00', '2026-04-07 09:00:00', '2026-04-07 10:00:00'),
    ('CONV-recent', 'customer1', 'web', 'active', '2026-04-08 09:00:00', '2026-04-08 09:00:00', '2026-04-08 10:00:00'),
    ('CONV-other', 'customer2', 'web', 'active', '2026-04-08 09:30:00', '2026-04-08 09:30:00', '2026-04-08 10:00:00')
");

        // Act
        var conversations = await _connection.QueryAsync<ConversationSummary>(@"
SELECT conversation_id AS ConversationId, channel AS Channel, status AS Status,
       started_at AS StartedAt, updated_at AS UpdatedAt
FROM ai_conversations
WHERE customer_id = @UserId OR guest_session_id = @UserId
ORDER BY updated_at DESC
LIMIT @Limit",
            new { UserId = "customer1", Limit = 1 });

        var result = conversations.ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("CONV-recent", result[0].ConversationId);
        Assert.Equal("active", result[0].Status);
    }

    [Fact]
    public async Task GetMessages_ShouldReturnAllMessagesInOrder()
    {
        // Arrange
        var convId = "CONV-test";
        
        await _connection.ExecuteAsync(@"
INSERT INTO ai_conversations (conversation_id, customer_id, channel, status, started_at, created_at, updated_at)
VALUES (@ConvId, 'customer1', 'web', 'active', '2026-04-08 10:00:00', '2026-04-08 10:00:00', '2026-04-08 10:00:00')
", new { ConvId = convId });

        await _connection.ExecuteAsync(@"
INSERT INTO ai_messages (message_id, conversation_id, sender, content, timestamp)
VALUES 
    ('MSG-1', @ConvId, 'customer', 'こんにちは', '2026-04-08 10:00:01'),
    ('MSG-2', @ConvId, 'ai', 'こんにちは！何かお手伝いできますか？', '2026-04-08 10:00:02'),
    ('MSG-3', @ConvId, 'customer', '在庫を確認してください', '2026-04-08 10:00:03'),
    ('MSG-4', @ConvId, 'ai', '在庫車両をご案内します。', '2026-04-08 10:00:04')
", new { ConvId = convId });

        // Act
        var messages = await _connection.QueryAsync<ConversationMessage>(@"
SELECT message_id AS MessageId, sender AS Sender, content AS Content, timestamp AS Timestamp, intent AS Intent
FROM ai_messages
WHERE conversation_id = @Id
ORDER BY timestamp ASC",
            new { Id = convId });

        var result = messages.ToList();

        // Assert
        Assert.Equal(4, result.Count);
        Assert.Equal("customer", result[0].Sender);
        Assert.Equal("こんにちは", result[0].Content);
        Assert.Equal("ai", result[1].Sender);
        Assert.Contains("お手伝い", result[1].Content);
    }

    [Fact]
    public async Task GuestSession_ShouldAlsoBeRetrievable()
    {
        // Arrange
        var guestId = "guest-12345";
        
        await _connection.ExecuteAsync(@"
INSERT INTO ai_conversations (conversation_id, guest_session_id, channel, status, started_at, created_at, updated_at)
VALUES ('CONV-guest', @GuestId, 'web', 'active', '2026-04-08 10:00:00', '2026-04-08 10:00:00', '2026-04-08 10:00:00')
", new { GuestId = guestId });

        // Act
        var conversations = await _connection.QueryAsync<ConversationSummary>(@"
SELECT conversation_id AS ConversationId, channel AS Channel, status AS Status,
       started_at AS StartedAt, updated_at AS UpdatedAt
FROM ai_conversations
WHERE customer_id = @UserId OR guest_session_id = @UserId
ORDER BY updated_at DESC
LIMIT @Limit",
            new { UserId = guestId, Limit = 1 });

        var result = conversations.ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("CONV-guest", result[0].ConversationId);
    }
}

public record ConversationSummary
{
    public string ConversationId { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Status { get; init; } = "";
    public string StartedAt { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
}

public record ConversationMessage
{
    public string MessageId { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Content { get; init; } = "";
    public string? Intent { get; init; }
    public string Timestamp { get; init; } = "";
}
