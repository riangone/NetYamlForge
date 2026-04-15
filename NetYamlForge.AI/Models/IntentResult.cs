namespace NetYamlForge.AI.Models;

/// <summary>
/// 意図判定結果モデル
/// </summary>
public class IntentResult
{
    /// <summary>
    /// 判定されたインテント
    /// </summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// 置信度 (0.0-1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 抽出されたエンティティ
    /// </summary>
    public Dictionary<string, string> Entities { get; set; } = new();

    /// <summary>
    /// 判定方法 (rule, llm, hybrid)
    /// </summary>
    public string Method { get; set; } = "rule";

    /// <summary>
    /// マッチしたルール ID（ルールベースの場合）
    /// </summary>
    public string? MatchedRuleId { get; set; }

    /// <summary>
    /// 感情スコア
    /// </summary>
    public double SentimentScore { get; set; }

    /// <summary>
    /// エスカレーション推奨フラグ
    /// </summary>
    public bool SuggestHandover { get; set; }

    /// <summary>
    /// エスカレーション理由
    /// </summary>
    public string? HandoverReason { get; set; }

    /// <summary>
    /// クイック返信ボタン
    /// </summary>
    public List<QuickReplyButton> QuickReplies { get; set; } = new();

    /// <summary>
    /// 追加質問が必要か
    /// </summary>
    public bool NeedsMoreInfo { get; set; }

    /// <summary>
    /// 必要な情報フィールド
    /// </summary>
    public List<string> RequiredFields { get; set; } = new();
}

/// <summary>
/// クイック返信ボタン
/// </summary>
public class QuickReplyButton
{
    /// <summary>
    /// ボタンラベル
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// アクション種別 (postback, url, phone)
    /// </summary>
    public string ActionType { get; set; } = "postback";

    /// <summary>
    /// アクション値
    /// </summary>
    public string ActionValue { get; set; } = string.Empty;
}
