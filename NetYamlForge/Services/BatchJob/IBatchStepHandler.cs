using System.Data;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 批处理步骤执行器接口。
/// 每种 job.Type 对应一个实现，由 DI 自动收集注册，
/// BatchJobExecutor 仅持有此接口集合，不再直接注入特定领域依赖。
/// </summary>
public interface IBatchStepHandler
{
    /// <summary>对应 BatchJobDefinition.Type 的字符串值（小写）</summary>
    string StepType { get; }

    Task ExecuteAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken ct);
}
