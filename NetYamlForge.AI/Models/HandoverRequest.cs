namespace NetYamlForge.AI.Models;

/// <summary>
/// エスカレーション要求モデル
/// </summary>
public class HandoverRequest
{
    /// <summary>
    /// 対話 ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// エスカレーション理由
    /// </summary>
    public string Reason { get; set; } = "ai_unable";

    /// <summary>
    /// 優先度 (low, medium, high, urgent)
    /// </summary>
    public string Priority { get; set; } = "medium";

    /// <summary>
    /// 対象部門 (sales, service, quality, finance, general)
    /// </summary>
    public string? TargetDepartment { get; set; }

    /// <summary>
    /// 指定オペレーター ID（任意）
    /// </summary>
    public string? AssignedToUserId { get; set; }

    /// <summary>
    /// 引き継ぎメモ
    /// </summary>
    public string? HandoverNotes { get; set; }

    /// <summary>
    /// 会話履歴の要約
    /// </summary>
    public string? ConversationSummary { get; set; }

    /// <summary>
    /// 顧客情報
    /// </summary>
    public CustomerInfo? Customer { get; set; }
}

/// <summary>
/// エスカレーション結果
/// </summary>
public class HandoverResult
{
    /// <summary>
    /// エスカレーション ID
    /// </summary>
    public string HandoverId { get; set; } = string.Empty;

    /// <summary>
    /// 成功フラグ
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 割り当てオペレーター名
    /// </summary>
    public string? AssignedOperatorName { get; set; }

    /// <summary>
    /// 予想待ち時間（分）
    /// </summary>
    public int? EstimatedWaitMinutes { get; set; }

    /// <summary>
    /// チケット ID
    /// </summary>
    public string? TicketId { get; set; }
}

/// <summary>
/// 顧客情報
/// </summary>
public class CustomerInfo
{
    /// <summary>
    /// 顧客 ID
    /// </summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// 顧客名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 電話番号
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// メールアドレス
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 顧客ランク (regular, vip, premium)
    /// </summary>
    public string TierLevel { get; set; } = "regular";
}
