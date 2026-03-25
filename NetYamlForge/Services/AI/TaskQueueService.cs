using System.Threading.Channels;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI.Providers;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 任务队列服务
/// </summary>
public class TaskQueueService
{
    private readonly Channel<AITask> _queue;
    private readonly ProgressTracker _tracker;
    private readonly CLIServiceFactory _factory;
    private readonly CliConfig _config;
    private readonly ILogger<TaskQueueService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public TaskQueueService(
        ProgressTracker tracker,
        CLIServiceFactory factory,
        IOptions<CliConfig> config,
        ILogger<TaskQueueService> logger,
        IServiceProvider serviceProvider)
    {
        _queue = Channel.CreateUnbounded<AITask>();
        _tracker = tracker;
        _factory = factory;
        _config = config.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task EnqueueAsync(AITask task, CancellationToken ct = default)
    {
        await _queue.Writer.WriteAsync(task, ct);
        _logger.LogInformation("Task {TaskId} enqueued", task.Id);
    }
    
    public void StartProcessing(CancellationToken ct = default)
    {
        // 启动多个消费者（根据最大并发数）
        for (int i = 0; i < _config.MaxConcurrentTasks; i++)
        {
            Task.Run(async () => await ProcessQueueAsync(ct), ct);
        }
        
        _logger.LogInformation("Task queue processing started with {Count} workers", _config.MaxConcurrentTasks);
    }
    
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var task in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ProcessTaskAsync(task, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _tracker.Cancel(task.Id);
                _logger.LogInformation("Task {TaskId} cancelled", task.Id);
            }
            catch (Exception ex)
            {
                _tracker.Fail(task.Id, ex.Message);
                _logger.LogError(ex, "Task {TaskId} failed", task.Id);
            }
        }
    }
    
    private async Task ProcessTaskAsync(AITask task, CancellationToken ct)
    {
        _logger.LogInformation("Processing task {TaskId} with CLI {CliTool}", task.Id, task.CliTool);

        CancellationTokenSource? timeoutCts = null;
        try
        {
            var aiService = _factory.GetService(task.CliTool);
            _tracker.UpdateStatus(task.Id, TaskStatus.Running, 0);

            // 使用超时 Token
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.TaskTimeoutSeconds));

            // 检查是否是 Mock 服务（测试用）
            if (aiService is MockCLIService mockService)
            {
                await foreach (var update in mockService.ExecuteMockStreamingAsync(task.Message, timeoutCts.Token))
                {
                    _tracker.UpdateProgress(task.Id, update);
                    if (update.Status == TaskStatus.Completed)
                    {
                        _tracker.Complete(task.Id, update.Message);
                        return;
                    }
                }
            }
            else
            {
                // 实际 CLI 服务
                var workingDir = GetWorkingDirectory(task.Project);

                await foreach (var update in aiService.ExecuteStreamingAsync(
                    task.Message,
                    workingDir,
                    task.SessionId,
                    task.AllowedTools ?? _config.DefaultAllowedTools,
                    timeoutCts.Token))
                {
                    _tracker.UpdateProgress(task.Id, update);

                    if (update.Status == TaskStatus.Completed)
                    {
                        _tracker.Complete(task.Id, update.Message);
                        return;
                    }
                    else if (update.Status == TaskStatus.Failed)
                    {
                        _tracker.Fail(task.Id, update.Message ?? "Unknown error");
                        return;
                    }
                }

                // 如果流式完成但没有明确状态，标记为完成
                _tracker.Complete(task.Id, "Task completed");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || (timeoutCts?.IsCancellationRequested ?? false))
        {
            _tracker.Cancel(task.Id);
            throw;
        }
        catch (Exception ex)
        {
            _tracker.Fail(task.Id, ex.Message);
            throw;
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }
    
    private string? GetWorkingDirectory(string? project)
    {
        if (string.IsNullOrEmpty(project))
        {
            return _config.DefaultWorkingDirectory;
        }
        
        // 获取项目目录路径
        var projectPath = Path.Combine(
            AppContext.BaseDirectory,
            "projects",
            project);
        
        return Directory.Exists(projectPath) ? projectPath : _config.DefaultWorkingDirectory;
    }
}
