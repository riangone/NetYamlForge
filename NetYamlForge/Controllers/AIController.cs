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
    private readonly ILogger<AIController> _logger;

    public AIController(
        CLIServiceFactory cliFactory,
        ProgressTracker tracker,
        TaskQueueService taskQueue,
        ILogger<AIController> logger)
    {
        _cliFactory = cliFactory;
        _tracker = tracker;
        _taskQueue = taskQueue;
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
            
            // 加入队列
            await _taskQueue.EnqueueAsync(task);
            
            // 返回响应
            return Ok(new AIChatResponse
            {
                TaskId = task.Id,
                Status = task.Status,
                Progress = task.Progress,
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
            
            return Ok(new
            {
                available = tools.Values.Select(t => new
                {
                    t.Name,
                    t.DisplayName,
                    t.Installed,
                    t.Version,
                    t.Authenticated,
                    t.Capabilities
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
}
