// AI サービス HTTP API クライアント
// 主应用から独立 AI サービスへ HTTP 経由でアクセスするためのクライアント

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Client;

/// <summary>
/// AI サービス HTTP API クライアント
/// 中期アーキテクチャ：独立プロセス版 AI サービスとの通信用
/// </summary>
public class AIServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AIServiceClient> _logger;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AIServiceClient(HttpClient httpClient, IConfiguration configuration, ILogger<AIServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["AI:ServiceBaseUrl"] ?? "http://localhost:5200";
    }

    /// <summary>
    /// AI チャット処理を実行
    /// </summary>
    public async Task<AIChatResponse> ChatAsync(AIChatRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/api/ai/chat", content, cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<AIChatResponse>(responseJson, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize AI chat response");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to call AI chat service at {BaseUrl}", _baseUrl);
            throw new AIServiceUnavailableException("AI chat service is unavailable", ex);
        }
    }

    /// <summary>
    /// 自然言語クエリを実行
    /// </summary>
    public async Task<string> ExecuteQueryAsync(string query, string? projectName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new { Query = query, ProjectName = projectName };
            var content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/api/ai/query", content, cancellationToken);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to call AI query service at {BaseUrl}", _baseUrl);
            throw new AIServiceUnavailableException("AI query service is unavailable", ex);
        }
    }

    /// <summary>
    /// 会話詳情を取得
    /// </summary>
    public async Task<IEnumerable<Conversation>> GetConversationsAsync(string? projectId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_baseUrl}/api/ai/conversations";
            if (!string.IsNullOrEmpty(projectId))
            {
                url += $"?projectId={projectId}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<IEnumerable<Conversation>>(responseJson, JsonOptions)
                ?? Array.Empty<Conversation>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get conversations from AI service at {BaseUrl}", _baseUrl);
            return Array.Empty<Conversation>();
        }
    }

    /// <summary>
    /// AI サービスのヘルスチェック
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/health", cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// AI サービス利用不能例外
/// </summary>
public class AIServiceUnavailableException : Exception
{
    public AIServiceUnavailableException(string message) : base(message) { }
    public AIServiceUnavailableException(string message, Exception inner) : base(message, inner) { }
}
