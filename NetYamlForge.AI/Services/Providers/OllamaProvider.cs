using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetYamlForge.AI.Services.Providers;

/// <summary>
/// Ollama LLM プロバイダー
/// </summary>
public class OllamaProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly OllamaConfig _config;
    private readonly ILogger<OllamaProvider> _logger;

    public OllamaProvider(
        HttpClient httpClient,
        IOptions<OllamaConfig> configOptions,
        ILogger<OllamaProvider> logger)
    {
        _httpClient = httpClient;
        _config = configOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default, string? systemPromptOverride = null)
    {
        var finalPrompt = systemPromptOverride != null
            ? $"{systemPromptOverride}\n\n---\n\n{prompt}"
            : prompt;

        var request = new
        {
            model = _config.Model,
            prompt = finalPrompt,
            stream = false,
            options = new
            {
                temperature = _config.Temperature,
                num_predict = _config.MaxTokens
            }
        };

        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/api/generate", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<OllamaResponse>(responseJson);

            return parsed?.Response ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama API コールに失敗");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> ChatCompleteAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            model = _config.Model,
            messages = messages.Select(m => new
            {
                role = m.Role,
                content = m.Content
            }),
            stream = false,
            options = new
            {
                temperature = _config.Temperature,
                num_predict = _config.MaxTokens
            }
        };

        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/api/chat", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<OllamaChatResponse>(responseJson);

            return parsed?.Message?.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama Chat API コールに失敗");
            throw;
        }
    }

    private class OllamaResponse
    {
        public string Response { get; set; } = string.Empty;
    }

    private class OllamaChatResponse
    {
        public ChatMessageDto? Message { get; set; }
    }

    private class ChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}

/// <summary>
/// Ollama 設定
/// </summary>
public class OllamaConfig
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5-coder:7b";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
}
