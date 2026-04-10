using System.Text.Json;
using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;

namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// OpenAI Codex CLI 服务
/// </summary>
public class CodexCLIService : BaseCLIService
{
    public CodexCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger<CodexCLIService> logger)
        : base(executor, config, skillLoader, logger, "codex")
    {
    }

    // appsettings に Codex.Path が設定されている場合はそのパスを使用する
    protected override string CommandPath =>
        string.IsNullOrEmpty(Config.Codex.Path) ? ToolName : Config.Codex.Path;

    // OPENAI_API_KEY / OPENAI_BASE_URL を環境変数として渡す
    protected override IReadOnlyDictionary<string, string>? GetEnvironmentVariables()
    {
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(Config.Codex.ApiKey))
            env["OPENAI_API_KEY"] = Config.Codex.ApiKey;

        if (!string.IsNullOrEmpty(Config.Codex.BaseUrl))
            env["OPENAI_BASE_URL"] = Config.Codex.BaseUrl;

        if (!string.IsNullOrEmpty(Config.Codex.Organization))
            env["OPENAI_ORG_ID"] = Config.Codex.Organization;

        return env.Count > 0 ? env : null;
    }

    /// <summary>
    /// Codex CLI の出力形式を解析（独自フォーマット対応）
    /// </summary>
    protected override ProgressUpdate? ParseStreamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            Logger.LogDebug("Codex received JSON: {Json}", line);

            if (!root.TryGetProperty("type", out var typeProp))
                return null;

            return typeProp.GetString() switch
            {
                // {"type":"thread.started","thread_id":"..."}
                "thread.started"  => ParseCodexThreadStarted(root),
                // {"type":"agent_message","content":[{"type":"output_text","text":"..."}]}
                // または {"type":"message","role":"assistant","content":[...]}
                "agent_message"   => ParseCodexAgentMessage(root),
                "message"         => ParseCodexMessageEvent(root),
                // {"type":"turn.completed"} / {"type":"thread.stopped"}
                "turn.completed"  => new ProgressUpdate { Progress = 100, Status = TaskStatus.Completed },
                "thread.stopped"  => new ProgressUpdate { Progress = 100, Status = TaskStatus.Completed },
                // {"type":"error","message":"..."} / {"type":"turn.failed","error":{"message":"..."}}
                "error"           => ParseCodexError(root),
                "turn.failed"     => ParseCodexTurnFailed(root),
                // その他（turn.started, local_shell_call 等）はスキップ
                _                 => null
            };
        }
        catch (JsonException ex)
        {
            Logger.LogDebug(ex, "Non-JSON line from Codex (ignored): {Line}", line);
            return null;
        }
    }

    private static ProgressUpdate ParseCodexThreadStarted(JsonElement root)
    {
        var threadId = root.TryGetProperty("thread_id", out var tid) ? tid.GetString() : null;
        var hint = threadId != null ? $"session: {threadId[..Math.Min(8, threadId.Length)]}…" : "started";
        return new ProgressUpdate { Logs = new() { $"⚙ {hint}" }, Status = TaskStatus.Running };
    }

    private static ProgressUpdate? ParseCodexAgentMessage(JsonElement root)
    {
        // {"type":"agent_message","content":[{"type":"output_text","text":"..."}]}
        if (!root.TryGetProperty("content", out var content))
            return null;

        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                return new ProgressUpdate { Message = text, Logs = new() { text }, Status = TaskStatus.Running };
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                var itemType = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                if ((itemType == "output_text" || itemType == "text") &&
                    item.TryGetProperty("text", out var txt))
                {
                    var s = txt.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
                }
            }
            if (parts.Count > 0)
            {
                var combined = string.Join("\n", parts);
                return new ProgressUpdate { Message = combined, Logs = new() { combined }, Status = TaskStatus.Running };
            }
        }

        return null;
    }

    private static ProgressUpdate? ParseCodexMessageEvent(JsonElement root)
    {
        // role が "assistant" のみ処理
        if (!root.TryGetProperty("role", out var role) || role.GetString() != "assistant")
            return null;
        return ParseCodexAgentMessage(root);
    }

    private static ProgressUpdate ParseCodexError(JsonElement root)
    {
        var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "Codex error";
        return new ProgressUpdate { Message = msg, Status = TaskStatus.Failed };
    }

    private static ProgressUpdate ParseCodexTurnFailed(JsonElement root)
    {
        string? msg = null;
        if (root.TryGetProperty("error", out var err))
            msg = err.TryGetProperty("message", out var m) ? m.GetString() : err.GetString();
        return new ProgressUpdate { Message = msg ?? "Codex turn failed", Status = TaskStatus.Failed };
    }

    public override async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        var info = new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "OpenAI Codex",
            Capabilities = new() { "Read", "Write", "Edit", "Bash", "Git", "Web" },
            Installed = false,
            Authenticated = false
        };

        try
        {
            // バージョン確認
            var result = await Executor.ExecuteAsync(CommandPath,
                new[] { "--version" },
                environmentVariables: GetEnvironmentVariables(), ct: ct);
            
            Logger.LogInformation("Codex version check: ExitCode={ExitCode}, Output={Output}", 
                result.ExitCode, result.Output);
            
            if (result.ExitCode == 0)
            {
                info.Installed = true;
                info.Version = result.Output.Trim();

                // API キーが設定されていれば認証済みとみなす
                if (!string.IsNullOrEmpty(Config.Codex.ApiKey))
                {
                    info.Authenticated = true;
                    Logger.LogInformation("Codex authenticated via API key configuration");
                }
                else
                {
                    // codex exec --help で認証確認（ExitCode 0 なら認証済み）
                    var authResult = await Executor.ExecuteAsync(
                        CommandPath,
                        new[] { "exec", "--help" },
                        environmentVariables: GetEnvironmentVariables(),
                        ct: ct);

                    Logger.LogInformation("Codex auth check: ExitCode={ExitCode}, Error={Error}", 
                        authResult.ExitCode, authResult.Error);

                    // help が表示できれば認証済み（auth エラーが出ていなければ OK）
                    info.Authenticated = authResult.ExitCode == 0 ||
                        (authResult.Error != null &&
                         !authResult.Error.Contains("not authenticated", StringComparison.OrdinalIgnoreCase) &&
                         !authResult.Error.Contains("login required", StringComparison.OrdinalIgnoreCase));
                }
            }
            else
            {
                Logger.LogWarning("Codex version check failed: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to get Codex CLI info");
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

        // Codex CLI: codex exec [options] <prompt>
        args.Add("exec");

        // Git リポジトリチェックをスキップ
        args.Add("--skip-git-repo-check");

        // 自動実行モード（workspace-write サンドボックス + 自動承認）
        args.Add("--full-auto");

        // JSONL 出力（--output-format は存在しない。streaming/non-streaming 共通で --json を使用）
        args.Add("--json");

        // モデル指定（設定されている場合）
        if (!string.IsNullOrEmpty(Config.Codex.Model))
        {
            args.Add("--model");
            args.Add(Config.Codex.Model);
        }

        // セッション再開（exec resume サブコマンド経由）
        // Note: --resume フラグは存在しない。再開は "codex exec resume <id>" サブコマンド。
        // ここでは sessionId が指定されていてもシンプルな exec として扱う（マルチターン非対応）。

        // Codex exec は --instructions フラグ未対応のため、システムプロンプトはメッセージ先頭に埋め込む
        // メッセージは位置引数として渡す（stdin ではなく引数として渡すことで stdin EOF 問題を回避）
        var codexMessage = !string.IsNullOrEmpty(systemPromptOverride)
            ? $"{systemPromptOverride}\n\n---\n\n{message}"
            : message;
        args.Add(codexMessage);

        return args;
    }
}
