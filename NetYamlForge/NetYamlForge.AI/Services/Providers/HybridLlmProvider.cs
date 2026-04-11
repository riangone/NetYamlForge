// ファイル概要: ハイブリッド LLM プロバイダー（API ファースト + CLI フォールバック）
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Services.Providers;

/// <summary>
/// ハイブリッド LLM プロバイダー。
/// 設定に応じて API 直接呼び出しと CLI を使い分けます。
/// - 简单なチャット: API 直接呼び出し（高速）
/// - ツール必要な場合: CLI プロセス（高機能）
/// </summary>
public class HybridLlmProvider : ILlmProvider
{
    private readonly DashScopeApiProvider _apiProvider;
    private readonly CLIServiceFactory _cliFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<HybridLlmProvider> _logger;

    private bool UseDirectApi => _config.GetValue<bool>("AICli:UseDirectApi");
    private string[] ProviderPriority =>
        _config.GetSection("AiWindow:ProviderPriority").Get<string[]>()
        ?? ["qwen", "claude", "gemini", "ollama"];
    private int CliTimeoutSeconds =>
        int.TryParse(_config["AiWindow:CliTimeoutSeconds"], out var t) && t > 0 ? t : 3600;

    public HybridLlmProvider(
        DashScopeApiProvider apiProvider,
        CLIServiceFactory cliFactory,
        IConfiguration config,
        ILogger<HybridLlmProvider> logger)
    {
        _apiProvider = apiProvider;
        _cliFactory = cliFactory;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default, string? systemPromptOverride = null)
    {
        // API モードが有効な場合、まず直接 API 呼び出しを試みる
        if (UseDirectApi)
        {
            try
            {
                _logger.LogInformation("[HybridLLM] API直接呼び出しを試行");
                var apiResult = await _apiProvider.ChatAsync(prompt, systemPromptOverride, cancellationToken);
                
                if (!string.IsNullOrWhiteSpace(apiResult))
                {
                    _logger.LogInformation("[HybridLLM] API呼び出し成功: length={Length}", apiResult.Length);
                    return apiResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HybridLLM] API呼び出し失敗、CLIにフォールバック");
                // API 失敗時は CLI にフォールバック
            }
        }

        // CLI フォールバック
        var cliResult = await TryCliAsync(prompt, systemPromptOverride, cancellationToken);
        if (cliResult != null) return cliResult;

        throw new InvalidOperationException("利用可能な LLM プロバイダーがありません。設定を確認してください。");
    }

    /// <inheritdoc />
    public async Task<string> ChatCompleteAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        // API モードが有効な場合
        if (UseDirectApi)
        {
            try
            {
                _logger.LogInformation("[HybridLLM] チャットAPI直接呼び出しを試行: messages={Count}", messages.Count);
                
                // メッセージリストをテキストに変換
                var sb = new StringBuilder();
                foreach (var msg in messages)
                {
                    if (!string.IsNullOrEmpty(sb.ToString())) sb.AppendLine();
                    sb.AppendLine($"[{msg.Role}] {msg.Content}");
                }
                
                var apiResult = await _apiProvider.ChatAsync(sb.ToString(), ct: cancellationToken);
                
                if (!string.IsNullOrWhiteSpace(apiResult))
                {
                    _logger.LogInformation("[HybridLLM] チャットAPI呼び出し成功: length={Length}", apiResult.Length);
                    return apiResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HybridLLM] チャットAPI呼び出し失敗、CLIにフォールバック");
            }
        }

        // CLI フォールバック
        var sb2 = new StringBuilder();
        foreach (var msg in messages)
        {
            var label = msg.Role switch { "system" => "System", "assistant" => "Assistant", _ => "User" };
            sb2.AppendLine($"{label}: {msg.Content}");
        }
        
        return await CompleteAsync(sb2.ToString(), cancellationToken);
    }

    // ─────────────────────────────────────────────────────────
    // 内部: CLI チェーン（CliFirstLlmProvider からコピー）
    // ─────────────────────────────────────────────────────────

    private async Task<string?> TryCliAsync(string prompt, string? systemPromptOverride, CancellationToken ct)
    {
        foreach (var name in ProviderPriority)
        {
            var cli = _cliFactory.TryGetService(name);
            if (cli == null) continue;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(CliTimeoutSeconds));

                var raw = await cli.ExecuteAsync(
                    prompt,
                    workingDirectory: Path.GetTempPath(),
                    sessionId: null,
                    allowedTools: [],
                    systemPromptOverride: systemPromptOverride,
                    ct: cts.Token);

                var text = ExtractText(raw);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogInformation("[HybridLLM] CLI応答成功 provider={Name}", name);
                    return text;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[HybridLLM] CLIタイムアウト provider={Name}", name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HybridLLM] CLI失敗 provider={Name}", name);
            }
        }
        return null;
    }

    private static string ExtractText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 1. type="result"
        foreach (var line in lines)
        {
            if (!line.StartsWith("{")) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var t) && t.GetString() == "result" &&
                    root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    var text = r.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return ExtractJsonFromMarkdown(text);
                    }
                }
            }
            catch (JsonException) { }
        }

        // 2. type="assistant" の content[].text
        foreach (var line in lines)
        {
            if (!line.StartsWith("{")) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var t) || t.GetString() != "assistant") continue;
                if (!root.TryGetProperty("message", out var msg)) continue;
                if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;

                var parts = new List<string>();
                foreach (var item in content.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "text") continue;
                    if (item.TryGetProperty("text", out var tp))
                    {
                        var txt = tp.GetString();
                        if (!string.IsNullOrWhiteSpace(txt))
                        {
                            parts.Add(ExtractJsonFromMarkdown(txt));
                        }
                    }
                }
                if (parts.Count > 0) return string.Join("\n", parts).Trim();
            }
            catch (JsonException) { }
        }

        // 3. プレーンテキスト
        foreach (var line in lines)
        {
            if (line.StartsWith("{") || line.StartsWith("```") || line.Length < 5) continue;
            return line;
        }

        return raw.Trim();
    }

    private static string ExtractJsonFromMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var jsonStart = text.IndexOf("```json");
        if (jsonStart >= 0)
        {
            var jsonEnd = text.IndexOf("```", jsonStart + 7);
            if (jsonEnd > jsonStart)
            {
                var jsonContent = text.Substring(jsonStart + 7, jsonEnd - jsonStart - 7).Trim();
                return jsonContent;
            }
        }

        return text.Trim();
    }
}
