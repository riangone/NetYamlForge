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
    private readonly ChatHistoryService _chatHistory;

    public TaskQueueService(
        ProgressTracker tracker,
        CLIServiceFactory factory,
        IOptions<CliConfig> config,
        ILogger<TaskQueueService> logger,
        IServiceProvider serviceProvider,
        ChatHistoryService chatHistory)
    {
        _queue = Channel.CreateUnbounded<AITask>();
        _tracker = tracker;
        _factory = factory;
        _config = config.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _chatHistory = chatHistory;
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

        var startedAt = DateTime.UtcNow;

        // コマンド実行ログを開始記録
        try
        {
            await _chatHistory.CreateCommandLogAsync(
                task.UserId, task.Id, task.CliTool, task.Message,
                task.Project, task.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create command log for task {TaskId}", task.Id);
        }

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
                        await SaveCommandLogResultAsync(task.Id, "Completed", update.Message, null, startedAt);
                        return;
                    }
                }
            }
            else
            {
                // 实际 CLI 服务
                var workingDir = GetWorkingDirectory(task.Project);
                string? lastMessage = null;
                var allMessages = new System.Text.StringBuilder();
                var hasMessageContent = false;

                await foreach (var update in aiService.ExecuteStreamingAsync(
                    task.Message,
                    workingDir,
                    task.SessionId,
                    task.AllowedTools ?? _config.DefaultAllowedTools,
                    timeoutCts.Token))
                {
                    _tracker.UpdateProgress(task.Id, update);

                    // 累积所有 assistant 消息（多个 text part 可能分散在多个消息中）
                    if (!string.IsNullOrEmpty(update.Message))
                    {
                        if (hasMessageContent)
                        {
                            allMessages.AppendLine(update.Message);
                        }
                        else
                        {
                            allMessages.Append(update.Message);
                            hasMessageContent = true;
                        }
                        lastMessage = update.Message;
                    }

                    // CLI から返された session_id を保存（次回の --resume に使用）
                    if (!string.IsNullOrEmpty(update.SessionId))
                    {
                        task.SessionId = update.SessionId;
                    }

                    if (update.Status == TaskStatus.Completed)
                    {
                        // assistant メッセージから蓄積したテキストを優先する。
                        // result フィールドは "Task completed" などのステータス文字列の場合があるため、
                        // 実際のテキストコンテンツと区別してフォールバックとして使用する。
                        var resultText = update.Message;
                        // "Task completed" のような汎用ステータス文字列はフォールバックとして使わない
                        if (resultText != null && resultText.Trim().Equals("Task completed", StringComparison.OrdinalIgnoreCase))
                            resultText = null;
                        var finalResult = hasMessageContent ? allMessages.ToString() : (resultText ?? lastMessage ?? "");
                        _tracker.Complete(task.Id, finalResult);
                        await SaveCommandLogResultAsync(task.Id, "Completed", finalResult, null, startedAt);
                        return;
                    }
                    else if (update.Status == TaskStatus.Failed)
                    {
                        var errMsg = update.Message ?? "Unknown error";
                        _tracker.Fail(task.Id, errMsg);
                        await SaveCommandLogResultAsync(task.Id, "Failed", null, errMsg, startedAt);
                        return;
                    }
                }

                // 如果流式完成但没有明确状态，使用累积的消息
                var completedResult = hasMessageContent ? allMessages.ToString() : (lastMessage ?? "");
                _tracker.Complete(task.Id, completedResult);
                await SaveCommandLogResultAsync(task.Id, "Completed", completedResult, null, startedAt);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || (timeoutCts?.IsCancellationRequested ?? false))
        {
            _tracker.Cancel(task.Id);
            await SaveCommandLogResultAsync(task.Id, "Cancelled", null, "Cancelled", startedAt);
            throw;
        }
        catch (Exception ex)
        {
            _tracker.Fail(task.Id, ex.Message);
            await SaveCommandLogResultAsync(task.Id, "Failed", null, ex.Message, startedAt);
            throw;
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    private async Task SaveCommandLogResultAsync(
        string taskId, string status, string? result, string? error, DateTime startedAt)
    {
        try
        {
            var durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            await _chatHistory.UpdateCommandLogAsync(taskId, status, result, error, durationMs);

            // 完了した場合はチャット履歴にも AI レスポンスを保存
            if (status == "Completed" && !string.IsNullOrWhiteSpace(result))
            {
                var t = _tracker.GetTask(taskId);
                if (t != null)
                    await _chatHistory.SaveMessageAsync(t.UserId, result, "assistant");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save command log result for task {TaskId}", taskId);
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
