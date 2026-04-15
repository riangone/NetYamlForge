using System.Net.Http;
using System.Text;
using System.Text.Json;
using TaskStatus = NetYamlForge.AI.Models.TaskStatus;
using NetYamlForge.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetYamlForge.AI.Services.Providers;

/// <summary>
/// Ollama CLI サービス
/// 支持通过 HTTP API 或 CLI 命令与本地 Ollama 服务交互
/// </summary>
public class OllamaCLIService : BaseCLIService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public OllamaCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger<OllamaCLIService> logger,
        HttpClient? httpClient = null)
        : base(executor, config, skillLoader, logger, "ollama")
    {
        _httpClient = httpClient ?? new HttpClient();
        _baseUrl = string.IsNullOrEmpty(Config.Ollama.BaseUrl)
            ? "http://localhost:11434"
            : Config.Ollama.BaseUrl.TrimEnd('/');
    }

    protected override string CommandPath =>
        string.IsNullOrEmpty(Config.Ollama.Path) ? ToolName : Config.Ollama.Path;

    public override async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        var info = new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "Ollama (本地)",
            Capabilities = new() { "Read", "Write", "Edit", "Bash", "Git" }
        };

        try
        {
            // 检查 Ollama API 是否可用
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", ct);
            if (response.IsSuccessStatusCode)
            {
                info.Installed = true;
                info.Authenticated = true;

                // 获取模型列表
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var models))
                {
                    var modelList = new List<string>();
                    foreach (var model in models.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var name))
                        {
                            modelList.Add(name.GetString() ?? "");
                        }
                    }
                    info.Version = $"{modelList.Count} models available";
                    
                    // 检查配置的模型是否存在
                    if (!string.IsNullOrEmpty(Config.Ollama.Model))
                    {
                        info.Authenticated = modelList.Any(m => 
                            m.Contains(Config.Ollama.Model, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            else
            {
                // API 不可用，尝试 CLI 方式
                var result = await Executor.ExecuteAsync(CommandPath, new[] { "--version" }, ct: ct);
                if (result.ExitCode == 0)
                {
                    info.Installed = true;
                    info.Version = result.Output.Trim();
                    info.Authenticated = true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get Ollama status");
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
        // CLI 模式（当 UseApi = false 时）
        var args = new List<string>();
        args.Add("run");
        
        if (!string.IsNullOrEmpty(Config.Ollama.Model))
        {
            args.Add(Config.Ollama.Model);
        }
        else
        {
            args.Add("qwen2.5-coder"); // 默认模型
        }

        args.Add(message);
        return args;
    }

    /// <summary>
    /// 通过 API 或 CLI 执行
    /// </summary>
    public new async Task<string> ExecuteAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default)
    {
        if (Config.Ollama.UseApi)
        {
            return await ExecuteViaApiAsync(message, sessionId, false, ct);
        }
        else
        {
            return await base.ExecuteAsync(message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct);
        }
    }

    /// <summary>
    /// 通过 API 或 CLI 流式执行
    /// </summary>
    public new IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default)
    {
        if (Config.Ollama.UseApi)
        {
            return ExecuteViaApiStreamingAsync(message, sessionId, ct);
        }
        else
        {
            return base.ExecuteStreamingAsync(message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct);
        }
    }

    /// <summary>
    /// 通过 Ollama API 执行（OpenAI 兼容格式）
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
            model = Config.Ollama.Model ?? "qwen2.5-coder",
            messages,
            stream = streaming,
            options = new
            {
                temperature = Config.Ollama.Temperature,
                num_ctx = Config.Ollama.ContextSize
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var endpoint = streaming ? $"{_baseUrl}/api/chat" : $"{_baseUrl}/api/generate";
        var response = await _httpClient.PostAsync(endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        if (streaming)
        {
            // 流式响应处理
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var result = new StringBuilder();
            
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) break;
                
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var contentElem))
                {
                    result.Append(contentElem.GetString());
                }
                
                if (doc.RootElement.TryGetProperty("done", out var done) && done.GetBoolean())
                {
                    break;
                }
            }
            
            return result.ToString();
        }
        else
        {
            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            
            if (doc.RootElement.TryGetProperty("response", out var responseElem))
            {
                return responseElem.GetString() ?? "";
            }
            
            // 尝试 chat 格式
            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentElem))
            {
                return contentElem.GetString() ?? "";
            }
            
            return "";
        }
    }

    /// <summary>
    /// 通过 Ollama API 流式执行
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
            model = Config.Ollama.Model ?? "qwen2.5-coder",
            messages,
            stream = true,
            options = new
            {
                temperature = Config.Ollama.Temperature,
                num_ctx = Config.Ollama.ContextSize
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var fullMessage = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) break;

            var update = ParseOllamaLine(line, fullMessage);
            if (update != null)
            {
                yield return update;
            }

            // 检查是否完成
            if (line.Contains("\"done\":true"))
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
    /// 解析 Ollama 流式行
    /// </summary>
    private ProgressUpdate? ParseOllamaLine(string line, StringBuilder fullMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentElem))
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
