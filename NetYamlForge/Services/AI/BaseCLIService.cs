using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TaskStatus = NetYamlForge.Models.AI.TaskStatus;
using NetYamlForge.Models.AI;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 服务基类
/// </summary>
public abstract class BaseCLIService : ICLIService
{
    protected readonly ProcessExecutor Executor;
    protected readonly CliConfig Config;
    protected readonly ILogger Logger;
    protected readonly string _toolName;

    protected BaseCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        ILogger logger,
        string toolName)
    {
        Executor = executor;
        Config = config.Value;
        Logger = logger;
        _toolName = toolName;
    }

    public virtual string ToolName => _toolName;

    public abstract Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default);
    
    public async IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var args = BuildArguments(message, true, sessionId, allowedTools);
        var workingDir = workingDirectory ?? Config.DefaultWorkingDirectory;
        
        await foreach (var line in Executor.ExecuteStreamingAsync(ToolName, args, workingDir, ct))
        {
            // 解析 stream-json 输出
            var update = ParseStreamLine(line);
            if (update != null)
            {
                yield return update;
            }
        }
    }
    
    public async Task<string> ExecuteAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        CancellationToken ct = default)
    {
        var args = BuildArguments(message, false, sessionId, allowedTools);
        var workingDir = workingDirectory ?? Config.DefaultWorkingDirectory;
        
        var result = await Executor.ExecuteAsync(ToolName, args, workingDir, ct);
        
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"CLI failed: {result.Error}");
        }
        
        return result.Output;
    }
    
    public async Task CancelAsync(int processId, CancellationToken ct = default)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct);
                Logger.LogInformation("CLI process {Pid} killed", processId);
            }
        }
        catch (ArgumentException)
        {
            // 进程已不存在
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to kill CLI process {Pid}", processId);
        }
    }
    
    /// <summary>
    /// 构建 CLI 参数（由子类实现）
    /// </summary>
    protected abstract string BuildArguments(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools);
    
    /// <summary>
    /// 解析流式输出行
    /// </summary>
    protected virtual ProgressUpdate? ParseStreamLine(string line)
    {
        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            
            // 根据 type 字段解析不同类型的消息
            if (root.TryGetProperty("type", out var typeProp))
            {
                var type = typeProp.GetString();
                
                return type switch
                {
                    "result" => new ProgressUpdate
                    {
                        Message = root.GetProperty("result").GetString(),
                        Progress = 100,
                        Status = TaskStatus.Completed
                    },
                    "progress" => new ProgressUpdate
                    {
                        Message = root.GetProperty("message").GetString(),
                        Progress = root.TryGetProperty("percentage", out var p) ? p.GetInt32() : 50,
                        Status = TaskStatus.Running
                    },
                    "error" => new ProgressUpdate
                    {
                        Message = root.GetProperty("error").GetString(),
                        Status = TaskStatus.Failed
                    },
                    _ => new ProgressUpdate
                    {
                        Logs = new() { line },
                        Status = TaskStatus.Running
                    }
                };
            }
            
            // 没有 type 字段，尝试解析为结果
            return new ProgressUpdate
            {
                Message = line,
                Status = TaskStatus.Running
            };
        }
        catch (JsonException)
        {
            // 非 JSON 输出，作为日志返回
            return new ProgressUpdate
            {
                Logs = new() { line },
                Status = TaskStatus.Running
            };
        }
    }
}
