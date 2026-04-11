using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.AI.Infrastructure;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Controllers.Api;

/// <summary>
/// AI 対話詳細 API
/// </summary>
[ApiController]
[Route("api/ai/conversations")]
[Route("{project}/api/ai/conversations")]
[Produces("application/json")]
public class AIConversationDetailController : ControllerBase
{
    private readonly IAIDbConnectionFactory _dbConnectionFactory;
    private readonly IAIProjectContext _projectContext;
    private readonly ILogger<AIConversationDetailController> _logger;
    private const string DefaultProjectId = "auto-dealer-demo";

    public AIConversationDetailController(
        IAIDbConnectionFactory dbConnectionFactory,
        IAIProjectContext projectContext,
        ILogger<AIConversationDetailController> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _projectContext = projectContext;
        _logger = logger;
    }

    private string ResolveProject(string? project)
    {
        if (!string.IsNullOrWhiteSpace(project))
            return project;
        if (_projectContext.IsSet)
            return _projectContext.ProjectName;
        return DefaultProjectId;
    }

    /// <summary>
    /// 対話詳細取得
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ConversationDetailDto>> GetConversation(string id, [FromRoute] string? project)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var sql = @"
                SELECT
                    conversation_id,
                    customer_id,
                    channel,
                    status,
                    last_intent,
                    last_confidence,
                    sentiment_score,
                    context_data,
                    assigned_to_user_id,
                    started_at,
                    ended_at,
                    created_at,
                    updated_at
                FROM ai_conversations
                WHERE conversation_id = @ConversationId";

            var result = await db.QueryFirstOrDefaultAsync(sql, new { ConversationId = id });

            if (result == null)
                return NotFound();

            return Ok(new ConversationDetailDto
            {
                ConversationId = result.conversation_id,
                CustomerId = result.customer_id,
                Channel = result.channel,
                Status = result.status,
                LastIntent = result.last_intent,
                LastConfidence = result.last_confidence,
                SentimentScore = result.sentiment_score,
                ContextData = result.context_data,
                AssignedToUserId = result.assigned_to_user_id,
                StartedAt = result.started_at,
                EndedAt = result.ended_at,
                CreatedAt = result.created_at,
                UpdatedAt = result.updated_at
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対話詳細取得に失敗：{ConversationId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// メッセージ履歴取得
    /// </summary>
    [HttpGet("{id}/messages")]
    public async Task<ActionResult<List<MessageDetailDto>>> GetMessages(string id, [FromRoute] string? project)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var sql = @"
                SELECT
                    message_id,
                    conversation_id,
                    sender,
                    message_type,
                    content,
                    intent,
                    entities_json,
                    confidence_score,
                    sentiment_score,
                    timestamp
                FROM ai_messages
                WHERE conversation_id = @ConversationId
                ORDER BY timestamp ASC";

            var results = await db.QueryAsync(sql, new { ConversationId = id });

            return Ok(results.Select(r => new MessageDetailDto
            {
                MessageId = r.message_id,
                ConversationId = r.conversation_id,
                Sender = r.sender,
                MessageType = r.message_type,
                Content = r.content,
                Intent = r.intent,
                EntitiesJson = r.entities_json,
                ConfidenceScore = r.confidence_score,
                SentimentScore = r.sentiment_score,
                Timestamp = r.timestamp
            }).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージ履歴取得に失敗：{ConversationId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 対話をエクスポート
    /// </summary>
    [HttpGet("{id}/export")]
    public async Task<IActionResult> ExportConversation(string id, [FromRoute] string? project)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            // 対話情報取得
            var conversationSql = "SELECT * FROM ai_conversations WHERE conversation_id = @ConversationId";
            var conversation = await db.QueryFirstOrDefaultAsync(conversationSql, new { ConversationId = id });

            if (conversation == null)
                return NotFound();

            // メッセージ履歴取得
            var messagesSql = "SELECT * FROM ai_messages WHERE conversation_id = @ConversationId ORDER BY timestamp ASC";
            var messages = await db.QueryAsync(messagesSql, new { ConversationId = id });

            // CSV 生成
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Conversation ID,Channel,Status,Started At,Ended At");
            csv.AppendLine($"{conversation.conversation_id},{conversation.channel},{conversation.status},{conversation.started_at},{conversation.ended_at}");
            csv.AppendLine();
            csv.AppendLine("Timestamp,Sender,Content,Intent,Confidence");
            
            foreach (var msg in messages)
            {
                csv.AppendLine($"{msg.timestamp},{msg.sender},\"{msg.content?.Replace("\"", "\"\"")}\",{msg.intent},{msg.confidence_score}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"conversation_{id}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対話エクスポートに失敗：{ConversationId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// エスカレーション作成
    /// </summary>
    [HttpPost("{id}/handover")]
    public async Task<ActionResult> CreateHandover(
        string id,
        [FromBody] CreateHandoverRequest request,
        [FromRoute] string? project)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var handoverId = $"HO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N8}";

            var sql = @"
                INSERT INTO ai_handovers (
                    handover_id, conversation_id, reason, priority, status,
                    handover_notes, escalated_at, created_at
                ) VALUES (
                    @HandoverId, @ConversationId, @Reason, @Priority, 'pending',
                    @HandoverNotes, datetime('now'), datetime('now')
                )";

            await db.ExecuteAsync(sql, new
            {
                HandoverId = handoverId,
                ConversationId = id,
                Reason = request.Reason,
                Priority = request.Priority,
                HandoverNotes = request.Notes
            });

            // 対話ステータスを更新
            await db.ExecuteAsync(
                "UPDATE ai_conversations SET status = 'escalated', updated_at = datetime('now') WHERE conversation_id = @ConversationId",
                new { ConversationId = id });

            return Ok(new { handoverId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーション作成に失敗：{ConversationId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }
}

// DTO クラス

public class ConversationDetailDto
{
    public string ConversationId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? LastIntent { get; set; }
    public decimal? LastConfidence { get; set; }
    public decimal? SentimentScore { get; set; }
    public string? ContextData { get; set; }
    public string? AssignedToUserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MessageDetailDto
{
    public string MessageId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Intent { get; set; }
    public string? EntitiesJson { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public decimal? SentimentScore { get; set; }
    public DateTime Timestamp { get; set; }
}

public class CreateHandoverRequest
{
    public string Reason { get; set; } = "manual";
    public string Priority { get; set; } = "medium";
    public string? Notes { get; set; }
}
