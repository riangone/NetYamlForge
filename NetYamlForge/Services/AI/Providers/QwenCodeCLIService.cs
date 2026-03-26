using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// Qwen Code CLI サービス
/// </summary>
public class QwenCodeCLIService : BaseCLIService
{
    public QwenCodeCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger<QwenCodeCLIService> logger)
        : base(executor, config, skillLoader, logger, "qwen")
    {
    }

    // appsettings に QwenCode.Path が設定されている場合はそのパスを使用する
    protected override string CommandPath =>
        string.IsNullOrEmpty(Config.QwenCode.Path) ? ToolName : Config.QwenCode.Path;

    // DASHSCOPE_API_KEY / DASHSCOPE_BASE_URL を環境変数として渡す
    protected override IReadOnlyDictionary<string, string>? GetEnvironmentVariables()
    {
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(Config.QwenCode.ApiKey))
            env["DASHSCOPE_API_KEY"] = Config.QwenCode.ApiKey;

        if (!string.IsNullOrEmpty(Config.QwenCode.BaseUrl))
            env["DASHSCOPE_BASE_URL"] = Config.QwenCode.BaseUrl;

        return env.Count > 0 ? env : null;
    }

    public override async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        var info = new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "Qwen Code",
            Capabilities = new() { "Read", "Write", "Edit", "Bash", "Git" }
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
                if (!string.IsNullOrEmpty(Config.QwenCode.ApiKey))
                {
                    info.Authenticated = true;
                }
                else
                {
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
            Logger.LogWarning(ex, "Failed to get Qwen Code CLI info");
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

        // 非インタラクティブモード
        args.Add("-p");
        args.Add($"\"{EscapeArgument(message)}\"");

        // 出力フォーマット（Qwen Code は --verbose 未サポート）
        args.Add(streaming ? "--output-format stream-json" : "--output-format json");

        // モデル指定（設定されている場合）
        if (!string.IsNullOrEmpty(Config.QwenCode.Model))
        {
            args.Add("--model");
            args.Add(Config.QwenCode.Model);
        }

        // フレームワーク固有のシステムプロンプトを追加
        var systemPrompt = SkillLoader.GetSystemPrompt();
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            args.Add("--system-prompt");
            args.Add($"\"{EscapeArgument(systemPrompt)}\"");
        }

        // セッション再開
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Add("--resume");
            args.Add($"\"{sessionId}\"");
        }

        // ツール権限制御
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
