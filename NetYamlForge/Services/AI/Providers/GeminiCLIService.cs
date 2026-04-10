using System.Text.Json;
using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;

namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// Google Gemini CLI 服务
/// </summary>
public class GeminiCLIService : BaseCLIService
{
    public GeminiCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger<GeminiCLIService> logger)
        : base(executor, config, skillLoader, logger, "gemini")
    {
    }

    // appsettings に Gemini.Path が設定されている場合はそのパスを使用する
    protected override string CommandPath =>
        string.IsNullOrEmpty(Config.Gemini.Path) ? ToolName : Config.Gemini.Path;

    // GOOGLE_API_KEY / GOOGLE_GENAI_DEVELOPER_MODE を環境変数として渡す
    protected override IReadOnlyDictionary<string, string>? GetEnvironmentVariables()
    {
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(Config.Gemini.ApiKey))
            env["GOOGLE_API_KEY"] = Config.Gemini.ApiKey;

        if (!string.IsNullOrEmpty(Config.Gemini.ProjectId))
            env["GOOGLE_CLOUD_PROJECT"] = Config.Gemini.ProjectId;

        if (Config.Gemini.DeveloperMode)
            env["GOOGLE_GENAI_DEVELOPER_MODE"] = "true";

        return env.Count > 0 ? env : null;
    }

    /// <summary>
    /// Gemini CLI の出力形式を解析（独自フォーマット対応）
    /// </summary>
    protected override ProgressUpdate? ParseStreamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            Logger.LogDebug("Gemini received JSON: {Json}", line);

            if (!root.TryGetProperty("type", out var typeProp))
                return null;

            return typeProp.GetString() switch
            {
                // {"type":"message","role":"assistant","content":"text","delta":true}
                "message"  => ParseGeminiMessage(root),
                // {"type":"result","status":"success","stats":{...}}
                "result"   => ParseGeminiResult(root),
                // {"type":"init","session_id":"...","model":"..."}
                "init"     => ParseGeminiInit(root),
                _          => null
            };
        }
        catch (JsonException ex)
        {
            // 非 JSON テキスト（Gemini CLI の起動メッセージ等）はログのみ、チャットには出力しない
            Logger.LogDebug(ex, "Non-JSON line from Gemini (ignored): {Line}", line);
            return null;
        }
    }

    private static ProgressUpdate? ParseGeminiMessage(JsonElement root)
    {
        // role が "assistant" のメッセージのみ処理する（user エコーはスキップ）
        if (!root.TryGetProperty("role", out var roleProp) ||
            roleProp.GetString() != "assistant")
            return null;

        if (!root.TryGetProperty("content", out var contentProp))
            return null;

        // content は文字列（Gemini CLI の実際のフォーマット）
        if (contentProp.ValueKind == JsonValueKind.String)
        {
            var text = contentProp.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                return new ProgressUpdate { Message = text, Logs = new() { text }, Status = TaskStatus.Running };
        }

        return null;
    }

    private static ProgressUpdate ParseGeminiResult(JsonElement root)
    {
        // {"type":"result","status":"success"/"error","stats":{...},"session_id":"..."}
        // result テキストフィールドは存在しないため Message = null
        string? sessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString() : null;

        if (root.TryGetProperty("status", out var statusProp))
        {
            var status = statusProp.GetString();
            if (status == "error" || status == "failed")
            {
                var errMsg = root.TryGetProperty("error", out var e) ? e.GetString() : "Gemini CLI error";
                return new ProgressUpdate { Message = errMsg, Status = TaskStatus.Failed };
            }
        }

        return new ProgressUpdate { Message = null, Progress = 100, Status = TaskStatus.Completed, SessionId = sessionId };
    }

    private static ProgressUpdate ParseGeminiInit(JsonElement root)
    {
        // {"type":"init","model":"...","session_id":"..."}
        var parts = new List<string>();
        if (root.TryGetProperty("model", out var model)) parts.Add($"model: {model.GetString()}");
        if (root.TryGetProperty("session_id", out var sid)) parts.Add($"session: {sid.GetString()?[..Math.Min(8, sid.GetString()?.Length ?? 0)]}…");
        var summary = parts.Count > 0 ? string.Join(", ", parts) : "initialized";
        return new ProgressUpdate { Logs = new() { $"⚙ {summary}" }, Status = TaskStatus.Running };
    }

    public override async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        var info = new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "Google Gemini CLI",
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

                // API キーが設定されていれば認証済みとみなす
                if (!string.IsNullOrEmpty(Config.Gemini.ApiKey))
                {
                    info.Authenticated = true;
                }
                else
                {
                    // CLI 認証確認（qwen/claude と同じロジック）
                    var authResult = await Executor.ExecuteAsync(
                        CommandPath,
                        new[] { "-p", "Hello", "--output-format", "json" },
                        environmentVariables: GetEnvironmentVariables(),
                        ct: ct);
                    
                    info.Authenticated = authResult.ExitCode == 0 &&
                        !authResult.Error.Contains("auth", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get Gemini CLI info");
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

        // ─────────────────────────────────────────────────────────
        // チャットモード判定：systemPromptOverride あり＋ツールなし＝チャット用途
        // ─────────────────────────────────────────────────────────
        var isChatMode = !string.IsNullOrEmpty(systemPromptOverride)
                         && (allowedTools == null || allowedTools.Count == 0);

        // Gemini CLI: gemini --yolo --prompt <message> [options]
        // --yolo (-y): 自動実行モード（確認不要）
        // Gemini CLI は --system-prompt 未対応のため、システムプロンプトはメッセージ先頭に埋め込む
        var geminiPrompt = !string.IsNullOrEmpty(systemPromptOverride)
            ? $"{systemPromptOverride}\n\n---\n\n{message}"
            : message;

        args.Add("--yolo");
        args.Add("--prompt");
        args.Add(geminiPrompt);

        // 出力フォーマット
        args.Add("--output-format");
        args.Add(streaming ? "stream-json" : "json");

        // モデル指定（設定されている場合）
        if (!string.IsNullOrEmpty(Config.Gemini.Model))
        {
            args.Add("--model");
            args.Add(Config.Gemini.Model);
        }

        // セッション再開（チャットモード以外）
        if (!isChatMode && !string.IsNullOrEmpty(sessionId))
        {
            args.Add("--resume");
            args.Add(sessionId);
        }

        // ツール権限制御（チャットモード以外）
        if (!isChatMode && allowedTools != null && allowedTools.Count > 0)
        {
            args.Add("--allowed-tools");
            args.Add(string.Join(",", allowedTools));
        }

        return args;
    }
}
