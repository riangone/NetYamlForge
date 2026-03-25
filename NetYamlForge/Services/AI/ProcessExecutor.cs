using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 进程执行器（CLI 调用核心）
/// </summary>
public class ProcessExecutor
{
    private readonly ILogger<ProcessExecutor> _logger;
    
    public ProcessExecutor(ILogger<ProcessExecutor> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// 执行 CLI 命令（流式输出）
    /// </summary>
    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        string command,
        string arguments,
        string? workingDirectory = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
            }
        };
        
        _logger.LogInformation("Starting CLI: {Command} {Arguments}", command, arguments);
        
        process.Start();
        _logger.LogInformation("CLI started with PID: {Pid}", process.Id);
        
        // 同时读取标准输出和错误输出
        var outputTask = ReadStreamAsync(process.StandardOutput, ct);
        var errorTask = ReadStreamAsync(process.StandardError, ct);
        
        await foreach (var line in outputTask.WithCancellation(ct))
        {
            yield return line;
        }
        
        await foreach (var line in errorTask.WithCancellation(ct))
        {
            // 错误输出也返回（可能包含有用信息）
            yield return line;
        }
        
        await process.WaitForExitAsync(ct);
        _logger.LogInformation("CLI exited with code: {ExitCode}", process.ExitCode);
    }
    
    /// <summary>
    /// 执行 CLI 命令（一次性）
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        string command,
        string arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
            }
        };
        
        process.Start();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        
        await process.WaitForExitAsync(ct);
        
        return (process.ExitCode, output, error);
    }
    
    private static async IAsyncEnumerable<string> ReadStreamAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line != null)
            {
                yield return line;
            }
        }
    }
}
