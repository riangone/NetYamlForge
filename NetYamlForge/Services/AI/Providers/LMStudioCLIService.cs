using System.Net.Http;
using System.Text;
using System.Text.Json;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;
using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// LM Studio CLI サービス
/// 通过 OpenAI 兼容 API 与本地 LM Studio 服务交互
/// </summary>
public class LmStudioCLIService : BaseCLIService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public LmStudioCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger<LmStudioCLIService> logger,
        HttpClient? httpClient = null)
        : base(executor, config, skillLoader, logger, "lmstudio")
    {
        _httpClient = httpClient ?? new HttpClient();
        _baseUrl = string.IsNullOrEmpty(Config.LmStudio.BaseUrl)
            ? "http://localhost:1234"
            : Config.LmStudio.BaseUrl.TrimEnd('/');
    }

    public override string ToolName => "lmstudio";

    public override async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        var info = new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "LM Studio (本地)",
            Capabilities = new() { "Read", "Write", "Edit", "Bash", "Git" }
        };

        try
        {
            // 检查 LM Studio API 是否可用
            var response = await _httpClient.GetAsync($"{_baseUrl}/v1/models", ct);
            if (response.IsSuccessStatusCode)
            {
                info.Installed = true;
                info.Authenticated = true;

                // 获取模型列表
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var models))
                {
                    var modelList = new List<string>();
                    foreach (var model in models.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var id))
                        {
                            modelList.Add(id.GetString() ?? "");
                        }
                    }
                    info.Version = $"{modelList.Count} models loaded";

                    // 检查配置的模型是否存在
                    if (!string.IsNullOrEmpty(Config.LmStudio.Model))
                    {
                        info.Authenticated = modelList.Any(m =>
                            m.Contains(Config.LmStudio.Model, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get LM Studio status");
            info.Installed = false;
            info.Authenticated = false;
        }

        return info;
    }

    protected override List<string> BuildArgumentList(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools,
        string? systemPromptOverride = null)
    {
        // LM Studio 仅支持 API 模式
        throw new NotSupportedException("LM Studio only supports API mode, not CLI mode.");
    }

    /// <summary>
    /// 通过 API 执行
    /// </summary>
    public new async Task<string> ExecuteAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default)
    {
        return await ExecuteViaApiAsync(message, sessionId, false, ct);
    }

    /// <summary>
    /// 通过 API 流式执行
    /// </summary>
    public new IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default)
    {
        return ExecuteViaApiStreamingAsync(message, sessionId, ct);
    }

    /// <summary>
    /// 通过 LM Studio API 执行（OpenAI 兼容格式）
    /// </summary>
    private async Task<string> ExecuteViaApiAsync(
        string message,
        string? sessionId,
        bool streaming,
        CancellationToken ct)
    {
        var systemPrompt = SkillLoader.GetSystemPrompt();
        var messages = BuildMessages(message, systemPrompt, sessionId);

        var requestBody = new
        {
            model = Config.LmStudio.Model ?? "",
            messages,
            stream = streaming,
            temperature = Config.LmStudio.Temperature,
            max_tokens = -1, // 无限制
            context_length = Config.LmStudio.ContextSize
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentElem))
            {
                return contentElem.GetString() ?? "";
            }
        }

        return "";
    }

    /// <summary>
    /// 通过 LM Studio API 流式执行
    /// </summary>
    private async IAsyncEnumerable<ProgressUpdate> ExecuteViaApiStreamingAsync(
        string message,
        string? sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var systemPrompt = SkillLoader.GetSystemPrompt();
        var messages = BuildMessages(message, systemPrompt, sessionId);

        var requestBody = new
        {
            model = Config.LmStudio.Model ?? "",
            messages,
            stream = true,
            temperature = Config.LmStudio.Temperature,
            max_tokens = -1,
            context_length = Config.LmStudio.ContextSize
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var fullMessage = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) break;

            var update = ParseLmStudioLine(line, fullMessage);
            if (update != null)
            {
                yield return update;
            }

            // 检查是否结束
            if (line.Trim() == "data: [DONE]")
            {
                break;
            }
        }

        yield return new ProgressUpdate
        {
            Message = fullMessage.ToString(),
            Status = TaskStatus.Completed,
            Progress = 100
        };
    }

    /// <summary>
    /// 解析 LM Studio 流式行（SSE 格式）
    /// </summary>
    private ProgressUpdate? ParseLmStudioLine(string line, StringBuilder fullMessage)
    {
        // SSE 格式：data: {...}
        if (!line.StartsWith("data: ")) return null;

        var data = line["data: ".Length..].Trim();
        if (data == "[DONE]") return null;

        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentElem))
                {
                    var chunk = contentElem.GetString();
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        fullMessage.Append(chunk);
                        return new ProgressUpdate
                        {
                            Message = fullMessage.ToString(),
                            Status = TaskStatus.Running,
                            Logs = new() { chunk }
                        };
                    }
                }
            }
        }
        catch (JsonException)
        {
            // 跳过无效的 JSON 行
        }
        return null;
    }

    /// <summary>
    /// 构建消息列表
    /// </summary>
    private List<Dictionary<string, string>> BuildMessages(
        string message,
        string? systemPrompt,
        string? sessionId)
    {
        var messages = new List<Dictionary<string, string>>();

        // 添加系统提示
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new Dictionary<string, string>
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            });
        }

        // 如果有会话 ID，可以添加历史上下文（简化实现）
        if (!string.IsNullOrEmpty(sessionId))
        {
            // TODO: 从 ChatHistoryService 加载历史消息
        }

        // 添加用户消息
        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "user",
            ["content"] = message
        });

        return messages;
    }
}
