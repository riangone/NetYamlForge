using System.Text.Json;
using NetYamlForge.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskStatus = NetYamlForge.AI.Models.TaskStatus;

namespace NetYamlForge.AI.Services.Providers;

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
    /// Copilot CLI stream-json 出力を解析。
    /// 実際のフォーマット（--output-format json で確認済み）:
    ///   {"type":"assistant.message","data":{"content":"text","toolRequests":[],...}}
    ///   {"type":"result","sessionId":"...","exitCode":0,"usage":{...}}
    /// ephemeral:true の行（reasoning_delta 等）はスキップする。
    /// </summary>
    protected override ProgressUpdate? ParseStreamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            Logger.LogDebug("Copilot received JSON: {Json}", line);

            // ephemeral イベント（reasoning_delta 等）はチャットに表示しない
            if (root.TryGetProperty("ephemeral", out var ephemeral) && ephemeral.GetBoolean())
                return null;

            if (!root.TryGetProperty("type", out var typeProp))
                return null;

            return typeProp.GetString() switch
            {
                // {"type":"assistant.message","data":{"content":"Hello","toolRequests":[],...}}
                "assistant.message" => ParseCopilotAssistantMessage(root),
                // {"type":"result","sessionId":"...","exitCode":0}
                "result"            => ParseCopilotResult(root),
                // その他（user.message, session.*, assistant.turn_start/end 等）はスキップ
                _                   => null
            };
        }
        catch (JsonException ex)
        {
            Logger.LogDebug(ex, "Non-JSON line from Copilot (ignored): {Line}", line);
            return null;
        }
    }

    private static ProgressUpdate? ParseCopilotAssistantMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)) return null;
        if (!data.TryGetProperty("content", out var contentProp)) return null;

        var text = contentProp.ValueKind == JsonValueKind.String ? contentProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        return new ProgressUpdate { Message = text, Logs = new() { text }, Status = TaskStatus.Running };
    }

    private static ProgressUpdate ParseCopilotResult(JsonElement root)
    {
        // {"type":"result","sessionId":"...","exitCode":0,"usage":{...}}
        var sessionId = root.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
        var exitCode = root.TryGetProperty("exitCode", out var ec) ? ec.GetInt32() : 0;

        if (exitCode != 0)
        {
            var errMsg = root.TryGetProperty("error", out var e) ? e.GetString() : "Copilot CLI error";
            return new ProgressUpdate { Message = errMsg, Status = TaskStatus.Failed };
        }

        return new ProgressUpdate { Message = null, Progress = 100, Status = TaskStatus.Completed, SessionId = sessionId };
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
        List<string>? allowedTools,
        string? systemPromptOverride = null)
    {
        var args = new List<string>();

        // Copilot CLI: copilot --prompt <message> --allow-all-tools --output-format json
        // --allow-all-tools: 非インタラクティブモードに必須
        // Copilot CLI は --system-prompt 未対応のため、システムプロンプトはメッセージ先頭に埋め込む
        var copilotPrompt = !string.IsNullOrEmpty(systemPromptOverride)
            ? $"{systemPromptOverride}\n\n---\n\n{message}"
            : message;

        args.Add("--prompt");
        args.Add(copilotPrompt);

        // --allow-all-tools: 非インタラクティブモード（--prompt）に必須
        args.Add("--allow-all-tools");

        // --output-format json: JSONL 形式で出力（stream-json は存在しない）
        args.Add("--output-format");
        args.Add("json");

        // セッション再開（--resume[=sessionId] 形式）
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Add($"--resume={sessionId}");
        }

        return args;
    }
}
