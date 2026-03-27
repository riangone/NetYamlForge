using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI プロセス実行器
/// </summary>
public class ProcessExecutor
{
    private readonly ILogger<ProcessExecutor> _logger;

    public ProcessExecutor(ILogger<ProcessExecutor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// CLI をストリーミング実行する（引数リスト版）。
    /// ArgumentList を使うため、引用符エスケープ不要。
    /// stdout / stderr を並行して読み取り、行単位で yield する。
    /// </summary>
    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        string command,
        IReadOnlyList<string> argumentList,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var process = CreateProcess(command, argumentList, workingDirectory, environmentVariables);

        _logger.LogInformation("Starting CLI: {Command} {Arguments}", command, string.Join(" ", argumentList));
        process.Start();
        _logger.LogInformation("CLI started with PID: {Pid}", process.Id);

        // stdout / stderr を並行して読み込む（逐次だとバッファ枯渇でデッドロックする可能性がある）
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = false });

        var stdoutTask = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(ct)) != null)
                    await channel.Writer.WriteAsync(line, ct);
            }
            catch (OperationCanceledException) { /* expected */ }
        }, ct);

        var stderrTask = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync(ct)) != null)
                    await channel.Writer.WriteAsync(line, ct);
            }
            catch (OperationCanceledException) { /* expected */ }
        }, ct);

        _ = Task.WhenAll(stdoutTask, stderrTask).ContinueWith(
            _ => channel.Writer.Complete(), TaskScheduler.Default);

        await foreach (var line in channel.Reader.ReadAllAsync(ct))
        {
            yield return line;
        }

        await process.WaitForExitAsync(ct);
        _logger.LogInformation("CLI exited with code: {ExitCode}", process.ExitCode);
    }

    /// <summary>
    /// CLI を一括実行する（引数リスト版）。
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        string command,
        IReadOnlyList<string> argumentList,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default)
    {
        var process = CreateProcess(command, argumentList, workingDirectory, environmentVariables);
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error  = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, output, error);
    }

    /// <summary>
    /// CLI を一括実行する（単一文字列引数版。simple commands 用）。
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        string command,
        string arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken ct = default)
    {
        var process = CreateProcessFromString(command, arguments, workingDirectory, environmentVariables);
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error  = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, output, error);
    }

    /// <summary>
    /// ArgumentList を使ってプロセスを生成する（推奨）。
    /// 引数は引用符エスケープ不要でそのまま渡せる。
    /// </summary>
    private static Process CreateProcess(
        string command,
        IReadOnlyList<string> argumentList,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        foreach (var arg in argumentList)
            startInfo.ArgumentList.Add(arg);

        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
        }

        return new Process { StartInfo = startInfo };
    }

    /// <summary>
    /// Arguments 文字列を使ってプロセスを生成する（後方互換・simple commands 用）。
    /// </summary>
    private static Process CreateProcessFromString(
        string command,
        string arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
                startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
        }

        return new Process { StartInfo = startInfo };
    }
}
