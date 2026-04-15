using System.Runtime.CompilerServices;
using TaskStatus = NetYamlForge.AI.Models.TaskStatus;
using NetYamlForge.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetYamlForge.AI.Services.Providers;

/// <summary>
/// 模拟 CLI 服务（测试用）
/// </summary>
public class MockCLIService : BaseCLIService
{
    public MockCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        SkillLoader skillLoader,
        ILogger<MockCLIService> logger)
        : base(executor, config, skillLoader, logger, "mock")
    {
    }
    
    public override Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "Mock CLI (Test)",
            Installed = true,
            Version = "1.0.0-test",
            Authenticated = true,
            Capabilities = new() { "Read", "Write", "Edit", "Bash", "Git" }
        });
    }
    
    protected override List<string> BuildArgumentList(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools,
        string? systemPromptOverride = null)
    {
        var args = new List<string> { "-p", message };
        if (streaming) { args.Add("--output-format"); args.Add("stream-json"); }
        return args;
    }
    
    /// <summary>
    /// 模拟流式执行（用于测试）
    /// </summary>
    public async IAsyncEnumerable<ProgressUpdate> ExecuteMockStreamingAsync(
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ProgressUpdate
        {
            Message = "收到指令，开始处理...",
            Progress = 10,
            Status = TaskStatus.Running,
            Logs = new() { $"收到：{message}" }
        };
        
        await Task.Delay(500, ct);
        
        yield return new ProgressUpdate
        {
            Message = "分析中...",
            Progress = 30,
            Status = TaskStatus.Running,
            Logs = new() { "正在分析请求内容" }
        };
        
        await Task.Delay(500, ct);
        
        yield return new ProgressUpdate
        {
            Message = "生成结果...",
            Progress = 70,
            Status = TaskStatus.Running,
            Logs = new() { "正在生成响应" }
        };
        
        await Task.Delay(500, ct);
        
        yield return new ProgressUpdate
        {
            Message = $"[Mock] 已完成：{message}",
            Progress = 100,
            Status = TaskStatus.Completed,
            Logs = new() { "任务完成" }
        };
    }
}
