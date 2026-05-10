using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// 常驻 CLI 进程实例
/// 通过 stdin/stdout 实现双向 Stream-JSON 通信，支持多轮对话复用
/// </summary>
public class DaemonProcessInstance : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _provider;
    private readonly string _command;
    private readonly IReadOnlyList<string> _daemonArgs;
    private readonly string? _workingDirectory;
    private readonly IReadOnlyDictionary<string, string>? _envVars;
    private readonly CliProcessPoolConfig _poolConfig;
    private readonly DaemonMessageProtocol _protocol;

    private Process? _process;
    private readonly SemaphoreSlim _execLock = new(1, 1);
    private readonly Channel<DaemonRequest> _requestChannel;
    private readonly ConcurrentDictionary<string, Channel<DaemonResponse>> _pendingRequests = new();
    private readonly StringBuilder _stdoutBuffer = new();
    private readonly object _bufferLock = new();

    private Task? _stdoutReaderTask;
    private Task? _stdinWriterTask;
    private CancellationTokenSource? _processCts;
    private DateTime _createdAt;
    private DateTime _lastUsedAt;
    private int _requestCount;
    private bool _isHealthy;
    private string? _sessionId;

    public int ProcessId => _process?.Id ?? -1;
    public string Provider => _provider;
    public int RequestCount => _requestCount;
    public bool IsHealthy => _isHealthy && _process != null && !_process.HasExited;
    public bool IsBusy => _pendingRequests.Count > 0;
    public TimeSpan Lifetime => DateTime.UtcNow - _createdAt;
    public TimeSpan IdleTime => DateTime.UtcNow - _lastUsedAt;
    public string? SessionId => _sessionId;

    public DaemonProcessInstance(
        string provider,
        string command,
        IReadOnlyList<string> daemonArgs,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? envVars,
        CliProcessPoolConfig poolConfig,
        ILogger logger)
    {
        _provider = provider;
        _command = command;
        _daemonArgs = daemonArgs;
        _workingDirectory = workingDirectory;
        _envVars = envVars;
        _poolConfig = poolConfig;
        _logger = logger;
        _protocol = DaemonMessageProtocol.ForProvider(provider);
        _requestChannel = Channel.CreateBounded<DaemonRequest>(new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        _createdAt = DateTime.UtcNow;
        _lastUsedAt = DateTime.UtcNow;
    }

    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= _poolConfig.MaxStartRetries; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "[常驻进程] 启动: Provider={Provider}, 尝试={Attempt}/{MaxRetries}, Command={Command}",
                    _provider, attempt, _poolConfig.MaxStartRetries, _command);

                var startInfo = new ProcessStartInfo
                {
                    FileName = _command,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _workingDirectory ?? Directory.GetCurrentDirectory()
                };

                foreach (var arg in _daemonArgs)
                    startInfo.ArgumentList.Add(arg);

                if (_envVars != null)
                {
                    foreach (var kvp in _envVars)
                        startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }

                _processCts = new CancellationTokenSource();
                _process = new Process { StartInfo = startInfo };

                _process.Start();
                _isHealthy = true;
                _createdAt = DateTime.UtcNow;
                _lastUsedAt = DateTime.UtcNow;

                _stdoutReaderTask = Task.Run(() => ReadStdoutLoopAsync(_processCts.Token));
                _stdinWriterTask = Task.Run(() => WriteStdinLoopAsync(_processCts.Token));

                await WaitForReadyAsync(_processCts.Token);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[常驻进程] 启动失败: Provider={Provider}", _provider);
                CleanupProcess();
                if (attempt == _poolConfig.MaxStartRetries) return false;
                await Task.Delay(1000 * attempt, ct);
            }
        }
        return false;
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (!cts.IsCancellationRequested)
            {
                lock (_bufferLock)
                {
                    if (_stdoutBuffer.Length > 0) return;
                }
                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReadStdoutLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process != null && !_process.StandardOutput.EndOfStream)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct);
                if (line == null) continue;

                lock (_bufferLock)
                {
                    _stdoutBuffer.AppendLine(line);
                }

                if (line.TrimStart().StartsWith('{'))
                {
                    await TryProcessJsonMessageAsync(line, ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[常驻进程] Stdout 读取异常: Provider={Provider}", _provider);
            _isHealthy = false;
        }
    }

    private async Task TryProcessJsonMessageAsync(string line, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var msgType = typeEl.GetString();

            if (root.TryGetProperty("session_id", out var sidEl) && sidEl.ValueKind == JsonValueKind.String)
                _sessionId = sidEl.GetString();

            // 路由到所有正在进行的请求（在常驻模式下通常只有一个请求）
            foreach (var channel in _pendingRequests.Values)
            {
                var isComplete = _protocol.IsResponseComplete(msgType, root);
                await channel.Writer.WriteAsync(new DaemonResponse
                {
                    JsonLine = line,
                    JsonElement = root.Clone(),
                    IsComplete = isComplete
                }, ct);
                
                if (isComplete)
                {
                    // 注意：这里不主动移除，由 SendMessageAsync 自行结束并清理
                }
            }
        }
        catch { }
    }

    private async Task WriteStdinLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var request in _requestChannel.Reader.ReadAllAsync(ct))
            {
                if (_process?.StandardInput == null) continue;

                var json = _protocol.FormatRequest(request.Message, request.SessionId, request.SystemPromptOverride, request.AllowedTools);
                await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
                await _process.StandardInput.FlushAsync(ct);
            }
        }
        catch { }
    }

    public async IAsyncEnumerable<DaemonResponse> SendMessageAsync(
        string message,
        string? sessionId = null,
        string? systemPromptOverride = null,
        List<string>? allowedTools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsHealthy) throw new InvalidOperationException("进程不健康");

        await _execLock.WaitAsync(ct);
        var requestId = Guid.NewGuid().ToString();
        var channel = Channel.CreateUnbounded<DaemonResponse>();
        _pendingRequests[requestId] = channel;

        try
        {
            _sessionId = sessionId ?? _sessionId;
            await _requestChannel.Writer.WriteAsync(new DaemonRequest
            {
                Message = message,
                SessionId = _sessionId,
                SystemPromptOverride = systemPromptOverride,
                AllowedTools = allowedTools,
                ResponseChannel = channel
            }, ct);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            while (await channel.Reader.WaitToReadAsync(linkedCts.Token))
            {
                while (channel.Reader.TryRead(out var response))
                {
                    yield return response;
                    if (response.IsComplete)
                    {
                        _requestCount++;
                        _lastUsedAt = DateTime.UtcNow;
                        yield break;
                    }
                }
            }
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
            _execLock.Release();
        }
    }

    public async Task<string> SendMessageAndGetResultAsync(
        string message,
        string? sessionId = null,
        string? systemPromptOverride = null,
        List<string>? allowedTools = null,
        CancellationToken ct = default)
    {
        var fullText = new StringBuilder();
        await foreach (var response in SendMessageAsync(message, sessionId, systemPromptOverride, allowedTools, ct))
        {
            if (response.IsComplete)
                return _protocol.ExtractResult(response.JsonElement);
            
            // 累积可能的中间文本（视协议而定）
        }
        throw new InvalidOperationException("未收到完整响应");
    }

    public bool HealthCheck()
    {
        if (_process == null || _process.HasExited) return false;
        if (IdleTime.TotalMinutes > _poolConfig.IdleTimeoutMinutes) return false;
        return _isHealthy;
    }

    public void Touch() => _lastUsedAt = DateTime.UtcNow;

    private void CleanupProcess()
    {
        try
        {
            _processCts?.Cancel();
            _process?.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            _isHealthy = false;
        }
    }

    public void Dispose()
    {
        CleanupProcess();
        _process?.Dispose();
        _processCts?.Dispose();
        _execLock.Dispose();
    }

    public Dictionary<string, object> GetStats() => new()
    {
        ["provider"] = _provider,
        ["pid"] = ProcessId,
        ["healthy"] = IsHealthy,
        ["requestCount"] = _requestCount,
        ["idleMinutes"] = Math.Round(IdleTime.TotalMinutes, 2)
    };
}

internal class DaemonRequest
{
    public required string Message { get; init; }
    public string? SessionId { get; init; }
    public string? SystemPromptOverride { get; init; }
    public List<string>? AllowedTools { get; init; }
    public required Channel<DaemonResponse> ResponseChannel { get; init; }
}

public class DaemonResponse
{
    public required string JsonLine { get; init; }
    public required JsonElement JsonElement { get; init; }
    public bool IsComplete { get; init; }
}
