using System.Data;
using Dapper;
using NetYamlForge.Models.AI;
using NetYamlForge.Services;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Services.AI;

/// <summary>
/// オペレーター向けチャット管理サービス（プロジェクト共通）
/// </summary>
public class OperatorChatService : IOperatorChatService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<OperatorChatService> _logger;
    private const string DefaultProjectId = "auto-dealer-demo";

    public OperatorChatService(
        IDbConnectionFactory dbConnectionFactory,
        ProjectScope projectScope,
        ILogger<OperatorChatService> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _projectScope = projectScope;
        _logger = logger;
    }

    private string ResolveProject(string? projectId = null)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
            return projectId;
        if (_projectScope.IsSet)
            return _projectScope.Current.Name;
        return DefaultProjectId;
    }

    private IDbConnection OpenConnection(string? projectId = null)
    {
        var db = _dbConnectionFactory.CreateConnection(ResolveProject(projectId));
        db.Open();
        return db;
    }

    public async Task<IEnumerable<OperatorHandoverDetail>> GetPendingHandoversAsync()
    {
        using var db = OpenConnection();
        return await db.QueryAsync<OperatorHandoverDetail>(@"
SELECT h.handover_id AS HandoverId, h.conversation_id AS ConversationId,
       h.reason AS Reason, h.priority AS Priority, h.status AS Status,
       h.handover_notes AS Notes, h.escalated_at AS EscalatedAt,
       h.assigned_at AS AssignedAt, h.assigned_to_user_id AS AssignedToUserId,
       c.customer_id AS CustomerId, cu.name AS CustomerName,
       cu.tier_level AS CustomerTier, cu.phone AS CustomerPhone, cu.email AS CustomerEmail
FROM ai_handovers h
INNER JOIN ai_conversations c ON h.conversation_id = c.conversation_id
LEFT JOIN customers cu ON c.customer_id = cu.customer_id
WHERE h.status IN ('pending', 'assigned', 'in_progress')
ORDER BY CASE h.priority WHEN 'urgent' THEN 1 WHEN 'high' THEN 2 WHEN 'medium' THEN 3 ELSE 4 END,
         h.escalated_at ASC");
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(string conversationId)
    {
        using var db = OpenConnection();
        return await db.QueryAsync<ChatMessage>(@"
SELECT message_id AS MessageId, sender AS Sender, content AS Content, timestamp AS Timestamp, intent AS Intent
FROM ai_messages
WHERE conversation_id = @Id
ORDER BY timestamp ASC",
            new { Id = conversationId });
    }

    public async Task<OperatorHandoverDetail?> GetHandoverByConversationAsync(string conversationId)
    {
        using var db = OpenConnection();
        return await db.QueryFirstOrDefaultAsync<OperatorHandoverDetail>(@"
SELECT h.handover_id AS HandoverId, h.conversation_id AS ConversationId,
       h.reason AS Reason, h.priority AS Priority, h.status AS Status,
       h.handover_notes AS Notes, h.escalated_at AS EscalatedAt,
       h.assigned_at AS AssignedAt, h.assigned_to_user_id AS AssignedToUserId,
       c.customer_id AS CustomerId, cu.name AS CustomerName,
       cu.tier_level AS CustomerTier, cu.phone AS CustomerPhone, cu.email AS CustomerEmail
FROM ai_handovers h
INNER JOIN ai_conversations c ON h.conversation_id = c.conversation_id
LEFT JOIN customers cu ON c.customer_id = cu.customer_id
WHERE h.conversation_id = @CId
ORDER BY h.escalated_at DESC
LIMIT 1",
            new { CId = conversationId });
    }

    public async Task OperatorReplyAsync(string conversationId, string operatorId, string message)
    {
        using var db = OpenConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await SaveMessageAsync(db, $"MSG-{Guid.NewGuid():N}"[..32], conversationId, "agent", message, now);

        await db.ExecuteAsync(@"
UPDATE ai_handovers
SET status = 'in_progress', assigned_to_user_id = @OpId, assigned_at = COALESCE(assigned_at, @Now), updated_at = @Now
WHERE conversation_id = @CId AND status IN ('pending','assigned')",
            new { OpId = operatorId, Now = now, CId = conversationId });
    }

    public async Task<bool> AcceptHandoverAsync(string handoverId, string operatorId)
    {
        using var db = OpenConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await db.ExecuteAsync(@"
UPDATE ai_handovers SET status = 'assigned', assigned_to_user_id = @OpId, assigned_at = @Now
WHERE handover_id = @HId AND status = 'pending'",
            new { OpId = operatorId, Now = now, HId = handoverId });
        return affected > 0;
    }

    public async Task ResolveHandoverAsync(string conversationId, string operatorId, string? resolutionNotes)
    {
        using var db = OpenConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await db.ExecuteAsync(@"
UPDATE ai_handovers
SET status = 'resolved', resolved_at = @Now, resolution_notes = @Notes
WHERE conversation_id = @CId AND assigned_to_user_id = @OpId",
            new { Now = now, Notes = resolutionNotes ?? "", CId = conversationId, OpId = operatorId });

        await db.ExecuteAsync(@"
UPDATE ai_conversations SET status = 'completed', ended_at = @Now, updated_at = @Now WHERE conversation_id = @CId",
            new { Now = now, CId = conversationId });

        await SaveMessageAsync(db, $"MSG-{Guid.NewGuid():N}"[..32], conversationId, "ai",
            "対応が完了しました。ご利用ありがとうございました。またのご来店をお待ちしております。🚗", now);
    }

    private static Task SaveMessageAsync(
        IDbConnection db,
        string messageId,
        string conversationId,
        string sender,
        string content,
        string timestamp,
        string? intent = null,
        double confidence = 0.9,
        double sentiment = 0)
    {
        return db.ExecuteAsync(@"
INSERT INTO ai_messages
  (message_id, conversation_id, sender, message_type, content, intent, confidence_score, sentiment_score, timestamp)
VALUES
  (@MessageId, @ConversationId, @Sender, 'text', @Content, @Intent, @Confidence, @Sentiment, @Timestamp)",
            new
            {
                MessageId = messageId,
                ConversationId = conversationId,
                Sender = sender,
                Content = content,
                Intent = intent ?? "general",
                Confidence = confidence,
                Sentiment = sentiment,
                Timestamp = timestamp
            });
    }
}
