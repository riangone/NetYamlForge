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
        ILogger<ClaudeCLIService> logger)
        : base(executor, config, logger, "claude")
    {
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
            // 检查是否安装
            var result = await Executor.ExecuteAsync(ToolName, "--version", ct: ct);
            if (result.ExitCode == 0)
            {
                info.Installed = true;
                info.Version = result.Output.Trim();
                
                // 检查是否已认证（通过简单命令测试）
                var authResult = await Executor.ExecuteAsync(
                    ToolName, 
                    "-p \"Hello\" --output-format json", 
                    ct: ct);
                info.Authenticated = authResult.ExitCode == 0 && 
                    !authResult.Error.Contains("auth", StringComparison.OrdinalIgnoreCase);
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
        args.Add(streaming ? "--output-format stream-json" : "--output-format json");
        
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
