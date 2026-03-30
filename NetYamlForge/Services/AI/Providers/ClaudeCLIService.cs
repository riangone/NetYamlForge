using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// Claude Code CLI 服务
/// </summary>
public class ClaudeCLIService : BaseCLIService
{
    public ClaudeCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger<ClaudeCLIService> logger)
        : base(executor, config, skillLoader, logger, "claude")
    {
    }

    // appsettings に Claude.Path が設定されている場合はそのパスを使用する
    protected override string CommandPath =>
        string.IsNullOrEmpty(Config.Claude.Path) ? ToolName : Config.Claude.Path;

    // appsettings に Claude.ApiKey が設定されている場合は環境変数として渡す
    protected override IReadOnlyDictionary<string, string>? GetEnvironmentVariables()
    {
        if (string.IsNullOrEmpty(Config.Claude.ApiKey)) return null;
        return new Dictionary<string, string>
        {
            ["ANTHROPIC_API_KEY"] = Config.Claude.ApiKey
        };
    }

    public override async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        var info = new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "Claude Code",
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
                if (!string.IsNullOrEmpty(Config.Claude.ApiKey))
                {
                    info.Authenticated = true;
                }
                else
                {
                    // claude login の認証情報を確認
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
            Logger.LogWarning(ex, "Failed to get Claude CLI info");
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

        // Claude CLI: claude -p <prompt> [options]
        // -p / --print: 非インタラクティブモードでプロンプトを実行する。
        // メッセージは -p の直後に渡す（"prompt argument" として認識される）。
        args.Add("-p");
        args.Add(message);

        // --dangerously-skip-permissions: 権限プロンプトをスキップ
        args.Add("--dangerously-skip-permissions");

        // 出力フォーマット
        args.Add("--output-format");
        args.Add(streaming ? "stream-json" : "json");
        if (streaming) args.Add("--verbose");

        // systemPromptOverride が指定された場合: --system-prompt でペルソナを完全置換する。
        // これにより NetYamlForge 開発コンテキストが混入しない（ロール汚染防止）。
        // 指定なし: フレームワーク固有のシステムプロンプトを追記する（既存動作）。
        if (!string.IsNullOrEmpty(systemPromptOverride))
        {
            args.Add("--system-prompt");
            args.Add(systemPromptOverride);
        }
        else
        {
            var frameworkPrompt = SkillLoader.GetSystemPrompt();
            if (!string.IsNullOrEmpty(frameworkPrompt))
            {
                args.Add("--append-system-prompt");
                args.Add(frameworkPrompt);
            }
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
            args.Add("--allowedTools");
            args.Add(string.Join(",", allowedTools));
        }

        return args;
    }
}
