// ファイル概要: DashScope 直接 API 呼び出しサービス（CLI 不要）
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.AI.Services.Providers;

namespace NetYamlForge.AI.Services;

/// <summary>
/// DashScope API 直接呼び出しサービス。
/// CLI プロセス起動のオーバーヘッドを回避し、高速応答を実現します。
/// </summary>
public interface IDashScopeApiProvider
{
    Task<string> ChatAsync(string prompt, string? systemPrompt = null, CancellationToken ct = default);
    Task<string> ChatAsync(List<ChatMessage> messages, CancellationToken ct = default);
}

public class DashScopeApiProvider : IDashScopeApiProvider
{
    private readonly HttpClient _http;
    private readonly QwenCodeConfig _config;
    private readonly ILogger<DashScopeApiProvider> _logger;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;

    public DashScopeApiProvider(
        IOptions<CliConfig> cliConfig,
        ILogger<DashScopeApiProvider> logger)
    {
        _config = cliConfig.Value.QwenCode;
        _logger = logger;
        _apiKey = _config.ApiKey ?? Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";
        _baseUrl = _config.BaseUrl 
            ?? Environment.GetEnvironmentVariable("DASHSCOPE_BASE_URL") 
            ?? "https://dashscope.aliyuncs.com";
        _model = _config.Model ?? "qwen-plus";

        _http = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    /// <summary>
    /// 単一プロンプトでチャット（簡易 API 呼び出し）
    /// </summary>
    public async Task<string> ChatAsync(string prompt, string? systemPrompt = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("DashScope API キーが設定されていません。");
        }

        var messages = new List<ChatMessage>();
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });
        }
        
        messages.Add(new ChatMessage { Role = "user", Content = prompt });

        return await ChatAsync(messages, ct);
    }

    /// <summary>
    /// メッセージリストでチャット（完全 API 呼び出し）
    /// </summary>
    public async Task<string> ChatAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("DashScope API キーが設定されていません。");
        }

        var request = new
        {
            model = _model,
            input = new
            {
                messages = messages.Select(m => new
                {
                    role = m.Role,
                    content = m.Content
                }).ToArray()
            },
            parameters = new
            {
                result_format = "text",
                temperature = 0.7,
                top_p = 0.8,
                max_tokens = 4096
            }
        };

        try
        {
            _logger.LogDebug("[DashScope API] リクエスト送信: model={Model}, messages={Count}", 
                _model, messages.Count);

            var response = await _http.PostAsJsonAsync(
                "compatible-mode/v1/chat/completions",
                request,
                ct);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    var text = content.GetString() ?? "";
                    _logger.LogInformation("[DashScope API] 応答成功: length={Length}", text.Length);
                    return text;
                }
            }

            throw new InvalidOperationException("API 応答にテキストが含まれていません。");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[DashScope API] HTTP エラー");
            throw new InvalidOperationException($"DashScope API 呼び出しに失敗しました: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
