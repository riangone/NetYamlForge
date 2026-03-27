using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// エスカレーション管理インターフェース
/// </summary>
public interface IHandoverManager
{
    /// <summary>
    /// エスカレーションが必要か評価
    /// </summary>
    Task<HandoverEvaluation> EvaluateHandoverNeedAsync(IntentResult intentResult, ConversationContext? context = null, string? projectId = null);

    /// <summary>
    /// エスカレーションを作成
    /// </summary>
    Task<HandoverResult> CreateHandoverAsync(HandoverRequest request, string? projectId = null);

    /// <summary>
    /// エスカレーションを取得
    /// </summary>
    Task<HandoverInfo?> GetHandoverAsync(string handoverId, string? projectId = null);

    /// <summary>
    /// オペレーターを割り当て
    /// </summary>
    Task<bool> AssignOperatorAsync(string handoverId, string operatorId, string? projectId = null);

    /// <summary>
    /// エスカレーションを解決済みにマーク
    /// </summary>
    Task<bool> ResolveHandoverAsync(string handoverId, string resolutionNotes, string? projectId = null);

    /// <summary>
    /// 待機中のエスカレーションキューを取得
    /// </summary>
    Task<List<HandoverInfo>> GetPendingQueueAsync(string? department = null, string? projectId = null);
}

/// <summary>
/// エスカレーション評価結果
/// </summary>
public class HandoverEvaluation
{
    /// <summary>
    /// エスカレーションが必要か
    /// </summary>
    public bool NeedsHandover { get; set; }

    /// <summary>
    /// エスカレーション理由
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 優先度
    /// </summary>
    public string Priority { get; set; } = "medium";

    /// <summary>
    /// 推奨部門
    /// </summary>
    public string? TargetDepartment { get; set; }

    /// <summary>
    /// 評価スコア
    /// </summary>
    public double EvaluationScore { get; set; }
}

/// <summary>
/// エスカレーション情報
/// </summary>
public class HandoverInfo
{
    public string HandoverId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
    public string? TargetDepartment { get; set; }
    public string? AssignedToUserId { get; set; }
    public string? AssignedOperatorName { get; set; }
    public string Status { get; set; } = "pending";
    public string? HandoverNotes { get; set; }
    public DateTime EscalatedAt { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
