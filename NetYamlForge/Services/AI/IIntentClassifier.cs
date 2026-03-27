using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 意図分類インターフェース
/// </summary>
public interface IIntentClassifier
{
    /// <summary>
    /// メッセージから意図を分類
    /// </summary>
    /// <param name="message">顧客メッセージ</param>
    /// <param name="conversationContext">対話コンテキスト</param>
    /// <param name="projectId">プロジェクト ID</param>
    Task<IntentResult> ClassifyAsync(string message, ConversationContext? conversationContext = null, string? projectId = null);

    /// <summary>
    /// 意図分類キャッシュをクリア
    /// </summary>
    Task ClearCacheAsync(string? projectId = null);
}

/// <summary>
/// 対話コンテキスト
/// </summary>
public class ConversationContext
{
    /// <summary>
    /// 対話 ID
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// 以前のメッセージ履歴
    /// </summary>
    public List<Message> PreviousMessages { get; set; } = new();

    /// <summary>
    /// 抽出されたエンティティ
    /// </summary>
    public Dictionary<string, string> Entities { get; set; } = new();

    /// <summary>
    /// 現在のインテント
    /// </summary>
    public string? CurrentIntent { get; set; }

    /// <summary>
    /// 対話開始からの経過時間（分）
    /// </summary>
    public int ElapsedMinutes { get; set; }
}
