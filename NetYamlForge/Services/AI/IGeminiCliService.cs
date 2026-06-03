using System.Text.Json;

namespace NetYamlForge.Services.AI;

/// <summary>
/// Gemini CLI を使用して AI プロンプトを実行するサービス
/// </summary>
public interface IGeminiCliService
{
    /// <summary>
    /// AI にプロンプトを送信し、テキストの回答を得る
    /// </summary>
    Task<string> PromptAsync(string prompt, string? model = null, string? projectName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// AI にプロンプトを送信し、JSON 形式で構造化された回答を得る
    /// </summary>
    Task<T?> PromptJsonAsync<T>(string prompt, string? model = null, string? projectName = null, CancellationToken cancellationToken = default);
}
