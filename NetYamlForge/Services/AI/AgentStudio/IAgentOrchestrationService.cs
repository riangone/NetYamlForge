namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 流水线执行请求
/// </summary>
public record AgentTaskExecutionRequest(
    string Title,
    string Description,
    string? ProjectName = null,
    IReadOnlyList<string>? CustomProviders = null,
    int? TimeoutSeconds = null,
    string? PreferredArchitect = null,
    string? PreferredDeveloper = null,
    string? PreferredTester = null
);

/// <summary>
/// 流水线各阶段执行日志条目
/// </summary>
public record AgentExecutionLogEntry(
    string Stage,
    string Role,
    string Provider,
    bool Success,
    string? Output,
    string? Error,
    DateTime Timestamp
);

/// <summary>
/// 流水线执行结果
/// </summary>
public record AgentTaskExecutionResult(
    bool Success,
    AgentTaskAllocation Allocation,
    string? ArchitectOutput,
    string? DeveloperOutput,
    string? TesterOutput,
    IReadOnlyList<AgentExecutionLogEntry> Logs,
    string? Error = null
);

/// <summary>
/// Agent 自动化编排调度服务契约
/// </summary>
public interface IAgentOrchestrationService
{
    /// <summary>
    /// 获取一次随机角色分配矩阵
    /// </summary>
    AgentTaskAllocation AllocateRoles(IReadOnlyList<string>? customProviders = null);

    /// <summary>
    /// 执行完整的“架构设计 -> 代码开发 -> 测试验证”三阶段自动化流水线
    /// </summary>
    Task<AgentTaskExecutionResult> ExecutePipelineAsync(
        AgentTaskExecutionRequest request,
        Func<AgentExecutionLogEntry, Task>? onStageCompleted = null,
        CancellationToken cancellationToken = default);
}
