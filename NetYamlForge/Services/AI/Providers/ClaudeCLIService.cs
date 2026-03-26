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
            var result = await Executor.ExecuteAsync(CommandPath, "--version",
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
                        "-p \"Hello\" --output-format json",
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
    
    protected override string BuildArguments(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools)
    {
        var args = new List<string>();

        // -p 标志：非交互模式
        args.Add("-p");
        args.Add($"\"{EscapeArgument(message)}\"");

        // 输出格式
        if (streaming)
        {
            // stream-json 需要 --verbose 参数
            args.Add("--output-format stream-json");
            args.Add("--verbose");
        }
        else
        {
            args.Add("--output-format json");
        }

        // フレームワーク固有のシステムプロンプトを追加（既存のシステムプロンプトに追記）
        var systemPrompt = SkillLoader.GetSystemPrompt();
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            args.Add("--append-system-prompt");
            args.Add($"\"{EscapeArgument(systemPrompt)}\"");
        }

        // 会话恢复
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Add("--resume");
            args.Add($"\"{sessionId}\"");
        }

        // 工具权限控制
        if (allowedTools != null && allowedTools.Count > 0)
        {
            args.Add("--allowedTools");
            args.Add(string.Join(",", allowedTools));
        }

        return string.Join(" ", args);
    }
    
    private static string EscapeArgument(string arg)
    {
        return arg.Replace("\"", "\\\"").Replace("\n", " ");
    }
}
