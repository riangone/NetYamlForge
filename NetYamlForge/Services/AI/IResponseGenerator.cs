using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 応答生成インターフェース
/// </summary>
public interface IResponseGenerator
{
    /// <summary>
    /// 応答メッセージを生成
    /// </summary>
    /// <param name="intentResult">意図判定結果</param>
    /// <param name="conversationContext">対話コンテキスト</param>
    /// <param name="projectId">プロジェクト ID</param>
    Task<AIResponse> GenerateResponseAsync(IntentResult intentResult, ConversationContext? conversationContext = null, string? projectId = null);

    /// <summary>
    /// 初期歡迎メッセージを生成
    /// </summary>
    /// <param name="channel">チャネル種別</param>
    /// <param name="projectId">プロジェクト ID</param>
    string GenerateWelcomeMessage(string channel, string? projectId = null);

    /// <summary>
    /// エスカレーションメッセージを生成
    /// </summary>
    /// <param name="reason">エスカレーション理由</param>
    /// <param name="projectId">プロジェクト ID</param>
    string GenerateHandoverMessage(string reason, string? projectId = null);
}

/// <summary>
/// AI 応答モデル
/// </summary>
public class AIResponse
{
    /// <summary>
    /// 応答メッセージ
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// メッセージ種別
    /// </summary>
    public string MessageType { get; set; } = "text";

    /// <summary>
    /// クイック返信ボタン
    /// </summary>
    public List<QuickReplyButton> QuickReplies { get; set; } = new();

    /// <summary>
    /// 追加データ（JSON）
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// 生成方法 (template, llm, knowledge)
    /// </summary>
    public string GenerationMethod { get; set; } = "template";

    /// <summary>
    /// 使用された AI モデル
    /// </summary>
    public string AiModel { get; set; } = "Default AI";

    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public long ProcessingTimeMs { get; set; }
}
