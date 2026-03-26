using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers;

/// <summary>
/// AI 助手控制器
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    private readonly CLIServiceFactory _cliFactory;
    private readonly ProgressTracker _tracker;
    private readonly TaskQueueService _taskQueue;
    private readonly ChatHistoryService _chatHistory;
    private readonly SkillLoader _skillLoader;
    private readonly ILogger<AIController> _logger;

    public AIController(
        CLIServiceFactory cliFactory,
        ProgressTracker tracker,
        TaskQueueService taskQueue,
        ChatHistoryService chatHistory,
        SkillLoader skillLoader,
        ILogger<AIController> logger)
    {
        _cliFactory = cliFactory;
        _tracker = tracker;
        _taskQueue = taskQueue;
        _chatHistory = chatHistory;
        _skillLoader = skillLoader;
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
    public async Task<ActionResult<AIChatResponse>> Chat([FromBody] AIChatRequest request)
    {
        try
        {
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

            // 创建任务
            var task = new AITask
            {
                Id = $"task_{Guid.NewGuid():N}",
                UserId = GetCurrentUserId(),
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
                        SessionId = task.SessionId
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
                SessionId = task.SessionId
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
                defaultTool = "claude"
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
    [HttpGet("history")]
    public async Task<ActionResult> GetHistory([FromQuery] int limit = 100)
    {
        var userId = GetCurrentUserId();
        var messages = await _chatHistory.GetHistoryAsync(userId, limit);
        return Ok(messages);
    }

    /// <summary>
    /// メッセージを履歴に保存します
    /// </summary>
    [HttpPost("history")]
    public async Task<ActionResult> SaveMessage([FromBody] SaveChatMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.Type))
            return BadRequest(new { error = "Content and Type are required" });

        var userId = GetCurrentUserId();
        var id = await _chatHistory.SaveMessageAsync(userId, request.Content, request.Type);
        return Ok(new { id });
    }

    /// <summary>
    /// チャット履歴を全件削除します
    /// </summary>
    [HttpDelete("history")]
    public async Task<ActionResult> ClearHistory()
    {
        var userId = GetCurrentUserId();
        await _chatHistory.ClearHistoryAsync(userId);
        return Ok();
    }
}
