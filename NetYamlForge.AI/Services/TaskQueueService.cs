using System.Threading.Channels;
using TaskStatus = NetYamlForge.AI.Models.TaskStatus;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services.Providers;
using Microsoft.Extensions.Options;

namespace NetYamlForge.AI.Services;

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
    private readonly SkillLoader? _skillLoader;

    public TaskQueueService(
        ProgressTracker tracker,
        CLIServiceFactory factory,
        IOptions<CliConfig> config,
        ILogger<TaskQueueService> logger,
        IServiceProvider serviceProvider,
        ChatHistoryService chatHistory,
        SkillLoader? skillLoader = null)
    {
        _queue = Channel.CreateUnbounded<AITask>();
        _tracker = tracker;
        _factory = factory;
        _config = config.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _chatHistory = chatHistory;
        _skillLoader = skillLoader;
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
                        await SaveCommandLogResultAsync(task, "Completed", update.Message, null, startedAt);
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

                // ✨ 构建项目特定的系统提示词（如果指定了项目）
                var systemPrompt = BuildSystemPromptForTask(task);

                await foreach (var update in aiService.ExecuteStreamingAsync(
                    task.Message,
                    workingDir,
                    task.SessionId,
                    task.AllowedTools ?? _config.DefaultAllowedTools,
                    systemPromptOverride: systemPrompt,
                    timeoutCts.Token))
                {
                    _tracker.UpdateProgress(task.Id, update);

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
                        
                        _logger.LogInformation("[TaskQueue] Task {TaskId} completed. hasMessageContent={HasContent}, allMessages.Length={Len}, resultText={ResultLen}, finalResult.Length={FinalLen}", 
                            task.Id, hasMessageContent, allMessages.Length, resultText?.Length ?? 0, finalResult?.Length ?? 0);
                        _logger.LogInformation("[TaskQueue] finalResult 先頭 200 文字：{Preview}", finalResult?.Length > 200 ? finalResult[..200] : finalResult);
                        
                        _tracker.Complete(task.Id, finalResult);
                        await SaveCommandLogResultAsync(task, "Completed", finalResult, null, startedAt);
                        return;
                    }
                    else if (update.Status == TaskStatus.Failed)
                    {
                        var errMsg = update.Message ?? "Unknown error";
                        _tracker.Fail(task.Id, errMsg);
                        await SaveCommandLogResultAsync(task, "Failed", null, errMsg, startedAt);
                        return;
                    }

                    // 累积所有 assistant 消息（Completed/Failed 以外のメッセージのみ）
                    if (!string.IsNullOrEmpty(update.Message))
                    {
                        _logger.LogDebug("[TaskQueue] 受信メッセージ：{Len}文字，先頭 50 文字={Preview}", update.Message.Length, update.Message.Length > 50 ? update.Message[..50] : update.Message);
                        
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
                }

                // 如果流式完成但没有明确状态，使用累积的消息
                var completedResult = hasMessageContent ? allMessages.ToString() : (lastMessage ?? "");
                _tracker.Complete(task.Id, completedResult);
                await SaveCommandLogResultAsync(task, "Completed", completedResult, null, startedAt);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || (timeoutCts?.IsCancellationRequested ?? false))
        {
            _tracker.Cancel(task.Id);
            await SaveCommandLogResultAsync(task, "Cancelled", null, "Cancelled", startedAt);
            throw;
        }
        catch (Exception ex)
        {
            _tracker.Fail(task.Id, ex.Message);
            await SaveCommandLogResultAsync(task, "Failed", null, ex.Message, startedAt);
            throw;
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    private async Task SaveCommandLogResultAsync(
        AITask task, string status, string? result, string? error, DateTime startedAt)
    {
        try
        {
            var durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            await _chatHistory.UpdateCommandLogAsync(task.Id, status, result, error, durationMs, task.Project);

            // 完了した場合はチャット履歴にも AI レスポンスを保存
            if (status == "Completed" && !string.IsNullOrWhiteSpace(result))
            {
                var chatContext = string.IsNullOrEmpty(task.Project) ? "framework" : task.Project;
                await _chatHistory.SaveMessageAsync(
                    task.UserId, 
                    result, 
                    "assistant",
                    provider: task.CliTool,
                    chatContext: chatContext,
                    projectName: task.Project,
                    sessionId: task.SessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save command log result for task {TaskId}", task.Id);
        }
    }
    
    private string? GetWorkingDirectory(string? project)
    {
        // 寻找项目根目录（向上递归寻找 .slnx 或 NetYamlForge.AI 目录）
        var rootDir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(rootDir) && !File.Exists(Path.Combine(rootDir, "NetYamlForge.slnx")))
        {
            var parent = Directory.GetParent(rootDir)?.FullName;
            if (parent == null || parent == rootDir) break;
            rootDir = parent;
        }

        if (string.IsNullOrEmpty(project))
        {
            return rootDir ?? _config.DefaultWorkingDirectory;
        }

        // 获取项目目录路径
        var projectPath = Path.Combine(rootDir ?? AppContext.BaseDirectory, "projects", project);

        return Directory.Exists(projectPath) ? projectPath : (rootDir ?? _config.DefaultWorkingDirectory);
    }

    /// <summary>
    /// ✨ 根据任务的项目和上下文构建系统提示词
    /// </summary>
    private string? BuildSystemPromptForTask(AITask task)
    {
        // 如果没有 SkillLoader，返回 null（使用 CLI 默认提示词）
        if (_skillLoader == null) return null;

        // 如果指定了项目，尝试加载项目特定的提示词
        if (!string.IsNullOrEmpty(task.Project))
        {
            var projectSkillsDir = Path.Combine(
                AppContext.BaseDirectory,
                "skills",
                task.Project);

            // ✨ 使用明确的 Context 字段，如果没有则默认为 customer
            var context = task.Context ?? "customer";

            // 根据项目和上下文选择合适的提示词文件
            var rolePromptFile = task.Project switch
            {
                "auto-dealer-demo" => context == "staff"
                    ? "_system-prompt-staff.md"
                    : "_system-prompt-customer.md",
                "jpiere-cs" => context switch
                {
                    "employee" => "_system-prompt-employee.md",
                    "contract_manager" => "_system-prompt-contract-manager.md",
                    "accountant" => "_system-prompt-accountant.md",
                    "purchaser" => "_system-prompt-purchaser.md",
                    "approver" => "_system-prompt-approver.md",
                    _ => "_system-prompt-employee.md"
                },
                _ => null
            };

            if (rolePromptFile != null)
            {
                var promptPath = Path.Combine(projectSkillsDir, rolePromptFile);
                if (File.Exists(promptPath))
                {
                    var frameworkPrompt = _skillLoader.GetSystemPrompt();
                    var rolePrompt = File.ReadAllText(promptPath).Trim();

                    // 去除 YAML frontmatter
                    if (rolePrompt.StartsWith("---"))
                    {
                        var end = rolePrompt.IndexOf("---", 3);
                        if (end >= 0) rolePrompt = rolePrompt[(end + 3)..].Trim();
                    }

                    var systemPrompt = frameworkPrompt + "\n\n" + rolePrompt;

                    // 替换占位符
                    systemPrompt = systemPrompt
                        .Replace("{current_datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
                        .Replace("{project_name}", task.Project);

                    _logger.LogInformation("为任务构建系统提示词：project={Project}, context={Context}, promptFile={File}, length={Length}",
                        task.Project, context, rolePromptFile, systemPrompt.Length);

                    return systemPrompt;
                }
                else
                {
                    _logger.LogWarning("提示词文件不存在：project={Project}, file={File}", task.Project, promptPath);
                }
            }
        }

        // 默认：使用全局框架提示词
        return _skillLoader.GetSystemPrompt();
    }
}
