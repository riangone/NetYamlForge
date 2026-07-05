using System.Data;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// AI 步骤执行器的通用基类。
/// 强制子类使用 ICliChainService 进行 AI 调用，避免绕过 fallback 机制。
/// </summary>
public abstract class AiExecutorBase : IBatchStepHandler
{
    public abstract string StepType { get; }
    protected readonly ICliChainService Cli;
    protected readonly ILogger Logger;

    protected AiExecutorBase(ICliChainService cli, ILogger logger)
    {
        Cli = cli ?? throw new ArgumentNullException(nameof(cli));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract Task ExecuteAsync(
        BatchJobDefinition job,
        string? projectName,
        IDbConnection db,
        IDbTransaction tx,
        BatchJobResult result,
        CancellationToken ct);
}
