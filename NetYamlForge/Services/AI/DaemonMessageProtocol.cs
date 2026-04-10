using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 常驻进程消息协议适配器
/// 处理不同 CLI 工具（Qwen Code, Claude Code 等）的 stdin/stdout JSON 格式
/// </summary>
public class DaemonMessageProtocol
{
    private readonly string _provider;
    private readonly ILogger? _logger;

    private DaemonMessageProtocol(string provider, ILogger? logger = null)
    {
        _provider = provider;
        _logger = logger;
    }

    /// <summary>
    /// 为指定提供者创建协议适配器
    /// </summary>
    public static DaemonMessageProtocol ForProvider(string provider, ILogger? logger = null)
    {
        return new DaemonMessageProtocol(provider.ToLowerInvariant(), logger);
    }

    /// <summary>
    /// 格式化 stdin 请求消息
    /// </summary>
    public string FormatRequest(
        string message,
        string? sessionId = null,
        string? systemPromptOverride = null,
        List<string>? allowedTools = null)
    {
        return _provider switch
        {
            "qwen" or "qwen-code" or "qwen_code" => FormatQwenRequest(message, sessionId, systemPromptOverride, allowedTools),
            "claude" or "claude-code" or "claude_code" => FormatClaudeRequest(message, sessionId, systemPromptOverride, allowedTools),
            _ => FormatGenericRequest(message, sessionId)
        };
    }

    /// <summary>
    /// Qwen Code 协议格式
    /// </summary>
    private static string FormatQwenRequest(
        string message,
        string? sessionId,
        string? systemPromptOverride,
        List<string>? allowedTools)
    {
        var obj = new Dictionary<string, object?>
        {
            ["type"] = "message",
            ["content"] = message
        };

        if (!string.IsNullOrEmpty(sessionId))
            obj["session_id"] = sessionId;

        if (!string.IsNullOrEmpty(systemPromptOverride))
            obj["system_prompt"] = systemPromptOverride;

        if (allowedTools != null && allowedTools.Count > 0)
            obj["allowed_tools"] = allowedTools;

        return JsonSerializer.Serialize(obj);
    }

    /// <summary>
    /// Claude Code 协议格式
    /// </summary>
    private static string FormatClaudeRequest(
        string message,
        string? sessionId,
        string? systemPromptOverride,
        List<string>? allowedTools)
    {
        var messageObj = new Dictionary<string, object?>
        {
            ["content"] = message
        };

        var obj = new Dictionary<string, object?>
        {
            ["type"] = "user",
            ["message"] = messageObj
        };

        if (!string.IsNullOrEmpty(sessionId))
            obj["session_id"] = sessionId;

        if (!string.IsNullOrEmpty(systemPromptOverride))
            obj["system_prompt"] = systemPromptOverride;

        if (allowedTools != null && allowedTools.Count > 0)
            obj["allowed_tools"] = allowedTools;

        return JsonSerializer.Serialize(obj);
    }

    /// <summary>
    /// 通用协议格式（回退）
    /// </summary>
    private static string FormatGenericRequest(string message, string? sessionId)
    {
        var obj = new Dictionary<string, object?>
        {
            ["type"] = "message",
            ["content"] = message
        };

        if (!string.IsNullOrEmpty(sessionId))
            obj["session_id"] = sessionId;

        return JsonSerializer.Serialize(obj);
    }

    /// <summary>
    /// 判断消息类型是否为部分响应（流式更新）
    /// </summary>
    public bool IsPartialResponse(string? msgType)
    {
        return msgType switch
        {
            "assistant" => true,
            "system" => true,
            "progress" => true,
            "tool_use" => true,
            "thinking" => true,
            _ => false
        };
    }

    /// <summary>
    /// 判断消息类型是否为完整响应（结束信号）
    /// </summary>
    public bool IsResponseComplete(string? msgType, JsonElement root)
    {
        if (msgType == "result")
            return true;

        if (msgType == "error")
            return true;

        // Qwen Code: 检查是否有 result 字段
        if (root.TryGetProperty("result", out var resultEl) &&
            resultEl.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(resultEl.GetString()))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从响应 JSON 中提取最终结果文本
    /// </summary>
    public string ExtractResult(JsonElement root)
    {
        // 策略 1: 直接 result 字段
        if (root.TryGetProperty("result", out var resultEl) &&
            resultEl.ValueKind == JsonValueKind.String)
        {
            var text = resultEl.GetString();
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        // 策略 2: assistant 消息的 content 数组
        if (root.TryGetProperty("message", out var msgEl) &&
            msgEl.TryGetProperty("content", out var contentEl) &&
            contentEl.ValueKind == JsonValueKind.Array)
        {
            var parts = ExtractTextFromContentArray(contentEl);
            if (parts.Count > 0)
                return string.Join("\n", parts);
        }

        // 策略 3: 直接 content 字段（字符串）
        if (root.TryGetProperty("content", out var contentEl2) &&
            contentEl2.ValueKind == JsonValueKind.String)
        {
            var text = contentEl2.GetString();
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        // 策略 4: 回退到整个 JSON
        return root.GetRawText();
    }

    /// <summary>
    /// 从 content 数组提取文本
    /// </summary>
    private List<string> ExtractTextFromContentArray(JsonElement contentArray)
    {
        var parts = new List<string>();

        foreach (var item in contentArray.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeEl)) continue;

            var itemType = typeEl.GetString();
            switch (itemType)
            {
                case "text":
                    if (item.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == JsonValueKind.String)
                    {
                        var text = textEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                            parts.Add(text);
                    }
                    break;

                case "tool_use":
                    // 工具调用不计入结果文本
                    break;

                case "thinking":
                    // 思考块不计入结果文本
                    break;
            }
        }

        return parts;
    }

    /// <summary>
    /// 解析 stdout 消息为 ProgressUpdate
    /// </summary>
    public ProgressUpdate? ParseProgressMessage(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl))
                return null;

            var msgType = typeEl.GetString();

            return msgType switch
            {
                "result" => ParseResultMessage(root),
                "assistant" => ParseAssistantMessage(root),
                "system" => ParseSystemMessage(root),
                "progress" => ParseProgressMessageImpl(root),
                "error" => ParseErrorMessage(root),
                _ => null
            };
        }
        catch (JsonException)
        {
            return new ProgressUpdate
            {
                Logs = new() { line },
                Status = Models.AI.TaskStatus.Running
            };
        }
    }

    private static ProgressUpdate ParseResultMessage(JsonElement root)
    {
        string? text = null;
        if (root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String)
            text = r.GetString();

        string? sessionId = null;
        if (root.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String)
            sessionId = sid.GetString();

        return new ProgressUpdate
        {
            Message = text,
            Progress = 100,
            Status = Models.AI.TaskStatus.Completed,
            SessionId = sessionId
        };
    }

    private ProgressUpdate ParseAssistantMessage(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg))
            return new ProgressUpdate { Status = Models.AI.TaskStatus.Running };

        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return new ProgressUpdate { Status = Models.AI.TaskStatus.Running };

        var textParts = new List<string>();
        var toolLogs = new List<string>();

        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType)) continue;

            switch (itemType.GetString())
            {
                case "text":
                    if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var txt = text.GetString();
                        if (!string.IsNullOrWhiteSpace(txt))
                            textParts.Add(txt);
                    }
                    break;

                case "tool_use":
                    var toolName = item.TryGetProperty("name", out var n) ? n.GetString() ?? "tool" : "tool";
                    var hint = item.TryGetProperty("input", out var inp) ? GetToolInputHint(toolName, inp) : "";
                    toolLogs.Add($"🔧 {toolName}{hint}");
                    break;
            }
        }

        var combinedText = textParts.Count > 0 ? string.Join("\n", textParts) : null;

        if (combinedText != null)
        {
            return new ProgressUpdate
            {
                Message = combinedText,
                Logs = new List<string> { combinedText },
                Status = Models.AI.TaskStatus.Running
            };
        }
        else if (toolLogs.Count > 0)
        {
            return new ProgressUpdate { Logs = toolLogs, Status = Models.AI.TaskStatus.Running };
        }

        return new ProgressUpdate { Status = Models.AI.TaskStatus.Running };
    }

    private static ProgressUpdate ParseSystemMessage(JsonElement root)
    {
        var parts = new List<string>();
        if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
            parts.Add($"model: {model.GetString()}");
        if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            parts.Add($"tools: {tools.GetArrayLength()}");
        if (root.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String)
        {
            var s = sid.GetString();
            if (s != null) parts.Add($"session: {s.Substring(0, Math.Min(8, s.Length))}…");
        }

        var summary = parts.Count > 0 ? string.Join(", ", parts) : "initialized";
        return new ProgressUpdate { Logs = new() { $"⚙ {summary}" }, Status = Models.AI.TaskStatus.Running };
    }

    private static ProgressUpdate ParseProgressMessageImpl(JsonElement root)
    {
        return new ProgressUpdate
        {
            Message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null,
            Progress = root.TryGetProperty("percentage", out var p) ? p.GetInt32() : 50,
            Status = Models.AI.TaskStatus.Running
        };
    }

    private static ProgressUpdate ParseErrorMessage(JsonElement root)
    {
        string? errorMsg = null;
        if (root.TryGetProperty("error", out var e))
        {
            if (e.ValueKind == JsonValueKind.String)
                errorMsg = e.GetString();
            else if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var em) && em.ValueKind == JsonValueKind.String)
                errorMsg = em.GetString();
        }

        return new ProgressUpdate
        {
            Message = errorMsg ?? "Unknown error",
            Status = Models.AI.TaskStatus.Failed
        };
    }

    private static string GetToolInputHint(string toolName, JsonElement input)
    {
        try
        {
            if (input.ValueKind != JsonValueKind.Object)
                return "";

            switch (toolName.ToLowerInvariant())
            {
                case "bash":
                case "execute_command":
                    if (input.TryGetProperty("command", out var cmd) && cmd.ValueKind == JsonValueKind.String)
                        return $": {cmd.GetString()?.Split('\n')[0]?.Trim()}";
                    break;
                case "read":
                case "readfile":
                    if (input.TryGetProperty("file_path", out var fp) && fp.ValueKind == JsonValueKind.String)
                        return $": {fp.GetString()}";
                    break;
                case "write":
                case "writefile":
                    if (input.TryGetProperty("file_path", out var fp2) && fp2.ValueKind == JsonValueKind.String)
                        return $": {fp2.GetString()}";
                    break;
                case "edit":
                    if (input.TryGetProperty("file_path", out var fp3) && fp3.ValueKind == JsonValueKind.String)
                        return $": {fp3.GetString()}";
                    break;
            }
        }
        catch { }
        return "";
    }
}
