namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// LLM プロバイダーインターフェース
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// テキスト補完を生成
    /// </summary>
    /// <param name="prompt">入力プロンプト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="systemPromptOverride">オプション：システムプロンプトを上書き（CLIモードで使用）</param>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default, string? systemPromptOverride = null);

    /// <summary>
    /// チャット補完を生成
    /// </summary>
    /// <param name="messages">メッセージ履歴</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<string> ChatCompleteAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>
/// チャットメッセージ
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// 役割 (system, user, assistant)
    /// </summary>
    public string Role { get; set; } = "user";

    /// <summary>
    /// メッセージ内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
}
