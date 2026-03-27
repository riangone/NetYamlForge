using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Services.AI;

/// <summary>
/// エスカレーション管理サービス
/// </summary>
public class HandoverManager : IHandoverManager
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IConversationManager _conversationManager;
    private readonly AiWindowConfig _config;
    private readonly ILogger<HandoverManager> _logger;
    private const string DefaultProjectId = "auto-dealer-demo";

    public HandoverManager(
        IDbConnectionFactory dbConnectionFactory,
        IConversationManager conversationManager,
        IOptions<AiWindowConfig> configOptions,
        ILogger<HandoverManager> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _conversationManager = conversationManager;
        _logger = logger;
        _config = configOptions.Value;
    }

    /// <inheritdoc />
    public Task<HandoverEvaluation> EvaluateHandoverNeedAsync(IntentResult intentResult, ConversationContext? context = null, string? projectId = null)
    {
        var evaluation = new HandoverEvaluation
        {
            NeedsHandover = false,
            Priority = "medium"
        };

        // 1. 置信度による判定
        if (_config.Handover.AutoEnabled && intentResult.Confidence < _config.Handover.ConfidenceThreshold)
        {
            evaluation.NeedsHandover = true;
            evaluation.Reason = "ai_unable";
            evaluation.EvaluationScore = intentResult.Confidence;
        }

        // 2. 感情による判定
        if (intentResult.SentimentScore < _config.Handover.NegativeSentimentThreshold)
        {
            evaluation.NeedsHandover = true;
            evaluation.Reason = "negative_sentiment";
            evaluation.Priority = "high";
            evaluation.EvaluationScore = Math.Max(evaluation.EvaluationScore, Math.Abs(intentResult.SentimentScore));
        }

        // 3. 意図による判定
        if (intentResult.Intent == "complaint" || intentResult.Intent == "human_agent")
        {
            evaluation.NeedsHandover = true;
            evaluation.Reason = intentResult.Intent == "complaint" ? "complaint" : "customer_request";
            evaluation.Priority = "high";
            evaluation.TargetDepartment = intentResult.Intent == "complaint" ? "quality" : "general";
        }

        // 4. VIP 顧客による判定
        if (_config.Handover.VipAutoHandover && context != null)
        {
            if (context.Entities.TryGetValue("tier_level", out var tier) && tier == "vip")
            {
                evaluation.NeedsHandover = true;
                evaluation.Reason = "vip_customer";
                evaluation.Priority = "urgent";
            }
        }

        return Task.FromResult(evaluation);
    }

    /// <inheritdoc />
    public async Task<HandoverResult> CreateHandoverAsync(HandoverRequest request, string? projectId = null)
    {
        var handoverId = GenerateHandoverId();

        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            // 対話をエスカレーション状態に更新
            await _conversationManager.MarkConversationAsEscalatedAsync(request.ConversationId);

            // エスカレーションレコードを作成
            var sql = @"
                INSERT INTO ai_handovers 
                (handover_id, conversation_id, reason, priority, target_department, 
                 assigned_to_user_id, status, handover_notes, escalated_at, created_at)
                VALUES 
                (@HandoverId, @ConversationId, @Reason, @Priority, @TargetDepartment,
                 @AssignedToUserId, @Status, @HandoverNotes, @EscalatedAt, @CreatedAt)";

            await db.ExecuteAsync(sql, new
            {
                HandoverId = handoverId,
                ConversationId = request.ConversationId,
                Reason = request.Reason,
                Priority = request.Priority,
                TargetDepartment = request.TargetDepartment,
                AssignedToUserId = request.AssignedToUserId,
                Status = "pending",
                HandoverNotes = request.HandoverNotes,
                EscalatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });

            _logger.LogInformation(
                "エスカレーション作成：{HandoverId}, 理由：{Reason}, 優先度：{Priority}",
                handoverId, request.Reason, request.Priority);

            return new HandoverResult
            {
                HandoverId = handoverId,
                Success = true,
                EstimatedWaitMinutes = GetEstimatedWaitTime(request.Priority)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーションの作成に失敗：{ConversationId}", request.ConversationId);
            return new HandoverResult
            {
                Success = false,
                ErrorMessage = "エスカレーションの作成に失敗しました"
            };
        }
    }

    /// <inheritdoc />
    public async Task<HandoverInfo?> GetHandoverAsync(string handoverId, string? projectId = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = "SELECT * FROM ai_handovers WHERE handover_id = @HandoverId";
            var result = await db.QueryFirstOrDefaultAsync(sql, new { HandoverId = handoverId });

            if (result == null)
                return null;

            return MapToHandoverInfo(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーションの取得に失敗：{HandoverId}", handoverId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AssignOperatorAsync(string handoverId, string operatorId, string? projectId = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                UPDATE ai_handovers 
                SET assigned_to_user_id = @OperatorId, status = @Status, assigned_at = @AssignedAt
                WHERE handover_id = @HandoverId";

            var rows = await db.ExecuteAsync(sql, new
            {
                OperatorId = operatorId,
                Status = "assigned",
                AssignedAt = DateTime.UtcNow,
                HandoverId = handoverId
            });

            _logger.LogInformation("エスカレーションを割り当て：{HandoverId} -> {OperatorId}", handoverId, operatorId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーションの割り当てに失敗：{HandoverId}", handoverId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ResolveHandoverAsync(string handoverId, string resolutionNotes, string? projectId = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                UPDATE ai_handovers 
                SET status = @Status, resolution_notes = @ResolutionNotes, resolved_at = @ResolvedAt
                WHERE handover_id = @HandoverId";

            var rows = await db.ExecuteAsync(sql, new
            {
                Status = "resolved",
                ResolutionNotes = resolutionNotes,
                ResolvedAt = DateTime.UtcNow,
                HandoverId = handoverId
            });

            _logger.LogInformation("エスカレーション解決：{HandoverId}", handoverId);
            return rows > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーションの解決に失敗：{HandoverId}", handoverId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<List<HandoverInfo>> GetPendingQueueAsync(string? department = null, string? projectId = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(projectId ?? DefaultProjectId);
            db.Open();

            var sql = @"
                SELECT * FROM ai_handovers 
                WHERE status = @Status";
            
            if (!string.IsNullOrEmpty(department))
            {
                sql += " AND target_department = @Department";
            }

            sql += " ORDER BY escalated_at ASC";

            var results = await db.QueryAsync(sql, new
            {
                Status = "pending",
                Department = department
            });

            return results.Select(MapToHandoverInfo).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーションキューの取得に失敗");
            return new List<HandoverInfo>();
        }
    }

    private static string GenerateHandoverId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var guid = Guid.NewGuid().ToString("N")[..8];
        return $"HO-{timestamp}-{guid}";
    }

    private static HandoverInfo MapToHandoverInfo(dynamic data)
    {
        return new HandoverInfo
        {
            HandoverId = data.handover_id,
            ConversationId = data.conversation_id,
            Reason = data.reason,
            Priority = data.priority,
            TargetDepartment = data.target_department,
            AssignedToUserId = data.assigned_to_user_id,
            Status = data.status,
            HandoverNotes = data.handover_notes,
            EscalatedAt = data.escalated_at,
            AssignedAt = data.assigned_at,
            ResolvedAt = data.resolved_at
        };
    }

    private static int GetEstimatedWaitTime(string priority)
    {
        return priority switch
        {
            "urgent" => 5,
            "high" => 15,
            "medium" => 30,
            "low" => 60,
            _ => 30
        };
    }
}
