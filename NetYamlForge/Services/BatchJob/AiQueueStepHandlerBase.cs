using System.Data;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 队列式 AI 步骤执行器的通用基类（继承自 QueueStepHandlerBase）。
/// 强制子类使用 ICliChainService 进行 AI 调用，以保证 fallback 机制生效。
/// </summary>
public abstract class AiQueueStepHandlerBase<TRow> : QueueStepHandlerBase<TRow>
{
    protected readonly ICliChainService Cli;

    protected AiQueueStepHandlerBase(ICliChainService cli, ILogger logger)
    {
        Cli = cli ?? throw new ArgumentNullException(nameof(cli));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
