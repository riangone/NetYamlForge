using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI プロバイダー別の HTTP 呼び出しロジックを集約したユーティリティ。
/// AiAnnotatorExecutor 等の複数プロバイダー対応 Executor で共通化するためのもの。
/// プロバイダー別のリクエスト構築・レスポンス解析を1箇所に集約し、
/// Executor はプロンプト構築と結果のビジネスロジック処理のみに集中します。
/// </summary>
public sealed class AiProviderDispatcher
{
    private readonly ILogger _logger;
    private readonly HttpClient _http;
    private readonly ICliChainService? _cliChainService;
    private readonly IAiScenarioYamlLoader? _scenarioLoader;

    /// <param name="logger">ログ出力先</param>
    /// <param name="httpClient">HTTP 呼び出し用クライアント（未指定時は既定タイムアウト付きで新規作成）</param>
    /// <param name="cliChainService">
    /// 呼び出し元が既に DI で解決済みの <see cref="ICliChainService"/> を持つ場合はそれを渡す。
    /// 渡された場合は appsettings.json の AiCliChain 設定がそのまま反映される。
    /// 未指定の場合は既定設定の CliChainService を都度生成する（設定ファイルの上書きは反映されない）。
    /// </param>
    /// <param name="scenarioLoader">
    /// プロジェクトの ai/scenarios.yaml からプロバイダー接続設定（model / base_url など）を
    /// 解決するためのローダー。渡された場合、設定値は環境変数 → scenarios.yaml → ハードコード既定値 の順で解決される。
    /// </param>
    public AiProviderDispatcher(
        ILogger logger,
        HttpClient? httpClient = null,
        ICliChainService? cliChainService = null,
        IAiScenarioYamlLoader? scenarioLoader = null)
    {
        _logger = logger;
        var timeoutEnv = Environment.GetEnvironmentVariable("AI_HTTP_TIMEOUT_SECONDS");
        var timeoutSeconds = int.TryParse(timeoutEnv, out var t) && t > 0 ? t : 300;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _cliChainService = cliChainService;
        _scenarioLoader = scenarioLoader;
    }

    /// <summary>
    /// 指定プロバイダーを用いて画像付きプロンプトを実行し、生のレスポンステキストを返す。
    /// </summary>
    public async Task<string?> DispatchAsync(
        string provider,
        string prompt,
        string absoluteImagePath,
        string? projectName,
        Func<string, string?> getEnvValue,
        CancellationToken ct)
    {
        return provider.ToLowerInvariant() switch
        {
            "lmstudio"    => await CallLmStudioAsync(prompt, absoluteImagePath, projectName, getEnvValue, ct),
            "gemini_cli"  => await CallGeminiCliAsync(prompt, absoluteImagePath, projectName, getEnvValue, ct),
            "gemini"      => await CallGeminiAsync(prompt, absoluteImagePath, projectName, getEnvValue, ct),
            "ollama"      => await CallOllamaAsync(prompt, absoluteImagePath, projectName, getEnvValue, ct),
            "antigravity" => await CallGeminiCliAsync(prompt, absoluteImagePath, projectName, getEnvValue, ct),
            "anthropic"   => await CallAnthropicAsync(prompt, absoluteImagePath, projectName, getEnvValue, ct),
            _ => LogAndNull(provider)
        };
    }

    /// <summary>
    /// プロジェクトの ai/scenarios.yaml からプロバイダー設定を取得する（無ければ null）。
    /// </summary>
    private AiProviderConfig? GetProviderConfig(string provider, string? projectName)
    {
        if (string.IsNullOrEmpty(projectName) || _scenarioLoader == null) return null;
        try
        {
            var config = _scenarioLoader.GetConfig(projectName);
            return config.Providers.TryGetValue(provider, out var p) ? p : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load provider config from scenarios.yaml for {Provider} in {Project}", provider, projectName);
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────
    // LM Studio (local OpenAI-compatible)
    // ──────────────────────────────────────────────────────────

    private async Task<string?> CallLmStudioAsync(
        string prompt, string imagePath, string? projectName,
        Func<string, string?> getEnvValue, CancellationToken ct)
    {
        var cfg = GetProviderConfig("lmstudio", projectName);
        var baseUrl = getEnvValue("LMSTUDIO_BASE_URL") ?? cfg?.BaseUrl ?? "http://localhost:1234/v1";
        var model = getEnvValue("LMSTUDIO_MODEL") ?? cfg?.Model ?? cfg?.VisionModel ?? "google/gemma-4-12b";
        var (base64, mimeType) = EncodeImage(imagePath);

        var body = new
        {
            model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64}" } }
                    }
                }
            },
            max_tokens = cfg?.MaxTokens ?? 1024,
            temperature = cfg?.Temperature ?? 0.2
        };

        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { _logger.LogError("LM Studio {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
    }

    // ──────────────────────────────────────────────────────────
    // Gemini API (direct HTTP)
    // ──────────────────────────────────────────────────────────

    private async Task<string?> CallGeminiAsync(
        string prompt, string imagePath, string? projectName,
        Func<string, string?> getEnvValue, CancellationToken ct)
    {
        var cfg = GetProviderConfig("gemini", projectName);
        var apiKey = getEnvValue("GEMINI_API_KEY") ?? cfg?.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) { _logger.LogWarning("GEMINI_API_KEY not set"); return null; }

        var (base64, mimeType) = EncodeImage(imagePath);
        var model = cfg?.VisionModel ?? "gemini-2.0-flash";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var body = new
        {
            contents = new[]
            {
                new { parts = new object[] { new { text = prompt }, new { inline_data = new { mime_type = mimeType, data = base64 } } } }
            },
            generationConfig = new { maxOutputTokens = cfg?.MaxTokens ?? 1024, temperature = cfg?.Temperature ?? 0.2 }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { _logger.LogError("Gemini {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
    }

    // ──────────────────────────────────────────────────────────
    // Gemini CLI (Antigravity / opencode)
    // ──────────────────────────────────────────────────────────

    private async Task<string?> CallGeminiCliAsync(
        string prompt, string imagePath, string? projectName,
        Func<string, string?> getEnvValue, CancellationToken ct)
    {
        // DI から渡された ICliChainService があれば優先して使う（appsettings.json の AiCliChain 設定を反映）。
        // 無い場合のみ既定設定のインスタンスをフォールバック生成する。
        var cli = _cliChainService ?? new CliChainService(Microsoft.Extensions.Logging.Abstractions.NullLogger<CliChainService>.Instance);
        var result = await cli.PromptAsync(prompt, imagePath: imagePath, projectName: projectName, cancellationToken: ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
            return null;
        return result.Text;
    }

    // ──────────────────────────────────────────────────────────
    // Ollama (local)
    // ──────────────────────────────────────────────────────────

    private async Task<string?> CallOllamaAsync(
        string prompt, string imagePath, string? projectName,
        Func<string, string?> getEnvValue, CancellationToken ct)
    {
        var cfg = GetProviderConfig("ollama", projectName);
        var baseUrl = getEnvValue("OLLAMA_BASE_URL") ?? cfg?.BaseUrl ?? "http://localhost:11434";
        var model = getEnvValue("OLLAMA_VISION_MODEL") ?? cfg?.VisionModel ?? "llava:13b";
        var (base64, _) = EncodeImage(imagePath);

        var body = new { model, prompt, images = new[] { base64 }, stream = false };
        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { _logger.LogError("Ollama {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("response").GetString();
    }

    // ──────────────────────────────────────────────────────────
    // Anthropic Claude API
    // ──────────────────────────────────────────────────────────

    private async Task<string?> CallAnthropicAsync(
        string prompt, string imagePath, string? projectName,
        Func<string, string?> getEnvValue, CancellationToken ct)
    {
        var apiKey = getEnvValue("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) { _logger.LogWarning("ANTHROPIC_API_KEY not set"); return null; }

        var (base64, mediaType) = EncodeImage(imagePath);
        var model = getEnvValue("ANTHROPIC_MODEL") ?? "claude-haiku-4-5-20251001";

        var body = new
        {
            model,
            max_tokens = 1024,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = mediaType, data = base64 } },
                        new { type = "text", text = prompt }
                    }
                }
            }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        var resp = await _http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) { _logger.LogError("Anthropic {Status}: {Body}", resp.StatusCode, json); return null; }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
    }

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    private string? LogAndNull(string provider)
    {
        _logger.LogWarning("Unknown annotation provider '{Provider}'", provider);
        return null;
    }

    internal static (string base64, string mediaType) EncodeImage(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var mime = ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png"           => "image/png",
            "gif"           => "image/gif",
            "webp"          => "image/webp",
            "heic" or "heif" => "image/heic",
            _               => "image/jpeg"
        };
        return (Convert.ToBase64String(bytes), mime);
    }

    /// <summary>
    /// プロバイダー名からモデル名を取得する。
    /// </summary>
    public static string GetModelName(string provider) => provider switch
    {
        "lmstudio"    => "google/gemma-4-12b",
        "gemini"      => "gemini-2.0-flash",
        "gemini_cli"  => "antigravity-cli",
        "antigravity" => "antigravity-cli",
        "ollama"      => "llava:13b",
        "anthropic"   => "claude-haiku-4-5-20251001",
        _             => provider
    };
}
