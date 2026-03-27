using System.Text.Json;
using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;

namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// GitHub Copilot CLI 服务
/// </summary>
public class CopilotCLIService : BaseCLIService
{
    public CopilotCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger<CopilotCLIService> logger)
        : base(executor, config, skillLoader, logger, "copilot")
    {
    }

    // appsettings に Copilot.Path が設定されている場合はそのパスを使用する
    protected override string CommandPath =>
        string.IsNullOrEmpty(Config.Copilot.Path) ? ToolName : Config.Copilot.Path;

    // GITHUB_COPILOT_TOKEN を環境変数として渡す
    protected override IReadOnlyDictionary<string, string>? GetEnvironmentVariables()
    {
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(Config.Copilot.Token))
            env["GITHUB_COPILOT_TOKEN"] = Config.Copilot.Token;

        return env.Count > 0 ? env : null;
    }

    /// <summary>
    /// Copilot CLI の出力形式を解析
    /// </summary>
    protected override ProgressUpdate? ParseStreamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            Logger.LogDebug("Copilot received JSON: {Json}", line);

            // response フィールドをチェック
            if (root.TryGetProperty("response", out var responseProp) && responseProp.ValueKind == JsonValueKind.String)
            {
                var text = responseProp.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new ProgressUpdate { Message = text, Logs = new() { text }, Status = TaskStatus.Running };
                }
            }

            // output フィールドをチェック
            if (root.TryGetProperty("output", out var outputProp) && outputProp.ValueKind == JsonValueKind.String)
            {
                var text = outputProp.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new ProgressUpdate { Message = text, Logs = new() { text }, Status = TaskStatus.Running };
                }
            }

            // 完了ステータスをチェック
            if (root.TryGetProperty("status", out var statusProp))
            {
                var status = statusProp.GetString();
                if (status == "completed" || status == "success" || status == "done")
                {
                    return new ProgressUpdate
                    {
                        Message = root.TryGetProperty("content", out var c) ? c.GetString() : null,
                        Progress = 100,
                        Status = TaskStatus.Completed,
                        SessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString() : null
                    };
                }
                else if (status == "error" || status == "failed")
                {
                    return new ProgressUpdate
                    {
                        Message = root.TryGetProperty("error", out var e) ? e.GetString() : "Unknown error",
                        Status = TaskStatus.Failed
                    };
                }
            }

            // 標準的な type フィールドもサポート
            if (!root.TryGetProperty("type", out var typeProp))
            {
                return null;
            }

            return typeProp.GetString() switch
            {
                "result"    => ParseResultMessage(root),
                "assistant" => ParseAssistantMessagePublic(root),
                "system"    => ParseSystemMessage(root),
                "progress"  => ParseProgressMessage(root),
                "error"     => ParseErrorMessage(root),
                _           => null
            };
        }
        catch (JsonException ex)
        {
            Logger.LogDebug(ex, "Non-JSON line received: {Line}", line);
            return new ProgressUpdate { Logs = new() { line }, Status = TaskStatus.Running };
        }
    }

    /// <summary>
    /// assistant メッセージを解析（BaseCLIService の ParseAssistantMessage をコピー）
    /// </summary>
    private static ProgressUpdate ParseAssistantMessagePublic(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg)) return new ProgressUpdate { Status = TaskStatus.Running };
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return new ProgressUpdate { Status = TaskStatus.Running };

        return ParseContentArrayPublic(content);
    }

    /// <summary>
    /// content 配列を解析
    /// </summary>
    private static ProgressUpdate ParseContentArrayPublic(JsonElement content)
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
                    var hint = item.TryGetProperty("input", out var inp) ? GetToolInputHintPublic(toolName, inp) : "";
                    return new ProgressUpdate { Logs = new() { $"🔧 {toolName}{hint}" }, Status = TaskStatus.Running };
            }
        }

        if (textParts.Count > 0)
        {
            var combinedText = string.Join("\n", textParts);
            return new ProgressUpdate { Message = combinedText, Logs = new() { combinedText }, Status = TaskStatus.Running };
        }

        return new ProgressUpdate { Status = TaskStatus.Running };
    }

    /// <summary>
    /// ツール入力のヒントを取得
    /// </summary>
    private static string GetToolInputHintPublic(string toolName, JsonElement input)
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

    public override async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        var info = new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "GitHub Copilot CLI",
            Capabilities = new() { "Read", "Write", "Edit", "Bash", "Git", "Web" }
        };

        try
        {
            var result = await Executor.ExecuteAsync(CommandPath,
                new[] { "--version" },
                environmentVariables: GetEnvironmentVariables(), ct: ct);

            if (result.ExitCode == 0)
            {
                info.Installed = true;
                info.Version = result.Output.Trim();

                // トークンが設定されていれば認証済みとみなす
                if (!string.IsNullOrEmpty(Config.Copilot.Token))
                {
                    info.Authenticated = true;
                }
                else
                {
                    // CLI 認証確認
                    var authResult = await Executor.ExecuteAsync(
                        CommandPath,
                        new[] { "--help" },
                        environmentVariables: GetEnvironmentVariables(),
                        ct: ct);

                    info.Authenticated = authResult.ExitCode == 0 &&
                        !authResult.Error.Contains("auth", StringComparison.OrdinalIgnoreCase) &&
                        !authResult.Error.Contains("login", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get Copilot CLI info");
            info.Installed = false;
            info.Authenticated = false;
        }

        return info;
    }

    protected override List<string> BuildArgumentList(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools)
    {
        var args = new List<string>();

        // Copilot CLI: copilot <prompt>
        // 基本的にはメッセージをそのまま渡す

        // 出力フォーマット
        if (streaming)
        {
            args.Add("--stream");
            args.Add("--output-format");
            args.Add("stream-json");
        }
        else
        {
            args.Add("--output-format");
            args.Add("json");
        }

        // フレームワーク固有のシステムプロンプトを追加
        var systemPrompt = SkillLoader.GetSystemPrompt();
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            args.Add("--system-prompt");
            args.Add(systemPrompt);
        }

        // セッション再開
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Add("--resume");
            args.Add(sessionId);
        }

        // ツール権限制御
        if (allowedTools != null && allowedTools.Count > 0)
        {
            args.Add("--allowed-tools");
            args.Add(string.Join(",", allowedTools));
        }

        // メッセージは最後に追加（位置引数として扱われる）
        args.Add(message);

        return args;
    }
}
