namespace NetYamlForge.Models.AI;

/// <summary>
/// AI 対話セッションモデル
/// </summary>
public class Conversation
{
    /// <summary>
    /// 対話 ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// 顧客 ID
    /// </summary>
    public string? CustomerId { get; set; }

    /// <summary>
    /// チャネル種別 (web, voice, line, email, sms, tablet)
    /// </summary>
    public string Channel { get; set; } = "web";

    /// <summary>
    /// ステータス (active, completed, escalated, abandoned)
    /// </summary>
    public string Status { get; set; } = "active";

    /// <summary>
    /// 最終インテント
    /// </summary>
    public string? LastIntent { get; set; }

    /// <summary>
    /// 最終置信度 (0.0000-1.0000)
    /// </summary>
    public decimal? LastConfidence { get; set; }

    /// <summary>
    /// 感情スコア (-1.0: 不満 〜 1.0: 満足)
    /// </summary>
    public decimal? SentimentScore { get; set; }

    /// <summary>
    /// コンテキスト情報（JSON）
    /// </summary>
    public string? ContextData { get; set; }

    /// <summary>
    /// 割り当てオペレーター ID
    /// </summary>
    public string? AssignedToUserId { get; set; }

    /// <summary>
    /// 開始日時
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 終了日時
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新日時
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// コンテキストデータの辞書形式バージョン
    /// </summary>
    public Dictionary<string, object>? GetContextDataDict()
    {
        if (string.IsNullOrEmpty(ContextData))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(ContextData);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// コンテキストデータを設定
    /// </summary>
    public void SetContextData(Dictionary<string, object>? data)
    {
        ContextData = data != null
            ? System.Text.Json.JsonSerializer.Serialize(data)
            : null;
    }
}

/// <summary>
/// ユーザー別の会話履歴一覧サマリー（業務アシスタント / 顧客アシスタント用）
/// </summary>
public class UserConversationSummary
{
    public string ConversationId { get; set; } = "";
    public string Channel { get; set; } = ""; // staff | web
    public string Status { get; set; } = "";
    public string? LastIntent { get; set; }
    public string StartedAt { get; set; } = "";
    public string? EndedAt { get; set; }
    public int MessageCount { get; set; }
    public string? LastAiMessage { get; set; }
}
