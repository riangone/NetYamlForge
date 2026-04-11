using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.AI.Infrastructure;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Controllers.Api;

/// <summary>
/// AI 分析 API コントローラー
/// </summary>
[ApiController]
[Route("api/ai/analytics")]
[Route("{project}/api/ai/analytics")]
[Produces("application/json")]
public class AIAnalyticsController : ControllerBase
{
    private readonly IAIDbConnectionFactory _dbConnectionFactory;
    private readonly IAIProjectContext _projectContext;
    private readonly ILogger<AIAnalyticsController> _logger;
    private const string DefaultProjectId = "auto-dealer-demo";

    public AIAnalyticsController(
        IAIDbConnectionFactory dbConnectionFactory,
        IAIProjectContext projectContext,
        ILogger<AIAnalyticsController> logger)
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
    /// 対話数推移を取得
    /// </summary>
    [HttpGet("conversations-trend")]
    public async Task<ActionResult<List<ConversationTrendDto>>> GetConversationsTrend(
        [FromQuery] int days = 7,
        [FromRoute] string? project = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var sql = @"
                SELECT 
                    DATE(started_at) as date,
                    COUNT(*) as total,
                    SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) as resolved_by_ai,
                    SUM(CASE WHEN status = 'escalated' THEN 1 ELSE 0 END) as handed_over
                FROM ai_conversations
                WHERE started_at >= DATE('now', ? || ' days')
                GROUP BY DATE(started_at)
                ORDER BY date ASC";

            var results = await db.QueryAsync<ConversationTrendDto>(sql, new { days = -days });
            return Ok(results.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "対話数推移の取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// インテント内訳を取得
    /// </summary>
    [HttpGet("intent-distribution")]
    public async Task<ActionResult<List<IntentDistributionDto>>> GetIntentDistribution(
        [FromQuery] string? date = null,
        [FromRoute] string? project = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            // パラメータ化クエリを使用（文字列連結なし）
            string sql;
            object? param = null;

            if (string.IsNullOrEmpty(date))
            {
                sql = @"
                    SELECT
                        COALESCE(last_intent, 'unknown') as intent,
                        COUNT(*) as count
                    FROM ai_conversations
                    WHERE DATE(started_at) = DATE('now')
                    GROUP BY last_intent
                    ORDER BY count DESC";
            }
            else
            {
                sql = @"
                    SELECT
                        COALESCE(last_intent, 'unknown') as intent,
                        COUNT(*) as count
                    FROM ai_conversations
                    WHERE DATE(started_at) = @Date
                    GROUP BY last_intent
                    ORDER BY count DESC";
                param = new { Date = date };
            }

            var results = await db.QueryAsync<IntentDistributionDto>(sql, param);
            return Ok(results.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "インテント内訳の取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// 感情分析サマリーを取得
    /// </summary>
    [HttpGet("sentiment-summary")]
    public async Task<ActionResult<List<SentimentSummaryDto>>> GetSentimentSummary([FromRoute] string? project = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var sql = @"
                SELECT 
                    CASE 
                        WHEN sentiment_score > 0.3 THEN 'positive'
                        WHEN sentiment_score < -0.3 THEN 'negative'
                        ELSE 'neutral'
                    END as label,
                    COUNT(*) as count
                FROM ai_conversations
                WHERE sentiment_score IS NOT NULL
                GROUP BY label
                ORDER BY 
                    CASE label
                        WHEN 'positive' THEN 1
                        WHEN 'neutral' THEN 2
                        WHEN 'negative' THEN 3
                    END";

            var results = await db.QueryAsync<SentimentSummaryDto>(sql);
            return Ok(results.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "感情分析サマリーの取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// チャネル別利用状況を取得
    /// </summary>
    [HttpGet("channel-breakdown")]
    public async Task<ActionResult<List<ChannelBreakdownDto>>> GetChannelBreakdown(
        [FromQuery] int days = 7,
        [FromRoute] string? project = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var sql = @"
                SELECT 
                    channel,
                    COUNT(*) as count
                FROM ai_conversations
                WHERE started_at >= DATE('now', ? || ' days')
                GROUP BY channel
                ORDER BY count DESC";

            var results = await db.QueryAsync<ChannelBreakdownDto>(sql, new { days = -days });
            return Ok(results.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "チャネル別利用状況の取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// ダッシュボード統計を取得
    /// </summary>
    [HttpGet("dashboard-stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetDashboardStats([FromRoute] string? project = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var stats = new DashboardStatsDto();

            // アクティブ対話数
            stats.ActiveConversations = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ai_conversations WHERE status = 'active'");

            // 待機中エスカレーション
            stats.PendingHandovers = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ai_handovers WHERE status = 'pending'");

            // 今日 AI 回答数
            stats.TodayAiResponses = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ai_messages WHERE DATE(timestamp) = DATE('now') AND sender = 'ai'");

            // 平均回答率
            var todayTotal = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ai_conversations WHERE DATE(started_at) = DATE('now')");
            var todayResolved = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ai_conversations WHERE DATE(started_at) = DATE('now') AND status = 'completed'");
            
            stats.AvgResponseRate = todayTotal > 0 
                ? Math.Round((double)todayResolved / todayTotal * 100, 1) 
                : 0;

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ダッシュボード統計の取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// エスカレーションキューを取得
    /// </summary>
    [HttpGet("handover-queue")]
    public async Task<ActionResult<List<HandoverQueueItemDto>>> GetHandoverQueue(
        [FromQuery] string? department = null,
        [FromRoute] string? project = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var sql = @"
                SELECT 
                    h.handover_id,
                    h.conversation_id,
                    h.reason,
                    h.priority,
                    h.target_department,
                    h.escalated_at,
                    c.channel,
                    c.last_intent
                FROM ai_handovers h
                LEFT JOIN ai_conversations c ON h.conversation_id = c.conversation_id
                WHERE h.status = 'pending'
                " + (string.IsNullOrEmpty(department) ? "" : "AND h.target_department = @Department") + @"
                ORDER BY 
                    CASE h.priority
                        WHEN 'urgent' THEN 1
                        WHEN 'high' THEN 2
                        WHEN 'medium' THEN 3
                        WHEN 'low' THEN 4
                    END,
                    h.escalated_at ASC";

            var results = await db.QueryAsync<HandoverQueueItemDto>(sql, new { Department = department });
            return Ok(results.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーションキューの取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// エスカレーション統計取得
    /// </summary>
    [HttpGet("handover-stats")]
    public async Task<ActionResult<HandoverStatsDto>> GetHandoverStats([FromRoute] string? project = null)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var stats = new HandoverStatsDto();

            stats.Pending = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ai_handovers WHERE status = 'pending'");
            stats.Assigned = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ai_handovers WHERE status = 'assigned'");
            stats.ResolvedToday = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ai_handovers WHERE status = 'resolved' AND DATE(resolved_at) = DATE('now')");
            
            var avgTime = await db.ExecuteScalarAsync<double?>(@"
                SELECT AVG((julianday(resolved_at) - julianday(escalated_at)) * 24 * 60)
                FROM ai_handovers
                WHERE status = 'resolved' AND resolved_at IS NOT NULL
                AND julianday(resolved_at) - julianday(escalated_at) > 0");
            stats.AvgResolutionTime = avgTime.HasValue ? (int)avgTime.Value : 0;

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーション統計取得に失敗");
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// エスカレーション詳細取得
    /// </summary>
    [HttpGet("handovers/{id}")]
    public async Task<ActionResult<HandoverDetailDto>> GetHandover(string id, [FromRoute] string? project)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            var sql = @"SELECT h.*, u.name as assigned_operator_name FROM ai_handovers h
                LEFT JOIN users u ON h.assigned_to_user_id = u.user_id
                WHERE h.handover_id = @HandoverId";

            var result = await db.QueryFirstOrDefaultAsync(sql, new { HandoverId = id });

            if (result == null) return NotFound();

            return Ok(new HandoverDetailDto
            {
                HandoverId = result.handover_id,
                ConversationId = result.conversation_id,
                Reason = result.reason,
                Priority = result.priority,
                TargetDepartment = result.target_department,
                AssignedToUserId = result.assigned_to_user_id,
                AssignedOperatorName = result.assigned_operator_name,
                Status = result.status,
                HandoverNotes = result.handover_notes,
                ResolutionNotes = result.resolution_notes,
                EscalatedAt = result.escalated_at,
                AssignedAt = result.assigned_at,
                ResolvedAt = result.resolved_at
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーション詳細取得に失敗：{HandoverId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// エスカレーションを割り当て
    /// </summary>
    [HttpPut("handovers/{id}/assign")]
    public async Task<ActionResult> AssignHandover(
        string id,
        [FromBody] AssignHandoverRequest request,
        [FromRoute] string? project)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            await db.ExecuteAsync(@"UPDATE ai_handovers SET 
                assigned_to_user_id = @OperatorId, status = 'assigned', assigned_at = datetime('now')
                WHERE handover_id = @HandoverId", new { HandoverId = id, OperatorId = request.OperatorId });

            return Ok(new { message = "割り当てました" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーション割り当てに失敗：{HandoverId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }

    /// <summary>
    /// エスカレーションを解決
    /// </summary>
    [HttpPut("handovers/{id}/resolve")]
    public async Task<ActionResult> ResolveHandover(
        string id,
        [FromBody] ResolveHandoverRequest request,
        [FromRoute] string? project)
    {
        try
        {
            using var db = _dbConnectionFactory.CreateConnection(ResolveProject(project));
            db.Open();

            await db.ExecuteAsync(@"UPDATE ai_handovers SET 
                status = 'resolved', resolution_notes = @ResolutionNotes, resolved_at = datetime('now')
                WHERE handover_id = @HandoverId", new { HandoverId = id, ResolutionNotes = request.ResolutionNotes });

            return Ok(new { message = "解決済みにマークしました" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エスカレーション解決に失敗：{HandoverId}", id);
            return StatusCode(500, "エラーが発生しました");
        }
    }
}

// DTO クラス

public class ConversationTrendDto
{
    public DateTime Date { get; set; }
    public int Total { get; set; }
    public int ResolvedByAi { get; set; }
    public int HandedOver { get; set; }
}

public class IntentDistributionDto
{
    public string Intent { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class SentimentSummaryDto
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ChannelBreakdownDto
{
    public string Channel { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DashboardStatsDto
{
    public int ActiveConversations { get; set; }
    public int PendingHandovers { get; set; }
    public int TodayAiResponses { get; set; }
    public double AvgResponseRate { get; set; }
}

public class HandoverQueueItemDto
{
    public string HandoverId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? TargetDepartment { get; set; }
    public DateTime EscalatedAt { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string? LastIntent { get; set; }
}

public class HandoverStatsDto
{
    public int Pending { get; set; }
    public int Assigned { get; set; }
    public int ResolvedToday { get; set; }
    public int AvgResolutionTime { get; set; }
}

public class HandoverDetailDto
{
    public string HandoverId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? TargetDepartment { get; set; }
    public string? AssignedToUserId { get; set; }
    public string? AssignedOperatorName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? HandoverNotes { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime EscalatedAt { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class AssignHandoverRequest
{
    public string OperatorId { get; set; } = string.Empty;
}

public class ResolveHandoverRequest
{
    public string ResolutionNotes { get; set; } = string.Empty;
}
