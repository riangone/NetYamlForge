using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers;

/// <summary>
/// AI 助手控制器
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]             // フレームワーク AI: /api/AI/...
[Route("{project}/api/[controller]")]   // プロジェクト付き: /{project}/api/AI/...
public class AIController : ControllerBase
{
    private readonly CLIServiceFactory _cliFactory;
    private readonly ProgressTracker _tracker;
    private readonly TaskQueueService _taskQueue;
    private readonly ChatHistoryService _chatHistory;
    private readonly SkillLoader _skillLoader;
    private readonly IOptionsMonitor<CliConfig> _cliConfig;
    private readonly ILogger<AIController> _logger;

    public AIController(
        CLIServiceFactory cliFactory,
        ProgressTracker tracker,
        TaskQueueService taskQueue,
        ChatHistoryService chatHistory,
        SkillLoader skillLoader,
        IOptionsMonitor<CliConfig> cliConfig,
        ILogger<AIController> logger)
    {
        _cliFactory = cliFactory;
        _tracker = tracker;
        _taskQueue = taskQueue;
        _chatHistory = chatHistory;
        _skillLoader = skillLoader;
        _cliConfig = cliConfig;
        _logger = logger;
    }
    
    /// <summary>
    /// 获取当前用户 ID
    /// </summary>
    private string GetCurrentUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? User.FindFirst(ClaimTypes.Name)?.Value 
            ?? "anonymous";
    }
    
    /// <summary>
    /// 发送聊天请求
    /// </summary>
    [HttpPost("chat")]
    public async Task<ActionResult<AIChatResponse>> Chat([FromBody] AIChatRequest request, [FromRoute] string? project)
    {
        try
        {
            // 从路由绑定项目名（如果 body 中没有提供）
            if (string.IsNullOrEmpty(request.Project) && !string.IsNullOrEmpty(project))
            {
                request.Project = project;
            }

            // 验证 CLI 工具
            var cliService = _cliFactory.GetService(request.CliTool);
            var toolInfo = await cliService.GetToolInfoAsync();

            if (!toolInfo.Installed)
            {
                return BadRequest(new { error = $"CLI tool '{request.CliTool}' is not installed" });
            }

            if (!toolInfo.Authenticated)
            {
                return BadRequest(new { error = $"CLI tool '{request.CliTool}' is not authenticated. Please run '{request.CliTool} login' first." });
            }

            var userId = GetCurrentUserId();

            // ユーザーのメッセージをチャット履歴に保存
            // プロジェクトが指定されていればそのプロジェクトの DB、なければ全局 DB を使用
            await _chatHistory.SaveMessageAsync(userId, request.Message, "user", 
                provider: request.CliTool, 
                chatContext: string.IsNullOrEmpty(request.Project) ? "framework" : request.Project,
                projectName: request.Project);

            // 创建任务
            var task = new AITask
            {
                Id = $"task_{Guid.NewGuid():N}",
                UserId = userId,
                CliTool = request.CliTool,
                Message = request.Message,
                Project = request.Project,
                SessionId = request.SessionId,
                Status = TaskStatus.Pending,
                Progress = 0,
                CreatedAt = DateTime.UtcNow
            };

            _tracker.Add(task);

            // 加入队列并等待完成
            await _taskQueue.EnqueueAsync(task);

            // 等待任务完成（最多 60 秒）
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
                var currentTask = _tracker.GetTask(task.Id);
                if (currentTask == null) break;

                if (currentTask.Status == TaskStatus.Completed)
                {
                    return Ok(new AIChatResponse
                    {
                        TaskId = task.Id,
                        Message = currentTask.Result,
                        Status = currentTask.Status,
                        Progress = currentTask.Progress,
                        Result = currentTask.Result,
                        SessionId = task.SessionId,
                        Provider = request.CliTool
                    });
                }
                else if (currentTask.Status == TaskStatus.Failed || currentTask.Status == TaskStatus.Cancelled)
                {
                    return BadRequest(new {
                        error = currentTask.Error ?? "Task failed",
                        taskId = task.Id,
                        status = currentTask.Status
                    });
                }
            }

            // 超时返回
            return Ok(new AIChatResponse
            {
                TaskId = task.Id,
                Message = task.Result,
                Status = task.Status,
                Progress = task.Progress,
                Result = task.Result,
                SessionId = task.SessionId,
                Provider = request.CliTool
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid CLI tool request");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat request failed");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
    
    /// <summary>
    /// 获取任务列表
    /// </summary>
    [HttpGet("tasks")]
    public ActionResult<IEnumerable<AITask>> GetTasks([FromQuery] int? limit = 20)
    {
        var userId = GetCurrentUserId();
        var tasks = _tracker.GetUserTasks(userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit ?? 20)
            .ToList();
        
        return Ok(tasks);
    }
    
    /// <summary>
    /// 获取指定任务
    /// </summary>
    [HttpGet("tasks/{taskId}")]
    public ActionResult<AITask> GetTask(string taskId)
    {
        var task = _tracker.GetTask(taskId);
        if (task == null)
        {
            return NotFound();
        }
        
        // 权限检查
        if (task.UserId != GetCurrentUserId())
        {
            return Forbid();
        }
        
        return Ok(task);
    }
    
    /// <summary>
    /// 取消任务
    /// </summary>
    [HttpDelete("tasks/{taskId}")]
    public async Task<ActionResult> CancelTask(string taskId)
    {
        try
        {
            var task = _tracker.GetTask(taskId);
            if (task == null)
            {
                return NotFound();
            }
            
            // 权限检查
            if (task.UserId != GetCurrentUserId())
            {
                return Forbid();
            }
            
            // 只能取消运行中的任务
            if (task.Status != TaskStatus.Running && task.Status != TaskStatus.Pending)
            {
                return BadRequest(new { error = "Can only cancel running or pending tasks" });
            }
            
            // 终止进程
            if (task.ProcessId.HasValue)
            {
                var cliService = _cliFactory.GetService(task.CliTool);
                await cliService.CancelAsync(task.ProcessId.Value);
            }
            
            _tracker.Cancel(taskId);
            
            return Ok(new { message = "Task cancelled" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel task {TaskId}", taskId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    /// <summary>
    /// 获取可用的 CLI 工具列表
    /// </summary>
    [HttpGet("cli-tools")]
    public async Task<ActionResult<object>> GetCliTools()
    {
        try
        {
            var tools = await _cliFactory.GetAvailableToolsAsync();
            
            // available をオブジェクト（ツール名キー）として返す。
            // 配列で返すと JS 側で data.available['claude'] が undefined になる。
            return Ok(new
            {
                available = tools.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        kvp.Value.Name,
                        kvp.Value.DisplayName,
                        kvp.Value.Installed,
                        kvp.Value.Version,
                        kvp.Value.Authenticated,
                        kvp.Value.Capabilities
                    }),
                defaultTool = _cliConfig.CurrentValue.DefaultTool
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get CLI tools");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    /// <summary>
    /// スキル（プロンプトテンプレート）一覧を取得します
    /// </summary>
    [HttpGet("skills")]
    public ActionResult GetSkills()
    {
        return Ok(_skillLoader.GetSkills());
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult> Health()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// チャット履歴を取得します
    /// </summary>
    /// <param name="context">絞り込みコンテキスト（framework / dealer-staff / dealer-customer）。省略時は全件。</param>
    /// <param name="project">プロジェクト名。指定された場合はそのプロジェクトの DB から取得</param>
    [HttpGet("history")]
    public async Task<ActionResult> GetHistory([FromQuery] int limit = 100, [FromQuery] string? context = "framework", [FromRoute] string? project = null)
    {
        var userId = GetCurrentUserId();
        // プロジェクトが指定されていればそのプロジェクトの DB、なければ全局 DB を使用
        var messages = await _chatHistory.GetHistoryAsync(userId, projectName: project, limit: limit, chatContext: string.IsNullOrEmpty(context) ? null : context);
        return Ok(messages);
    }

    /// <summary>
    /// コマンド実行ログ一覧を取得します
    /// </summary>
    /// <param name="project">プロジェクト名。指定された場合はそのプロジェクトの DB から取得</param>
    [HttpGet("command-logs")]
    public async Task<ActionResult> GetCommandLogs([FromQuery] int limit = 50, [FromRoute] string? project = null)
    {
        var userId = GetCurrentUserId();
        var logs = await _chatHistory.GetCommandLogsAsync(userId, projectName: project, limit: limit);
        return Ok(logs);
    }

    /// <summary>
    /// メッセージを履歴に保存します
    /// </summary>
    /// <param name="project">プロジェクト名。指定された場合はそのプロジェクトの DB に保存</param>
    [HttpPost("history")]
    public async Task<ActionResult> SaveMessage([FromBody] SaveChatMessageRequest request, [FromRoute] string? project = null)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.Type))
            return BadRequest(new { error = "Content and Type are required" });

        var userId = GetCurrentUserId();
        var chatContext = string.IsNullOrEmpty(request.ChatContext) ? (string.IsNullOrEmpty(project) ? "framework" : project) : request.ChatContext;
        var id = await _chatHistory.SaveMessageAsync(userId, request.Content, request.Type, provider: request.Provider, chatContext: chatContext, projectName: project);
        return Ok(new { id });
    }

    // [已禁用] 禁止清空聊天记录 - 2026-04-03
    // /// <summary>
    // /// チャット履歴を削除します（context 指定で特定コンテキストのみ削除可）
    // /// </summary>
    // /// <param name="project">プロジェクト名。指定された場合はそのプロジェクトの DB から削除</param>
    // [HttpDelete("history")]
    // public async Task<ActionResult> ClearHistory([FromQuery] string? context = "framework", [FromRoute] string? project = null)
    // {
    //     var userId = GetCurrentUserId();
    //     await _chatHistory.ClearHistoryAsync(userId, string.IsNullOrEmpty(context) ? null : context, projectName: project);
    //     return Ok();
    // }
}
