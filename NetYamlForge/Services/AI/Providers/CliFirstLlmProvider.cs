using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// CLI ファーストの ILlmProvider 実装。
/// 設定された CLI プロバイダーチェーン（QwenCode / Claude CLI / Gemini …）を順に試み、
/// すべて失敗した場合は Claude API（直接）にフォールバックします。
/// </summary>
public class CliFirstLlmProvider : ILlmProvider
{
    private readonly CLIServiceFactory _cliFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<CliFirstLlmProvider> _logger;

    private string[] ProviderPriority =>
        _config.GetSection("AiWindow:ProviderPriority").Get<string[]>()
        ?? ["claude", "qwen", "gemini", "ollama"];
    private int CliTimeoutSeconds =>
        int.TryParse(_config["AiWindow:CliTimeoutSeconds"], out var t) ? t : 8;
    private string ClaudeApiKey =>
        _config["AiWindow:Claude:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    private string ClaudeModel =>
        _config["AiWindow:Claude:Model"] ?? "claude-haiku-4-5-20251001";

    public CliFirstLlmProvider(
        CLIServiceFactory cliFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<CliFirstLlmProvider> logger)
    {
        _cliFactory = cliFactory;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // 1. CLI プロバイダーチェーン
        var cliResult = await TryCliAsync(prompt, cancellationToken);
        if (cliResult != null) return cliResult;

        // 2. Claude API フォールバック
        if (!string.IsNullOrWhiteSpace(ClaudeApiKey))
        {
            try { return await CallClaudeAsync(prompt, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Claude API フォールバック失敗"); }
        }

        throw new InvalidOperationException("利用可能な LLM プロバイダーがありません。CLI または Claude API キーを設定してください。");
    }

    /// <inheritdoc />
    public async Task<string> ChatCompleteAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        // チャット形式を単一プロンプトに変換して CompleteAsync へ委譲
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            var label = msg.Role switch { "system" => "System", "assistant" => "Assistant", _ => "User" };
            sb.AppendLine($"{label}: {msg.Content}");
        }
        return await CompleteAsync(sb.ToString(), cancellationToken);
    }

    // ─────────────────────────────────────────────────────────
    // 内部: CLI チェーン
    // ─────────────────────────────────────────────────────────

    private async Task<string?> TryCliAsync(string prompt, CancellationToken ct)
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
                    systemPromptOverride: null,
                    ct: cts.Token);

                var text = ExtractText(raw);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogInformation("CliFirstLlmProvider: CLI応答成功 provider={Name}", name);
                    return text;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("CliFirstLlmProvider: CLIタイムアウト provider={Name}", name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CliFirstLlmProvider: CLI失敗 provider={Name}", name);
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────
    // 内部: CLI JSON 出力のテキスト抽出
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// CLI 出力（Claude Code JSON ログ形式など）から応答テキストを抽出します。
    /// type=result の result フィールド → type=assistant の text → プレーンテキスト の順で試みます。
    /// </summary>
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
                    if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                }
            }
            catch (JsonException) { }
        }

        // 2. type="assistant" の content[].text（thinking を除外）
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
                        if (!string.IsNullOrWhiteSpace(txt)) parts.Add(txt);
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

    // ─────────────────────────────────────────────────────────
    // 内部: Claude API（直接）
    // ─────────────────────────────────────────────────────────

    private async Task<string> CallClaudeAsync(string prompt, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("ClaudeApiClient");
        client.DefaultRequestHeaders.Remove("x-api-key");
        client.DefaultRequestHeaders.Add("x-api-key", ClaudeApiKey);

        var body = new
        {
            model = ClaudeModel,
            max_tokens = 1024,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("v1/messages", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("content", out var contentArr) &&
            contentArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentArr.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var tp) && tp.GetString() == "text" &&
                    item.TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString()?.Trim() ?? "";
                }
            }
        }

        throw new InvalidOperationException("Claude API レスポンスからテキストを取り出せませんでした");
    }
}
