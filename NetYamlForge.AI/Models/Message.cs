namespace NetYamlForge.AI.Models;

/// <summary>
/// AI メッセージモデル
/// </summary>
public class Message
{
    /// <summary>
    /// メッセージ ID
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// 対話 ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// 発話者 (customer, ai, agent)
    /// </summary>
    public string Sender { get; set; } = "customer";

    /// <summary>
    /// メッセージ種別 (text, voice_transcript, image, quick_reply, button, carousel)
    /// </summary>
    public string MessageType { get; set; } = "text";

    /// <summary>
    /// メッセージ内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 判定されたインテント
    /// </summary>
    public string? Intent { get; set; }

    /// <summary>
    /// 検出されたエンティティ（JSON）
    /// </summary>
    public string? EntitiesJson { get; set; }

    /// <summary>
    /// 置信度スコア (0.0000-1.0000)
    /// </summary>
    public decimal? ConfidenceScore { get; set; }

    /// <summary>
    /// 感情スコア
    /// </summary>
    public decimal? SentimentScore { get; set; }

    /// <summary>
    /// メタデータ（JSON）
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// 発話日時
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// エンティティの辞書形式バージョン
    /// </summary>
    public Dictionary<string, string>? GetEntitiesDict()
    {
        if (string.IsNullOrEmpty(EntitiesJson))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(EntitiesJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// エンティティを設定
    /// </summary>
    public void SetEntities(Dictionary<string, string>? entities)
    {
        EntitiesJson = entities != null 
            ? System.Text.Json.JsonSerializer.Serialize(entities) 
            : null;
    }
}
