using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 调用 Harness Multi-AI Pipeline 的服务
/// 支持 Planner → Generator → Evaluator 完整 AI 协作流程
/// </summary>
public class AiPipelineService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiPipelineService> _logger;
    private readonly AiPipelineConfig _config;
    private readonly string _harnessWorkDir;

    public AiPipelineService(
        HttpClient httpClient,
        IOptions<AiPipelineConfig> config,
        ILogger<AiPipelineService> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _harnessWorkDir = _config.HarnessWorkDirectory;
        
        // 确保工作目录存在
        if (!Directory.Exists(_harnessWorkDir))
        {
            Directory.CreateDirectory(_harnessWorkDir);
        }
    }

    /// <summary>
    /// 执行完整的 AI Pipeline（Planner → Generator → Evaluator）
    /// </summary>
    /// <param name="prompt">任务描述（自然语言）</param>
    /// <param name="projectName">项目名称（用于归档）</param>
    /// <param name="targetProjectDir">目标 NetYamlForge 项目目录</param>
    /// <param name="timeout">超时秒数（默认 1 小时）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>执行结果</returns>
    public async Task<PipelineExecutionResult> ExecutePipelineAsync(
        string prompt,
        string? projectName = null,
        string? targetProjectDir = null,
        int? timeout = null,
        CancellationToken ct = default)
    {
        var taskId = Guid.NewGuid().ToString("N")[..8];
        var workDir = Path.Combine(_harnessWorkDir, $"pipeline-{taskId}");
        Directory.CreateDirectory(workDir);

        _logger.LogInformation("[AI-Pipeline] 开始执行 Pipeline - TaskID={TaskId}, Prompt={Prompt}", 
            taskId, prompt.Substring(0, Math.Min(50, prompt.Length)));

        try
        {
            // 调用 Harness 的 pipeline_executor
            var result = await ExecuteHarnessPipelineAsync(prompt, workDir, projectName, timeout, ct);
            
            // 如果指定了目标项目目录，将生成结果复制过去
            if (!string.IsNullOrEmpty(targetProjectDir) && result.Success)
            {
                await CopyResultsToProjectAsync(workDir, targetProjectDir, ct);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-Pipeline] Pipeline 执行失败 - TaskID={TaskId}", taskId);
            return new PipelineExecutionResult
            {
                TaskId = taskId,
                Success = false,
                ErrorMessage = ex.Message,
                WorkDirectory = workDir,
                Logs = new List<string> { ex.ToString() }
            };
        }
    }

    /// <summary>
    /// 通过 HTTP API 调用 Harness（如果 Harness 作为独立服务运行）
    /// </summary>
    private async Task<PipelineExecutionResult> ExecuteHarnessPipelineAsync(
        string prompt,
        string workDir,
        string? projectName,
        int? timeout,
        CancellationToken ct)
    {
        // 检查 Harness 是否作为 HTTP 服务运行
        if (_config.HarnessHttpEndpoint != null)
        {
            return await CallHarnessHttpApiAsync(prompt, workDir, projectName, timeout, ct);
        }
        
        // 否则直接调用 Python 脚本
        return await ExecuteHarnessPythonAsync(prompt, workDir, projectName, timeout, ct);
    }

    /// <summary>
    /// 通过 HTTP API 调用 Harness 服务
    /// </summary>
    private async Task<PipelineExecutionResult> CallHarnessHttpApiAsync(
        string prompt,
        string workDir,
        string? projectName,
        int? timeout,
        CancellationToken ct)
    {
        var request = new HarnessPipelineRequest
        {
            Prompt = prompt,
            WorkDir = workDir,
            PipelineMode = "full",
            ProjectName = projectName,
            Timeout = timeout ?? _config.DefaultTimeoutSeconds
        };

        _logger.LogInformation("[AI-Pipeline] 调用 Harness HTTP API - Endpoint={Endpoint}", 
            _config.HarnessHttpEndpoint);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_config.HarnessHttpEndpoint}/api/pipeline/execute", 
            request, 
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<HarnessPipelineResponse>(cancellationToken: ct);
        
        if (result == null)
        {
            throw new InvalidOperationException("Harness 返回了无效的响应");
        }

        return new PipelineExecutionResult
        {
            TaskId = result.TaskId?.ToString() ?? Guid.NewGuid().ToString("N")[..8],
            Success = result.Status == "completed",
            WorkDirectory = workDir,
            Output = result.Output,
            Logs = result.Output?.Split('\n').ToList() ?? new List<string>(),
            GeneratedFiles = await GetGeneratedFilesAsync(workDir)
        };
    }

    /// <summary>
    /// 直接执行 Harness 的 Python pipeline_executor
    /// </summary>
    private async Task<PipelineExecutionResult> ExecuteHarnessPythonAsync(
        string prompt,
        string workDir,
        string? projectName,
        int? timeout,
        CancellationToken ct)
    {
        var harnessDir = _config.HarnessDirectory;
        if (string.IsNullOrEmpty(harnessDir) || !Directory.Exists(harnessDir))
        {
            throw new InvalidOperationException(
                $"Harness 目录不存在，请配置 AiPipeline:HarnessDirectory。当前路径: {harnessDir}");
        }

        var executorScript = Path.Combine(harnessDir, "webui", "app", "pipeline_executor.py");
        if (!File.Exists(executorScript))
        {
            throw new FileNotFoundException($"pipeline_executor.py 不存在: {executorScript}");
        }

        // 构建 Python 调用命令
        var pythonCmd = _config.PythonExecutable ?? "python3";
        var wrapperScript = Path.Combine(harnessDir, "webui", "harness_wrapper.py");
        
        var args = new List<string>
        {
            wrapperScript,
            "--prompt", prompt,
            "--work-dir", workDir
        };

        if (!string.IsNullOrEmpty(projectName))
        {
            args.AddRange(new[] { "--project", projectName });
        }

        if (timeout.HasValue)
        {
            args.AddRange(new[] { "--timeout", timeout.Value.ToString() });
        }

        _logger.LogInformation("[AI-Pipeline] 执行 Harness Python - Script={Script}", executorScript);

        var psi = new ProcessStartInfo
        {
            FileName = pythonCmd,
            WorkingDirectory = Path.Combine(harnessDir, "webui"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // 添加环境变量
        psi.EnvironmentVariables["PYTHONPATH"] = Path.Combine(harnessDir, "webui");

        using var process = Process.Start(psi) 
            ?? throw new InvalidOperationException($"无法启动进程: {pythonCmd}");

        var outputLines = new List<string>();
        var errorLines = new List<string>();

        // 异步读取输出
        var outputTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !process.HasExited)
            {
                if (process.StandardOutput.Peek() == -1)
                {
                    await Task.Delay(50, ct);
                    continue;
                }
                
                var line = await process.StandardOutput.ReadLineAsync(ct);
                if (line != null)
                {
                    outputLines.Add(line);
                    _logger.LogDebug("[AI-Pipeline-stdout] {Line}", line);
                }
            }
        }, ct);
        
        var errorTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !process.HasExited)
            {
                if (process.StandardError.Peek() == -1)
                {
                    await Task.Delay(50, ct);
                    continue;
                }
                
                var line = await process.StandardError.ReadLineAsync(ct);
                if (line != null)
                {
                    errorLines.Add(line);
                    _logger.LogWarning("[AI-Pipeline-stderr] {Line}", line);
                }
            }
        }, ct);

        // 等待完成或超时
        var timeoutMs = (timeout ?? _config.DefaultTimeoutSeconds) * 1000;
        var completedTask = await Task.WhenAny(
            Task.Run(() => process.WaitForExit(), ct),
            Task.Delay(timeoutMs, ct));

        if (completedTask.Id != Task.Run(() => process.WaitForExit(), ct).Id)
        {
            // 超时
            try
            {
                process.Kill();
            }
            catch { }
            
            _logger.LogWarning("[AI-Pipeline] Pipeline 执行超时 - TaskID={TaskId}, Timeout={Timeout}s", 
                prompt.Substring(0, Math.Min(8, prompt.Length)), timeout);
            
            return new PipelineExecutionResult
            {
                TaskId = prompt.Substring(0, Math.Min(8, prompt.Length)),
                Success = false,
                ErrorMessage = $"执行超时 ({timeout ?? _config.DefaultTimeoutSeconds}秒)",
                WorkDirectory = workDir,
                Logs = outputLines
            };
        }

        await Task.WhenAll(outputTask, errorTask);

        var success = process.ExitCode == 0;
        var output = string.Join("\n", outputLines);
        var error = string.Join("\n", errorLines);

        if (!success)
        {
            _logger.LogError("[AI-Pipeline] Pipeline 执行失败 - ExitCode={ExitCode}, Error={Error}", 
                process.ExitCode, error);
        }

        return new PipelineExecutionResult
        {
            TaskId = prompt.Substring(0, Math.Min(8, prompt.Length)),
            Success = success,
            WorkDirectory = workDir,
            Output = output,
            ErrorOutput = error,
            Logs = outputLines,
            GeneratedFiles = await GetGeneratedFilesAsync(workDir)
        };
    }

    /// <summary>
    /// 将 AI 生成结果复制到 NetYamlForge 项目目录
    /// </summary>
    private async Task CopyResultsToProjectAsync(
        string sourceDir, 
        string targetDir, 
        CancellationToken ct)
    {
        _logger.LogInformation("[AI-Pipeline] 复制生成结果到项目 - Target={Target}", targetDir);

        // 确保目标目录存在
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // 获取源目录中的所有文件
        var files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
        
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            // 计算相对路径
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var targetPath = Path.Combine(targetDir, relativePath);

            // 确保目标子目录存在
            var targetSubDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetSubDir) && !Directory.Exists(targetSubDir))
            {
                Directory.CreateDirectory(targetSubDir);
            }

            // 复制文件
            File.Copy(file, targetPath, overwrite: true);
            _logger.LogDebug("[AI-Pipeline] 已复制文件 - {RelativePath}", relativePath);
        }

        _logger.LogInformation("[AI-Pipeline] 复制完成 - 共 {Count} 个文件", files.Length);
    }

    /// <summary>
    /// 获取工作目录中生成的文件列表
    /// </summary>
    private async Task<List<string>> GetGeneratedFilesAsync(string workDir)
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(workDir))
                return new List<string>();

            return Directory.GetFiles(workDir, "*.*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(workDir, f))
                .ToList();
        });
    }
}

/// <summary>
/// AI Pipeline 配置选项
/// </summary>
public class AiPipelineConfig
{
    /// <summary>
    /// Harness 根目录路径
    /// </summary>
    public string? HarnessDirectory { get; set; }

    /// <summary>
    /// Harness HTTP API 端点（如果作为独立服务运行）
    /// 例如: http://localhost:10000
    /// </summary>
    public string? HarnessHttpEndpoint { get; set; }

    /// <summary>
    /// Harness 工作目录（用于存储临时文件和日志）
    /// </summary>
    public string HarnessWorkDirectory { get; set; } = "/tmp/nyf-harness";

    /// <summary>
    /// Python 可执行文件路径
    /// </summary>
    public string? PythonExecutable { get; set; }

    /// <summary>
    /// 默认超时秒数
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 3600;
}

/// <summary>
/// Pipeline 执行结果
/// </summary>
public class PipelineExecutionResult
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 工作目录
    /// </summary>
    public string WorkDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 完整输出日志
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// 错误输出
    /// </summary>
    public string? ErrorOutput { get; set; }

    /// <summary>
    /// 日志行
    /// </summary>
    public List<string> Logs { get; set; } = new();

    /// <summary>
    /// 生成的文件列表
    /// </summary>
    public List<string> GeneratedFiles { get; set; } = new();
}

/// <summary>
/// Harness HTTP API 请求
/// </summary>
internal class HarnessPipelineRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("work_dir")]
    public string WorkDir { get; set; } = string.Empty;

    [JsonPropertyName("pipeline_mode")]
    public string PipelineMode { get; set; } = "full";

    [JsonPropertyName("project_name")]
    public string? ProjectName { get; set; }

    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }
}

/// <summary>
/// Harness HTTP API 响应
/// </summary>
internal class HarnessPipelineResponse
{
    [JsonPropertyName("task_id")]
    public int? TaskId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    [JsonPropertyName("work_dir")]
    public string WorkDir { get; set; } = string.Empty;
}
