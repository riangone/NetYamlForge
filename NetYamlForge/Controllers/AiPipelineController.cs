using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers;

/// <summary>
/// AI Pipeline 管理控制器
/// 用于查看和管理 AI 任务执行历史和状态
/// </summary>
[Authorize]
public class AiPipelineController : Controller
{
    private readonly AiPipelineService _pipelineService;
    private readonly HarnessContextAdapter _contextAdapter;
    private readonly ProjectManager _projectManager;
    private readonly ILogger<AiPipelineController> _logger;
    private readonly AiPipelineConfig _config;
    private readonly string _tasksIndexPath;

    public AiPipelineController(
        AiPipelineService pipelineService,
        HarnessContextAdapter contextAdapter,
        ProjectManager projectManager,
        IOptions<AiPipelineConfig> config,
        ILogger<AiPipelineController> logger)
    {
        _pipelineService = pipelineService;
        _contextAdapter = contextAdapter;
        _projectManager = projectManager;
        _logger = logger;
        _config = config.Value;
        _tasksIndexPath = Path.Combine(_config.HarnessWorkDirectory, "tasks-index.json");
        
        // 确保索引文件存在
        if (!Directory.Exists(_config.HarnessWorkDirectory))
        {
            Directory.CreateDirectory(_config.HarnessWorkDirectory);
        }
        
        if (!System.IO.File.Exists(_tasksIndexPath))
        {
            System.IO.File.WriteAllText(_tasksIndexPath, "[]");
        }
    }

    /// <summary>
    /// AI Pipeline 管理主页
    /// </summary>
    [HttpGet("/ai-pipeline")]
    public IActionResult Index()
    {
        var tasks = LoadTaskIndex();
        return View(tasks);
    }

    /// <summary>
    /// 执行 AI Pipeline 任务
    /// </summary>
    [HttpPost("/ai-pipeline/execute")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Execute(
        [FromForm] string prompt,
        [FromForm] string? projectName,
        [FromForm] string? targetDir,
        [FromForm] int? timeout,
        [FromForm] string pipelineMode = "full")
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            TempData["Error"] = "Prompt 不能为空";
            return RedirectToAction("Index");
        }

        try
        {
            _logger.LogInformation("[AI-Pipeline-Web] 开始执行任务 - Project={Project}, Mode={Mode}", 
                projectName, pipelineMode);

            var result = await _pipelineService.ExecutePipelineAsync(
                prompt: prompt,
                projectName: projectName,
                targetProjectDir: targetDir,
                timeout: timeout,
                ct: HttpContext.RequestAborted);

            // 记录任务到索引
            var taskRecord = new AiTaskRecord
            {
                TaskId = result.TaskId,
                Prompt = prompt,
                ProjectName = projectName,
                Status = result.Success ? "completed" : "failed",
                WorkDirectory = result.WorkDirectory,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = result.Success ? DateTime.UtcNow : null,
                GeneratedFilesCount = result.GeneratedFiles.Count
            };

            SaveTaskRecord(taskRecord);

            if (result.Success)
            {
                TempData["Success"] = $"AI Pipeline 执行成功！任务 ID: {result.TaskId}";
                TempData["TaskId"] = result.TaskId;
                TempData["GeneratedFiles"] = result.GeneratedFiles;
            }
            else
            {
                TempData["Error"] = $"AI Pipeline 执行失败: {result.ErrorMessage}";
            }

            return RedirectToAction("Details", new { taskId = result.TaskId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Pipeline-Web] 执行任务时发生错误");
            TempData["Error"] = $"执行失败: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    /// <summary>
    /// 查看任务详情
    /// </summary>
    [HttpGet("/ai-pipeline/task/{taskId}")]
    public IActionResult Details(string taskId)
    {
        var taskDir = Path.Combine(_config.HarnessWorkDirectory, $"pipeline-{taskId}");
        if (!Directory.Exists(taskDir))
        {
            TempData["Error"] = $"任务目录不存在: {taskId}";
            return RedirectToAction("Index");
        }

        var taskRecord = LoadTaskRecord(taskId);
        var model = new AiTaskDetailsModel
        {
            TaskRecord = taskRecord,
            WorkDirectory = taskDir,
            Files = GetTaskFiles(taskDir),
            Logs = LoadTaskLogs(taskDir)
        };

        return View(model);
    }

    /// <summary>
    /// 查看任务生成的文件
    /// </summary>
    [HttpGet("/ai-pipeline/task/{taskId}/file")]
    public IActionResult ViewFile(string taskId, [FromQuery] string path)
    {
        var taskDir = Path.Combine(_config.HarnessWorkDirectory, $"pipeline-{taskId}");
        var fullPath = Path.Combine(taskDir, path);

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound("文件不存在");
        }

        var contentType = GetContentType(fullPath);
        var content = System.IO.File.ReadAllText(fullPath);

        return Content(content, contentType);
    }

    /// <summary>
    /// 下载任务生成的文件
    /// </summary>
    [HttpGet("/ai-pipeline/task/{taskId}/download")]
    public IActionResult DownloadFile(string taskId, [FromQuery] string path)
    {
        var taskDir = Path.Combine(_config.HarnessWorkDirectory, $"pipeline-{taskId}");
        var fullPath = Path.Combine(taskDir, path);

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound("文件不存在");
        }

        var contentType = GetContentType(fullPath);
        var fileName = Path.GetFileName(fullPath);
        
        return PhysicalFile(fullPath, contentType, fileName);
    }

    /// <summary>
    /// 应用生成结果到项目
    /// </summary>
    [HttpPost("/ai-pipeline/task/{taskId}/apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyToProject(string taskId, [FromForm] string targetProject)
    {
        var taskDir = Path.Combine(_config.HarnessWorkDirectory, $"pipeline-{taskId}");
        if (!Directory.Exists(taskDir))
        {
            TempData["Error"] = "任务目录不存在";
            return RedirectToAction("Index");
        }

        try
        {
            var projectDir = _projectManager.TryGet(targetProject, out var p) ? p!.ProjectDir : null;
            if (string.IsNullOrEmpty(projectDir))
            {
                TempData["Error"] = $"项目不存在: {targetProject}";
                return RedirectToAction("Index");
            }

            // 复制文件到项目
            var filesCopied = await CopyFilesToProjectAsync(taskDir, projectDir);
            
            TempData["Success"] = $"成功应用 {filesCopied} 个文件到项目 {targetProject}";
            return RedirectToAction("Details", new { taskId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Pipeline-Web] 应用到项目时发生错误");
            TempData["Error"] = $"应用失败: {ex.Message}";
            return RedirectToAction("Details", new { taskId });
        }
    }

    /// <summary>
    /// 删除任务
    /// </summary>
    [HttpPost("/ai-pipeline/task/{taskId}/delete")]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(string taskId)
    {
        var taskDir = Path.Combine(_config.HarnessWorkDirectory, $"pipeline-{taskId}");
        if (Directory.Exists(taskDir))
        {
            Directory.Delete(taskDir, true);
            _logger.LogInformation("[AI-Pipeline-Web] 已删除任务 - TaskId={TaskId}", taskId);
        }

        // 从索引中移除
        var tasks = LoadTaskIndex();
        tasks.RemoveAll(t => t.TaskId == taskId);
        SaveTaskIndex(tasks);

        TempData["Success"] = "任务已删除";
        return RedirectToAction("Index");
    }

    // ===== 辅助方法 =====

    private List<AiTaskRecord> LoadTaskIndex()
    {
        try
        {
            var json = System.IO.File.ReadAllText(_tasksIndexPath);
            return JsonSerializer.Deserialize<List<AiTaskRecord>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveTaskIndex(List<AiTaskRecord> tasks)
    {
        var json = JsonSerializer.Serialize(tasks, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(_tasksIndexPath, json);
    }

    private void SaveTaskRecord(AiTaskRecord task)
    {
        var tasks = LoadTaskIndex();
        var existing = tasks.FindIndex(t => t.TaskId == task.TaskId);
        if (existing >= 0)
        {
            tasks[existing] = task;
        }
        else
        {
            tasks.Add(task);
        }
        SaveTaskIndex(tasks);
    }

    private AiTaskRecord? LoadTaskRecord(string taskId)
    {
        var tasks = LoadTaskIndex();
        return tasks.Find(t => t.TaskId == taskId);
    }

    private List<string> GetTaskFiles(string taskDir)
    {
        if (!Directory.Exists(taskDir))
            return new();

        return Directory.GetFiles(taskDir, "*.*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(taskDir, f))
            .ToList();
    }

    private List<string> LoadTaskLogs(string taskDir)
    {
        var logs = new List<string>();
        
        // 读取 plan.md
        var planFile = Path.Combine(taskDir, "plan.md");
        if (System.IO.File.Exists(planFile))
        {
            logs.Add("=== plan.md ===");
            logs.AddRange(System.IO.File.ReadAllLines(planFile));
        }

        // 读取 eval-report.md
        var evalFile = Path.Combine(taskDir, "eval-report.md");
        if (System.IO.File.Exists(evalFile))
        {
            logs.Add("=== eval-report.md ===");
            logs.AddRange(System.IO.File.ReadAllLines(evalFile));
        }

        return logs;
    }

    private async Task<int> CopyFilesToProjectAsync(string sourceDir, string targetDir)
    {
        var files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
        var count = 0;

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var targetPath = Path.Combine(targetDir, relativePath);

            var targetSubDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetSubDir) && !Directory.Exists(targetSubDir))
            {
                Directory.CreateDirectory(targetSubDir);
            }

            System.IO.File.Copy(file, targetPath, overwrite: true);
            count++;
        }

        return count;
    }

    private string GetContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "text/plain",
            ".yaml" => "text/plain",
            ".yml" => "text/plain",
            ".md" => "text/plain",
            ".json" => "application/json",
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            _ => "application/octet-stream"
        };
    }
}

/// <summary>
/// AI 任务记录
/// </summary>
public class AiTaskRecord
{
    public string TaskId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string Status { get; set; } = "pending";
    public string WorkDirectory { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int GeneratedFilesCount { get; set; }
}

/// <summary>
/// AI 任务详情模型
/// </summary>
public class AiTaskDetailsModel
{
    public AiTaskRecord? TaskRecord { get; set; }
    public string WorkDirectory { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public List<string> Logs { get; set; } = new();
}
