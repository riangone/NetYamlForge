using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 服务接口
/// </summary>
public interface ICLIService
{
    /// <summary>
    /// CLI 工具名称
    /// </summary>
    string ToolName { get; }
    
    /// <summary>
    /// 检查 CLI 是否已安装
    /// </summary>
    Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default);
    
    /// <summary>
    /// 执行命令（流式）
    /// </summary>
    IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// 执行命令（一次性）
    /// </summary>
    Task<string> ExecuteAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// 取消任务（终止进程）
    /// </summary>
    Task CancelAsync(int processId, CancellationToken ct = default);
}
