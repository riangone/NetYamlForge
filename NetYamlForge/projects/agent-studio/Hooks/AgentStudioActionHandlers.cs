// 文件概要: agent-studio 项目的自定义动作处理器 (CustomActionHandler)
// 实现针对 agent_task 实体的 "run_pipeline" 动作，调用 IAgentOrchestrationService
// 执行 Architect -> Developer -> Tester 三阶段流水线，并将产出与执行日志回填到数据库中。
//
// 重要: ExecuteAsync 本体运行在 EntityCrudExecutionService.ExecuteActionAsync 的
// SqliteWriteGate + DB 事务内部（见 ICustomActionHandler 接口注释）。该门闩是按 DB 文件
// 的全局单写者信号量，持有期间会阻塞全站（聊天记录、BatchJob、其它 entity CRUD……）的
// 所有 SQLite 写入。三阶段 AI CLI 编排（Architect -> Developer -> Tester）单次调用超时
// 90s 且带 fallback 重试，最坏情况可持续十几分钟——绝不能在这个事务/门闩内同步执行。
// 因此这里只在 tx 内做「读任务 + 置 Running」两个快操作就立即返回 Success（让外层提交并
// 释放门闩），实际编排改为脱离请求作用域的后台任务，用独立的 DB 连接、独立的短事务分别
// 回写最终状态与日志，从而彻底避免长耗时外部 I/O 占用全局写锁。
// 取消信号也随之从「HTTP 请求级」改为「应用生命周期级」(IHostApplicationLifetime)：
// 既然任务已经和触发它的 HTTP 请求解耦，用户关闭页面不应该杀死已经在跑的后台流水线；
// 但进程优雅关闭时（ApplicationStopping）仍应尝试取消，而不是像旧代码的
// CancellationToken.None 那样完全无法取消。

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Dapper;
using NetYamlForge.Services.Connection;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.AI.AgentStudio;

namespace NetYamlForge.Projects.AgentStudio.Hooks;

/// <summary>
/// 协同 Agent 自动化流水线执行处理器。
/// ExecuteAsync 自身保持轻量（在外层事务内快速返回）；实际的三阶段 AI 编排在
/// <see cref="RunPipelineInBackgroundAsync"/> 中脱离该事务/门闩异步执行。
/// </summary>
public sealed class RunPipelineHandler : ICustomActionHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionManager _connectionManager;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<RunPipelineHandler> _logger;

    public string Name => "run_pipeline_handler";

    public RunPipelineHandler(
        IServiceScopeFactory scopeFactory,
        IConnectionManager connectionManager,
        IHostApplicationLifetime appLifetime,
        ILogger<RunPipelineHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionManager = connectionManager;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    private sealed class TaskRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string? Status { get; set; }
        public string? ArchitectCli { get; set; }
        public string? DeveloperCli { get; set; }
        public string? TesterCli { get; set; }
    }

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!int.TryParse(ctx.RecordId, out int taskId))
        {
            return ActionHandlerResult.Failure("无效的任务 ID (RecordId)");
        }

        var task = await db.QueryFirstOrDefaultAsync<TaskRow>(
            "SELECT id AS Id, title AS Title, description AS Description, status AS Status, " +
            "architect_cli AS ArchitectCli, developer_cli AS DeveloperCli, tester_cli AS TesterCli " +
            "FROM agent_task WHERE id = @Id",
            new { Id = taskId }, tx);

        if (task == null)
        {
            return ActionHandlerResult.Failure($"未找到 ID 为 {taskId} 的任务记录。");
        }

        _logger.LogInformation("[AgentStudio] Queuing pipeline for Task {TaskId}: '{Title}'", taskId, task.Title);

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync(
            "UPDATE agent_task SET status = 'Running (Stage 1: Architect)', updated_at = @Now WHERE id = @Id",
            new { Id = taskId, Now = now }, tx);

        // ここで await しない: 三阶段 AI 编排は后台で実行し、この handler（=外层事务/
        // SqliteWriteGate 持有区间）は「Running」への状態遷移だけを行って即座に返す。
        var title = task.Title;
        var description = task.Description ?? "";
        var projectName = ctx.Project;
        var stoppingToken = _appLifetime.ApplicationStopping;

        _ = Task.Run(() => RunPipelineInBackgroundAsync(taskId, title, description, projectName, stoppingToken));

        return ActionHandlerResult.Success();
    }

    /// <summary>
    /// 実際の三阶段 AI 编排を、外层の CRUD 事务 / SqliteWriteGate から完全に切り离して実行する。
    /// 各 Stage の開始・完了ごとに独立したコネクション + 短い専用事务（それぞれ自前で SqliteWriteGate を
    /// 経由する）で DB に即時書き込むため、リアルタイムに進捗が UI から確認できる。
    /// </summary>
    private async Task RunPipelineInBackgroundAsync(
        int taskId,
        string title,
        string description,
        string projectName,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var orchestrationService = scope.ServiceProvider.GetRequiredService<IAgentOrchestrationService>();

        var request = new AgentTaskExecutionRequest(
            Title: title,
            Description: description,
            ProjectName: projectName,
            TimeoutSeconds: 300
        );

        // 阶段完成回调：每完成一个阶段立即写库，UI 可即时观察到该阶段输出与状态流转
        Func<AgentExecutionLogEntry, Task> onStageCompleted = async (log) =>
        {
            try
            {
                using var db = await _connectionManager.GetConnectionAsync(projectName, CancellationToken.None);
                await SqliteWriteGate.RunAsync(db, async () =>
                {
                    using var tx = db.BeginTransaction();
                    try
                    {
                        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                        // 确保 log_content 绝不为空！即使报错或无输出也保证用户在 UI「日志输出」看到明确反馈
                        var displayContent = !string.IsNullOrWhiteSpace(log.Output)
                            ? log.Output
                            : (log.Success
                                ? $"[{log.Stage} 阶段执行完毕，产物已就绪]"
                                : $"[执行失败 - {log.Role} ({log.Provider})]\n错误详情:\n{log.Error}");

                        await db.ExecuteAsync(@"
                            INSERT INTO agent_task_log (task_id, stage, role, provider, success, log_content, error_message, created_at)
                            VALUES (@TaskId, @Stage, @Role, @Provider, @Success, @LogContent, @ErrorMessage, @CreatedAt)",
                            new
                            {
                                TaskId = taskId,
                                Stage = log.Stage,
                                Role = log.Role,
                                Provider = log.Provider,
                                Success = log.Success ? 1 : 0,
                                LogContent = displayContent,
                                ErrorMessage = log.Error,
                                CreatedAt = nowStr
                            }, tx);

                        var nextStatus = log.Success
                            ? (log.Stage switch
                            {
                                "Architect" => "Running (Stage 2: Developer)",
                                "Developer" => "Running (Stage 3: Tester)",
                                "Tester" => "Completed",
                                _ => "Running"
                            })
                            : $"Failed ({log.Stage})";

                        if (log.Stage == "Architect")
                        {
                            await db.ExecuteAsync(@"
                                UPDATE agent_task SET
                                    architect_cli = @Provider,
                                    architect_output = @Output,
                                    status = @Status,
                                    updated_at = @Now
                                WHERE id = @Id",
                                new { Id = taskId, Provider = log.Provider, Output = log.Output, Status = nextStatus, Now = nowStr }, tx);
                        }
                        else if (log.Stage == "Developer")
                        {
                            await db.ExecuteAsync(@"
                                UPDATE agent_task SET
                                    developer_cli = @Provider,
                                    developer_output = @Output,
                                    status = @Status,
                                    updated_at = @Now
                                WHERE id = @Id",
                                new { Id = taskId, Provider = log.Provider, Output = log.Output, Status = nextStatus, Now = nowStr }, tx);
                        }
                        else if (log.Stage == "Tester")
                        {
                            await db.ExecuteAsync(@"
                                UPDATE agent_task SET
                                    tester_cli = @Provider,
                                    tester_output = @Output,
                                    status = @Status,
                                    updated_at = @Now
                                WHERE id = @Id",
                                new { Id = taskId, Provider = log.Provider, Output = log.Output, Status = nextStatus, Now = nowStr }, tx);
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                    return true;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AgentStudio] Failed to write intermediate stage log for Task {TaskId}, Stage {Stage}", taskId, log.Stage);
            }
        };

        AgentTaskExecutionResult? result = null;
        Exception? failure = null;
        try
        {
            _logger.LogInformation("[AgentStudio] Starting background pipeline for Task {TaskId}", taskId);
            result = await orchestrationService.ExecutePipelineAsync(request, onStageCompleted, stoppingToken);
        }
        catch (Exception ex)
        {
            failure = ex;
            _logger.LogError(ex, "[AgentStudio] Pipeline execution failed with exception for Task {TaskId}", taskId);
        }

        // 最终收尾与异常持久化
        try
        {
            using var db = await _connectionManager.GetConnectionAsync(projectName, CancellationToken.None);

            await SqliteWriteGate.RunAsync(db, async () =>
            {
                using var tx = db.BeginTransaction();
                try
                {
                    var finishedNow = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    if (failure != null)
                    {
                        await db.ExecuteAsync(
                            "UPDATE agent_task SET status = 'Failed', updated_at = @Now WHERE id = @Id",
                            new { Id = taskId, Now = finishedNow }, tx);

                        await db.ExecuteAsync(@"
                            INSERT INTO agent_task_log (task_id, stage, role, provider, success, log_content, error_message, created_at)
                            VALUES (@TaskId, 'Pipeline', 'System', 'Exception', 0, @LogContent, @ErrorMessage, @CreatedAt)",
                            new
                            {
                                TaskId = taskId,
                                LogContent = $"[系统异常]\n{failure}",
                                ErrorMessage = failure.Message,
                                CreatedAt = finishedNow
                            }, tx);
                    }
                    else if (result != null)
                    {
                        var finalStatus = result.Success ? "Completed" : "Failed";

                        await db.ExecuteAsync(@"
                            UPDATE agent_task SET
                                status = @Status,
                                architect_cli = COALESCE(architect_cli, @ArchCli),
                                developer_cli = COALESCE(developer_cli, @DevCli),
                                tester_cli = COALESCE(tester_cli, @TestCli),
                                architect_output = COALESCE(architect_output, @ArchOutput),
                                developer_output = COALESCE(developer_output, @DevOutput),
                                tester_output = COALESCE(tester_output, @TestOutput),
                                updated_at = @Now
                            WHERE id = @Id",
                            new
                            {
                                Id = taskId,
                                Status = finalStatus,
                                ArchCli = result.Allocation?.Architect?.Provider,
                                DevCli = result.Allocation?.Developer?.Provider,
                                TestCli = result.Allocation?.Tester?.Provider,
                                ArchOutput = result.ArchitectOutput,
                                DevOutput = result.DeveloperOutput,
                                TestOutput = result.TesterOutput,
                                Now = finishedNow
                            }, tx);
                    }

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
                return true;
            });

            _logger.LogInformation(
                "[AgentStudio] Background pipeline finished for Task {TaskId}. Success: {Success}",
                taskId, failure == null && result is { Success: true });
        }
        catch (Exception writeEx)
        {
            _logger.LogCritical(writeEx, "[AgentStudio] Failed to persist pipeline result for Task {TaskId}", taskId);
        }
    }
}
