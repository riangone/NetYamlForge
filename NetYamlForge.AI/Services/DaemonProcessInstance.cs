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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DaemonResponse>> _pendingRequests = new();
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
    public bool IsHealthy => _isHealthy && !_process?.HasExited == true;
    public bool IsBusy => _pendingRequests.Count > 0;
    public TimeSpan Lifetime => DateTime.UtcNow - _createdAt;
    public TimeSpan IdleTime => DateTime.UtcNow - _lastUsedAt;
    public string? SessionId => _sessionId;

    /// <summary>
    /// 创建常驻进程实例（不启动）
    /// </summary>
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

    /// <summary>
    /// 启动常驻进程
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= _poolConfig.MaxStartRetries; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "[常驻进程] 启动: Provider={Provider}, 尝试={Attempt}/{MaxRetries}, Command={Command}, Args={Args}",
                    _provider, attempt, _poolConfig.MaxStartRetries, _command, string.Join(" ", _daemonArgs));

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

                // 启动后台读写任务
                _stdoutReaderTask = Task.Run(() => ReadStdoutLoopAsync(_processCts.Token), _processCts.Token);
                _stdinWriterTask = Task.Run(() => WriteStdinLoopAsync(_processCts.Token), _processCts.Token);

                // 等待进入就绪状态
                await WaitForReadyAsync(_processCts.Token);

                _logger.LogInformation(
                    "[常驻进程] 启动成功: Provider={Provider}, PID={PID}",
                    _provider, _process.Id);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[常驻进程] 启动失败: Provider={Provider}, 尝试={Attempt}/{MaxRetries}",
                    _provider, attempt, _poolConfig.MaxStartRetries);

                if (attempt == _poolConfig.MaxStartRetries)
                {
                    _isHealthy = false;
                    CleanupProcess();
                    return false;
                }

                CleanupProcess();
                await Task.Delay(1000 * attempt, ct);
            }
        }

        return false;
    }

    /// <summary>
    /// 等待进程进入就绪状态
    /// </summary>
    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            // 等待初始提示或就绪信号
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                lock (_bufferLock)
                {
                    var buffer = _stdoutBuffer.ToString();
                    // 检测到初始输出（欢迎信息或提示符）
                    if (!string.IsNullOrEmpty(buffer))
                    {
                        _logger.LogDebug("[常驻进程] 初始化输出: {Output}", buffer.Substring(0, Math.Min(200, buffer.Length)));
                        return;
                    }
                }
                await Task.Delay(200, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 超时也认为是就绪
        }
    }

    /// <summary>
    /// 后台 stdout 读取循环
    /// </summary>
    private async Task ReadStdoutLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_process?.StandardOutput.EndOfStream == false && !ct.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct);
                if (line == null) continue;

                lock (_bufferLock)
                {
                    _stdoutBuffer.AppendLine(line);
                }

                // 尝试解析 JSON 消息
                await TryProcessJsonMessageAsync(line, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[常驻进程] stdout 读取异常: Provider={Provider}", _provider);
            _isHealthy = false;
        }
    }

    /// <summary>
    /// 尝试解析 JSON 消息并路由到对应请求
    /// </summary>
    private async Task TryProcessJsonMessageAsync(string line, CancellationToken ct)
    {
        if (!line.TrimStart().StartsWith('{')) return;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl)) return;
            var msgType = typeEl.GetString();

            _logger.LogDebug("[常驻进程] 收到消息: Type={Type}", msgType);

            // 提取 session_id（如果有）
            if (root.TryGetProperty("session_id", out var sidEl) && sidEl.ValueKind == JsonValueKind.String)
            {
                _sessionId = sidEl.GetString();
            }

            // 路由到 pending request
            foreach (var kvp in _pendingRequests)
            {
                var requestId = kvp.Key;
                var tcs = kvp.Value;

                if (_protocol.IsResponseComplete(msgType, root))
                {
                    var response = new DaemonResponse
                    {
                        JsonLine = line,
                        JsonElement = root.Clone(),
                        IsComplete = true
                    };
                    tcs.TrySetResult(response);
                    _pendingRequests.TryRemove(requestId, out _);
                    return;
                }

                if (_protocol.IsPartialResponse(msgType))
                {
                    var response = new DaemonResponse
                    {
                        JsonLine = line,
                        JsonElement = root.Clone(),
                        IsComplete = false
                    };
                    // 部分响应也通知等待者（流式更新）
                    tcs.TrySetResult(response);
                    return;
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 行，忽略
        }
    }

    /// <summary>
    /// 后台 stdin 写入循环
    /// </summary>
    private async Task WriteStdinLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var request in _requestChannel.Reader.ReadAllAsync(ct))
            {
                if (_process?.StandardInput == null)
                {
                    request.Tcs.TrySetException(new InvalidOperationException("stdin 不可用"));
                    continue;
                }

                try
                {
                    var json = _protocol.FormatRequest(request.Message, request.SessionId, request.SystemPromptOverride, request.AllowedTools);
                    _logger.LogDebug("[常驻进程] 发送请求: {Json}", json.Length > 200 ? json.Substring(0, 200) + "..." : json);

                    await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
                    await _process.StandardInput.FlushAsync(ct);

                    // 注册 pending request
                    var requestId = Guid.NewGuid().ToString();
                    _pendingRequests[requestId] = request.Tcs;

                    // 等待响应
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                    try
                    {
                        var response = await request.Tcs.Task.WaitAsync(TimeSpan.FromSeconds(120), linkedCts.Token);
                        request.OnProgress?.Invoke(response);

                        if (response.IsComplete)
                        {
                            _requestCount++;
                            _lastUsedAt = DateTime.UtcNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        _pendingRequests.TryRemove(requestId, out _);
                        request.Tcs.TrySetException(ex);
                    }
                }
                catch (Exception ex)
                {
                    request.Tcs.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[常驻进程] stdin 写入循环异常");
            _isHealthy = false;
        }
    }

    /// <summary>
    /// 发送消息并获取流式响应
    /// </summary>
    public async IAsyncEnumerable<DaemonResponse> SendMessageAsync(
        string message,
        string? sessionId = null,
        string? systemPromptOverride = null,
        List<string>? allowedTools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsHealthy)
            throw new InvalidOperationException($"进程不健康: Provider={_provider}, PID={ProcessId}");

        if (_poolConfig.MaxLifetimeMinutes > 0 && Lifetime.TotalMinutes > _poolConfig.MaxLifetimeMinutes)
        {
            _logger.LogInformation("[常驻进程] 超过最大存活时间，回收: Provider={Provider}", _provider);
            Dispose();
            throw new InvalidOperationException("进程已超过最大存活时间");
        }

        if (_poolConfig.MaxRequestsPerProcess > 0 && _requestCount >= _poolConfig.MaxRequestsPerProcess)
        {
            _logger.LogInformation("[常驻进程] 超过最大请求次数，回收: Provider={Provider}, Count={Count}", _provider, _requestCount);
            Dispose();
            throw new InvalidOperationException("进程已达到最大请求次数");
        }

        await _execLock.WaitAsync(ct);
        try
        {
            _sessionId = sessionId ?? _sessionId;

            var tcs = new TaskCompletionSource<DaemonResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new DaemonRequest
            {
                Message = message,
                SessionId = _sessionId,
                SystemPromptOverride = systemPromptOverride,
                AllowedTools = allowedTools,
                Tcs = tcs,
                OnProgress = response =>
                {
                    // 流式响应通过 IAsyncEnumerable 返回
                }
            };

            // 入队请求
            await _requestChannel.Writer.WriteAsync(request, ct);

            // 等待并返回流式响应
            var lastResponse = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(120), ct);
            yield return lastResponse;

            // 继续接收后续流式更新（直到 result 类型）
            while (!lastResponse.IsComplete && !ct.IsCancellationRequested)
            {
                var nextTcs = new TaskCompletionSource<DaemonResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                var nextRequest = new DaemonRequest
                {
                    Message = "",  // 空消息，仅等待响应
                    SessionId = _sessionId,
                    Tcs = nextTcs
                };

                // 注意：这里不真正发送请求，只是等待上一个请求的后续更新
                // 实际上应该通过独立的流式监听机制获取
                break;
            }
        }
        finally
        {
            _execLock.Release();
        }
    }

    /// <summary>
    /// 发送消息并获取最终结果（同步）
    /// </summary>
    public async Task<string> SendMessageAndGetResultAsync(
        string message,
        string? sessionId = null,
        string? systemPromptOverride = null,
        List<string>? allowedTools = null,
        CancellationToken ct = default)
    {
        await foreach (var response in SendMessageAsync(message, sessionId, systemPromptOverride, allowedTools, ct))
        {
            if (response.IsComplete)
            {
                return _protocol.ExtractResult(response.JsonElement);
            }
        }
        throw new InvalidOperationException("未收到完整响应");
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    public bool HealthCheck()
    {
        if (_process == null || _process.HasExited)
        {
            _isHealthy = false;
            return false;
        }

        if (IdleTime.TotalMinutes > _poolConfig.IdleTimeoutMinutes)
        {
            _logger.LogInformation(
                "[常驻进程] 进程空闲超时，标记为不健康: Provider={Provider}, IdleTime={Minutes}分钟",
                _provider, IdleTime.TotalMinutes);
            _isHealthy = false;
            return false;
        }

        _isHealthy = true;
        return true;
    }

    public void Touch()
    {
        _lastUsedAt = DateTime.UtcNow;
    }

    private void CleanupProcess()
    {
        try
        {
            _processCts?.Cancel();
            _process?.Kill(entireProcessTree: true);
            _process?.WaitForExit(5000);
        }
        catch
        {
            // 忽略清理错误
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _isHealthy = false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _processCts?.Cancel();
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
                _logger.LogInformation(
                    "[常驻进程] 进程已终止: Provider={Provider}, PID={PID}, 处理请求数={Count}",
                    _provider, _process.Id, _requestCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[常驻进程] 终止进程时出错: Provider={Provider}, PID={PID}", _provider, ProcessId);
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _processCts?.Dispose();
            _isHealthy = false;
            _execLock.Dispose();
        }
    }

    public Dictionary<string, object> GetStats()
    {
        return new Dictionary<string, object>
        {
            ["provider"] = _provider,
            ["pid"] = ProcessId,
            ["healthy"] = IsHealthy,
            ["busy"] = IsBusy,
            ["requestCount"] = _requestCount,
            ["lifetimeMinutes"] = Math.Round(Lifetime.TotalMinutes, 2),
            ["idleMinutes"] = Math.Round(IdleTime.TotalMinutes, 2),
            ["sessionId"] = _sessionId ?? "none"
        };
    }
}

/// <summary>
/// 常驻进程请求
/// </summary>
internal class DaemonRequest
{
    public string Message { get; init; } = string.Empty;
    public string? SessionId { get; init; }
    public string? SystemPromptOverride { get; init; }
    public List<string>? AllowedTools { get; init; }
    public TaskCompletionSource<DaemonResponse> Tcs { get; init; } = new();
    public Action<DaemonResponse>? OnProgress { get; init; }
}

/// <summary>
/// 常驻进程响应
/// </summary>
public class DaemonResponse
{
    public string JsonLine { get; init; } = string.Empty;
    public JsonElement JsonElement { get; init; }
    public bool IsComplete { get; init; }
}
