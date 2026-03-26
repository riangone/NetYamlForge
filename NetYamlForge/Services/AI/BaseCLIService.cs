using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;
using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 服务基类
/// </summary>
public abstract class BaseCLIService : ICLIService
{
    protected readonly ProcessExecutor Executor;
    protected readonly CliConfig Config;
    protected readonly ILogger Logger;
    protected readonly string _toolName;

    protected BaseCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        ILogger logger,
        string toolName)
    {
        Executor = executor;
        Config = config.Value;
        Logger = logger;
        _toolName = toolName;
    }

    public virtual string ToolName => _toolName;

    /// <summary>
    /// 実際に起動するコマンドパス。
    /// サブクラスで設定ファイルのパスを返すようオーバーライドできる。
    /// デフォルトは ToolName（PATH から検索）。
    /// </summary>
    protected virtual string CommandPath => ToolName;

    /// <summary>
    /// プロセス起動時に設定する環境変数。
    /// サブクラスで API キー等を返すようオーバーライドできる。
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string>? GetEnvironmentVariables() => null;

    public abstract Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default);

    public async IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var args = BuildArguments(message, true, sessionId, allowedTools);
        var workingDir = workingDirectory ?? Config.DefaultWorkingDirectory;

        await foreach (var line in Executor.ExecuteStreamingAsync(CommandPath, args, workingDir, GetEnvironmentVariables(), ct))
        {
            // 解析 stream-json 输出
            var update = ParseStreamLine(line);
            if (update != null)
            {
                yield return update;
            }
        }
    }
    
    public async Task<string> ExecuteAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        CancellationToken ct = default)
    {
        var args = BuildArguments(message, false, sessionId, allowedTools);
        var workingDir = workingDirectory ?? Config.DefaultWorkingDirectory;

        var result = await Executor.ExecuteAsync(CommandPath, args, workingDir, GetEnvironmentVariables(), ct);
        
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"CLI failed: {result.Error}");
        }
        
        return result.Output;
    }
    
    public async Task CancelAsync(int processId, CancellationToken ct = default)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct);
                Logger.LogInformation("CLI process {Pid} killed", processId);
            }
        }
        catch (ArgumentException)
        {
            // 进程已不存在
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to kill CLI process {Pid}", processId);
        }
    }
    
    /// <summary>
    /// 构建 CLI 参数（由子类实现）
    /// </summary>
    protected abstract string BuildArguments(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools);
    
    /// <summary>
    /// 解析流式输出行。返回 null 时调用方跳过该行（不产生日志）。
    /// </summary>
    protected virtual ProgressUpdate? ParseStreamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return null;

            return typeProp.GetString() switch
            {
                "result"           => ParseResultMessage(root),
                "assistant"        => ParseAssistantMessage(root),
                "system"           => ParseSystemMessage(root),
                "progress"         => ParseProgressMessage(root),
                "error"            => ParseErrorMessage(root),
                // user / rate_limit_event / その他 → スキップ（生 JSON を出さない）
                _                  => null
            };
        }
        catch (JsonException)
        {
            // 非 JSON テキストはそのままログに出力（CLIの補助メッセージ等）
            return new ProgressUpdate { Logs = new() { line }, Status = TaskStatus.Running };
        }
    }

    private static ProgressUpdate ParseResultMessage(JsonElement root)
    {
        string? text = null;
        if (root.TryGetProperty("result", out var r))
            text = r.ValueKind == JsonValueKind.String ? r.GetString() : null;

        return new ProgressUpdate { Message = text, Progress = 100, Status = TaskStatus.Completed };
    }

    private static ProgressUpdate ParseProgressMessage(JsonElement root)
    {
        return new ProgressUpdate
        {
            Message = root.TryGetProperty("message", out var m) ? m.GetString() : null,
            Progress = root.TryGetProperty("percentage", out var p) ? p.GetInt32() : 50,
            Status = TaskStatus.Running
        };
    }

    private static ProgressUpdate ParseErrorMessage(JsonElement root)
    {
        return new ProgressUpdate
        {
            Message = root.TryGetProperty("error", out var e) ? e.GetString() : "Unknown error",
            Status = TaskStatus.Failed
        };
    }

    /// <summary>
    /// assistant メッセージを解析し、text/tool_use のみ人間が読めるログとして返す。
    /// thinking ブロックや不明なコンテンツは無視する（生 JSON を出力しない）。
    /// Claude Code / Qwen Code 共通形式：message.content 配列
    /// </summary>
    private static ProgressUpdate ParseAssistantMessage(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg)) return new ProgressUpdate { Status = TaskStatus.Running };
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return new ProgressUpdate { Status = TaskStatus.Running };

        return ParseContentArray(content);
    }

    /// <summary>
    /// content 配列を解析
    /// </summary>
    private static ProgressUpdate ParseContentArray(JsonElement content)
    {
        var textParts = new List<string>();

        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType)) continue;

            switch (itemType.GetString())
            {
                case "text":
                    if (item.TryGetProperty("text", out var text))
                    {
                        var txt = text.GetString();
                        if (!string.IsNullOrWhiteSpace(txt))
                            textParts.Add(txt);
                    }
                    break;

                case "tool_use":
                    var toolName = item.TryGetProperty("name", out var n) ? n.GetString() ?? "tool" : "tool";
                    var hint = item.TryGetProperty("input", out var inp) ? GetToolInputHint(toolName, inp) : "";
                    return new ProgressUpdate { Logs = new() { $"🔧 {toolName}{hint}" }, Status = TaskStatus.Running };

                // "thinking" → intentionally skipped (no log)
            }
        }

        if (textParts.Count > 0)
        {
            var combinedText = string.Join("\n", textParts);
            return new ProgressUpdate { Message = combinedText, Logs = new() { combinedText }, Status = TaskStatus.Running };
        }

        return new ProgressUpdate { Status = TaskStatus.Running };
    }

    private static ProgressUpdate ParseSystemMessage(JsonElement root)
    {
        var parts = new List<string>();
        if (root.TryGetProperty("model", out var model)) parts.Add($"model: {model.GetString()}");
        if (root.TryGetProperty("tools", out var tools)) parts.Add($"tools: {tools.GetArrayLength()}");
        if (root.TryGetProperty("session_id", out var sid)) parts.Add($"session: {sid.GetString()?[..Math.Min(8, sid.GetString()?.Length ?? 0)]}…");

        var summary = parts.Count > 0 ? string.Join(", ", parts) : "initialized";
        return new ProgressUpdate { Logs = new() { $"⚙ {summary}" }, Status = TaskStatus.Running };
    }

    private static string GetToolInputHint(string toolName, JsonElement input)
    {
        try
        {
            switch (toolName.ToLowerInvariant())
            {
                case "bash":
                case "execute_command":
                    if (input.TryGetProperty("command", out var cmd))
                        return $": {cmd.GetString()?.Split('\n')[0]?.Trim()}";
                    break;
                case "read":
                case "readfile":
                    if (input.TryGetProperty("file_path", out var fp))
                        return $": {fp.GetString()}";
                    break;
                case "write":
                case "writefile":
                    if (input.TryGetProperty("file_path", out var fp2))
                        return $": {fp2.GetString()}";
                    break;
                case "edit":
                    if (input.TryGetProperty("file_path", out var fp3))
                        return $": {fp3.GetString()}";
                    break;
                case "grep":
                    if (input.TryGetProperty("pattern", out var pat))
                        return $": {pat.GetString()}";
                    break;
            }
        }
        catch { /* ignore */ }
        return "";
    }
}
