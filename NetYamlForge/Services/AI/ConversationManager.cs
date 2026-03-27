using System.Data;
using System.Text.Json;
using Dapper;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 対話管理サービス
/// </summary>
public class ConversationManager : IConversationManager
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<ConversationManager> _logger;
    private readonly string DefaultProjectId = "auto-dealer-demo";

    public ConversationManager(
        IDbConnectionFactory dbConnectionFactory,
        ILogger<ConversationManager> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Conversation> StartConversationAsync(StartConversationRequest request, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;
        var conversationId = GenerateConversationId();

        var conversation = new Conversation
        {
            ConversationId = conversationId,
            CustomerId = request.CustomerId,
            Channel = request.Channel,
            Status = "active",
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                INSERT INTO ai_conversations 
                (conversation_id, customer_id, channel, status, started_at, created_at, updated_at)
                VALUES 
                (@ConversationId, @CustomerId, @Channel, @Status, @StartedAt, @CreatedAt, @UpdatedAt)";

            await db.ExecuteAsync(sql, new
            {
                conversation.ConversationId,
                conversation.CustomerId,
                conversation.Channel,
                conversation.Status,
                conversation.StartedAt,
                conversation.CreatedAt,
                conversation.UpdatedAt
            });

            _logger.LogInformation("対話セッション開始：{ConversationId}, Channel: {Channel}", conversationId, request.Channel);

            return conversation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対話セッションの開始に失敗：{ConversationId}", conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Conversation?> GetConversationAsync(string conversationId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = "SELECT * FROM ai_conversations WHERE conversation_id = @ConversationId";
            var result = await db.QueryFirstOrDefaultAsync(sql, new { ConversationId = conversationId });

            if (result == null)
                return null;

            return MapToConversation(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対話セッションの取得に失敗：{ConversationId}", conversationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CloseConversationAsync(string conversationId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                UPDATE ai_conversations 
                SET status = @Status, ended_at = @EndedAt, updated_at = @UpdatedAt
                WHERE conversation_id = @ConversationId";

            var rows = await db.ExecuteAsync(sql, new
            {
                Status = "completed",
                EndedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ConversationId = conversationId
            });

            _logger.LogInformation("対話セッション終了：{ConversationId}", conversationId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対話セッションの終了に失敗：{ConversationId}", conversationId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> MarkConversationAsEscalatedAsync(string conversationId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                UPDATE ai_conversations 
                SET status = @Status, updated_at = @UpdatedAt
                WHERE conversation_id = @ConversationId";

            var rows = await db.ExecuteAsync(sql, new
            {
                Status = "escalated",
                UpdatedAt = DateTime.UtcNow,
                ConversationId = conversationId
            });

            _logger.LogInformation("対話セッションをエスカレーション状態に更新：{ConversationId}", conversationId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対話セッションのエスカレーション更新に失敗：{ConversationId}", conversationId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> LinkCustomerToConversationAsync(string conversationId, string customerId, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                UPDATE ai_conversations 
                SET customer_id = @CustomerId, updated_at = @UpdatedAt
                WHERE conversation_id = @ConversationId";

            var rows = await db.ExecuteAsync(sql, new
            {
                CustomerId = customerId,
                UpdatedAt = DateTime.UtcNow,
                ConversationId = conversationId
            });

            _logger.LogInformation("顧客を対話に紐付け：{ConversationId} -> {CustomerId}", conversationId, customerId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顧客の紐付けに失敗：{ConversationId}", conversationId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateContextAsync(string conversationId, Dictionary<string, object> context, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                UPDATE ai_conversations 
                SET context_data = @ContextData, updated_at = @UpdatedAt
                WHERE conversation_id = @ConversationId";

            var rows = await db.ExecuteAsync(sql, new
            {
                ContextData = JsonSerializer.Serialize(context),
                UpdatedAt = DateTime.UtcNow,
                ConversationId = conversationId
            });

            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "コンテキストの更新に失敗：{ConversationId}", conversationId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredSessionsAsync(int timeoutMinutes = 30, string? projectId = null)
    {
        var project = projectId ?? DefaultProjectId;
        var cutoffTime = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                UPDATE ai_conversations 
                SET status = @Status, ended_at = @EndedAt, updated_at = @UpdatedAt
                WHERE status = @CurrentStatus AND started_at < @CutoffTime";

            var rows = await db.ExecuteAsync(sql, new
            {
                Status = "abandoned",
                EndedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CurrentStatus = "active",
                CutoffTime = cutoffTime
            });

            if (rows > 0)
            {
                _logger.LogInformation("{Count} 件の有効期限切れセッションをクリーンアップ", rows);
            }

            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "セッションのクリーンアップに失敗");
            return 0;
        }
    }

    private static string GenerateConversationId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var guid = Guid.NewGuid().ToString("N")[..8];
        return $"CONV-{timestamp}-{guid}";
    }

    private static Conversation MapToConversation(dynamic data)
    {
        return new Conversation
        {
            ConversationId = data.conversation_id,
            CustomerId = data.customer_id,
            Channel = data.channel,
            Status = data.status,
            LastIntent = data.last_intent,
            LastConfidence = data.last_confidence,
            SentimentScore = data.sentiment_score,
            ContextData = data.context_data,
            AssignedToUserId = data.assigned_to_user_id,
            StartedAt = data.started_at,
            EndedAt = data.ended_at,
            CreatedAt = data.created_at,
            UpdatedAt = data.updated_at
        };
    }
}
