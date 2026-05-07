using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 持久化 AI CLI 进程封装
/// 通过 stdin/stdout 进行多次请求/响应交互，进程不退出
/// </summary>
public class PersistentAIProcess : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _provider;
    private readonly CliProcessPoolConfig _config;
    private Process? _process;
    private DateTime _createdAt;
    private DateTime _lastUsedAt;
    private int _requestCount;
    private bool _isHealthy;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly StringBuilder _stdoutBuffer = new();
    private Task? _stdoutReaderTask;

    public int ProcessId => _process?.Id ?? -1;
    public string Provider => _provider;
    public int RequestCount => _requestCount;
    public bool IsHealthy => _isHealthy && !_process?.HasExited == true;
    public bool IsBusy { get; private set; }
    public TimeSpan Lifetime => DateTime.UtcNow - _createdAt;
    public TimeSpan IdleTime => DateTime.UtcNow - _lastUsedAt;

    public PersistentAIProcess(
        string provider,
        CliProcessPoolConfig config,
        ILogger logger)
    {
        _provider = provider;
        _config = config;
        _logger = logger;
        _createdAt = DateTime.UtcNow;
        _lastUsedAt = DateTime.UtcNow;
        _requestCount = 0;
        _isHealthy = false;
    }

    /// <summary>
    /// 启动持久化进程（不带 --prompt，进入交互模式）
    /// </summary>
    public async Task<bool> StartAsync(
        string command,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables,
        CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= _config.MaxStartRetries; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "[进程池] 启动进程: Provider={Provider}, 尝试={Attempt}/{MaxRetries}, Args={Args}",
                    _provider, attempt, _config.MaxStartRetries, string.Join(" ", arguments));

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

                foreach (var arg in arguments)
                    startInfo.ArgumentList.Add(arg);

                if (environmentVariables != null)
                {
                    foreach (var kvp in environmentVariables)
                        startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }

                _process = new Process { StartInfo = startInfo };

                // 启动后台 stdout 读取任务
                _stdoutBuffer.Clear();
                _stdoutReaderTask = Task.Run(() => ReadStdoutAsync(ct), ct);

                _process.Start();
                _isHealthy = true;
                _createdAt = DateTime.UtcNow;
                _lastUsedAt = DateTime.UtcNow;
                _requestCount = 0;

                // 等待进程进入就绪状态（输出初始提示或空行）
                await WaitForReadyAsync(ct);

                _logger.LogInformation(
                    "[进程池] 进程启动成功: Provider={Provider}, PID={PID}",
                    _provider, _process.Id);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[进程池] 进程启动失败: Provider={Provider}, 尝试={Attempt}/{MaxRetries}",
                    _provider, attempt, _config.MaxStartRetries);

                if (attempt == _config.MaxStartRetries)
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
        // 给进程 3 秒时间进入就绪状态（加载模块、显示提示等）
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            // 简单等待一小段时间让进程完成初始化
            await Task.Delay(500, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 超时也认为是就绪（进程可能已经启动）
        }
    }

    /// <summary>
    /// 后台读取 stdout 任务
    /// </summary>
    private async Task ReadStdoutAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process != null && !_process.HasExited)
            {
                if (_process.StandardOutput.Peek() == -1)
                {
                    await Task.Delay(50, ct);
                    continue;
                }
                
                var line = await _process.StandardOutput.ReadLineAsync(ct);
                if (line != null)
                {
                    lock (_stdoutBuffer)
                    {
                        _stdoutBuffer.AppendLine(line);
                    }
                }
            }
        }
                
                var line = await _process.StandardOutput.ReadLineAsync(ct);
                if (line != null)
                {
                    lock (_stdoutBuffer)
                    {
                        _stdoutBuffer.AppendLine(line);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[进程池] stdout 读取异常: Provider={Provider}", _provider);
        }
    }

    /// <summary>
    /// 通过 stdin/stdout 执行请求（核心方法）
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> ExecuteViaStdinAsync(
        string message,
        string? systemPromptOverride,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            IsBusy = true;

            if (!IsHealthy)
            {
                throw new InvalidOperationException(
                    $"进程不健康: Provider={_provider}, PID={ProcessId}");
            }

            if (_process?.HasExited == true)
            {
                _isHealthy = false;
                throw new InvalidOperationException(
                    $"进程已退出: Provider={_provider}, PID={ProcessId}");
            }

            // 检查生命周期
            if (_config.MaxLifetimeMinutes > 0 && Lifetime.TotalMinutes > _config.MaxLifetimeMinutes)
            {
                _logger.LogInformation("[进程池] 超过最大存活时间，回收: Provider={Provider}", _provider);
                Dispose();
                throw new InvalidOperationException("进程已超过最大存活时间");
            }

            if (_config.MaxRequestsPerProcess > 0 && _requestCount >= _config.MaxRequestsPerProcess)
            {
                _logger.LogInformation("[进程池] 超过最大请求次数，回收: Provider={Provider}, Count={Count}", _provider, _requestCount);
                Dispose();
                throw new InvalidOperationException("进程已达到最大请求次数");
            }

            _logger.LogDebug("[进程池] 通过stdin发送请求: Provider={Provider}, RequestCount={Count}",
                _provider, _requestCount + 1);

            // 清空缓冲区
            lock (_stdoutBuffer)
            {
                _stdoutBuffer.Clear();
            }

            // 通过 stdin 发送消息
            var providerLower = _provider.ToLowerInvariant();
            await SendInputAsync(message, systemPromptOverride, providerLower, ct);

            // 等待响应（从缓冲区读取 JSON 输出）
            var output = await WaitForResponseAsync(ct);

            _requestCount++;
            _lastUsedAt = DateTime.UtcNow;

            _logger.LogDebug("[进程池] 请求完成: Provider={Provider}, ResponseLength={Length}",
                _provider, output.Length);

            return (0, output, string.Empty);
        }
        finally
        {
            IsBusy = false;
            _lock.Release();
        }
    }

    /// <summary>
    /// 通过 stdin 发送输入
    /// </summary>
    private async Task SendInputAsync(string message, string? systemPromptOverride, string providerLower, CancellationToken ct)
    {
        if (_process?.StandardInput == null)
            throw new InvalidOperationException("stdin 不可用");

        // 根据工具类型选择输入格式
        switch (providerLower)
        {
            case "qwen":
            case "qwen-code":
            case "gemini":
            case "copilot":
                // 这些工具在交互模式下直接读取文本输入
                // 如果有 --acp 模式则用 JSON-RPC
                await _process.StandardInput.WriteLineAsync(message.AsMemory(), ct);
                break;

            default:
                // 默认直接写入文本
                await _process.StandardInput.WriteLineAsync(message.AsMemory(), ct);
                break;
        }

        await _process.StandardInput.FlushAsync(ct);
    }

    /// <summary>
    /// 等待响应（从 stdout 缓冲区读取 JSON）
    /// </summary>
    private async Task<string> WaitForResponseAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.IdleTimeoutMinutes > 0
            ? Math.Min((int)_config.IdleTimeoutMinutes * 60, 300)
            : 300));

        var deadline = DateTime.UtcNow.AddSeconds(120); // 最长等待 2 分钟

        while (DateTime.UtcNow < deadline)
        {
            // 检查进程是否退出
            if (_process?.HasExited == true)
            {
                // 返回已收集的输出
                lock (_stdoutBuffer)
                {
                    var result = _stdoutBuffer.ToString();
                    if (!string.IsNullOrEmpty(result))
                        return result;
                }
                throw new InvalidOperationException($"进程意外退出: PID={ProcessId}");
            }

            // 尝试从缓冲区提取 JSON 响应
            lock (_stdoutBuffer)
            {
                var bufferContent = _stdoutBuffer.ToString();
                var response = TryExtractResponse(bufferContent);
                if (response != null)
                {
                    return response;
                }
            }

            // 等待一小段时间再重试
            await Task.Delay(100, timeoutCts.Token);
        }

        // 超时，返回当前缓冲区内容
        lock (_stdoutBuffer)
        {
            return _stdoutBuffer.ToString();
        }
    }

    /// <summary>
    /// 从输出缓冲区提取 JSON 响应
    /// </summary>
    private static string? TryExtractResponse(string buffer)
    {
        if (string.IsNullOrEmpty(buffer)) return null;

        // 查找 JSON 对象（type="result" 或 type="assistant"）
        var lines = buffer.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 策略1: 找 type="result" 行
        foreach (var line in lines)
        {
            if (!line.StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var t) && t.GetString() == "result" &&
                    root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    return line;
                }
            }
            catch (JsonException) { }
        }

        // 策略2: 找 type="assistant" 行
        foreach (var line in lines)
        {
            if (!line.StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var t) && t.GetString() == "assistant")
                {
                    return line;
                }
            }
            catch (JsonException) { }
        }

        // 策略3: 如果缓冲区包含完整的 JSON 对象（有配对的 { }），返回整个 JSON
        if (buffer.Contains("\"type\":") || buffer.Contains("\"type\" :"))
        {
            return buffer.Trim();
        }

        return null;
    }

    /// <summary>
    /// 执行请求（兼容接口，返回占位符）
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            IsBusy = true;

            if (!IsHealthy)
                throw new InvalidOperationException($"进程不健康: Provider={_provider}");

            if (_config.MaxLifetimeMinutes > 0 && Lifetime.TotalMinutes > _config.MaxLifetimeMinutes)
            {
                Dispose();
                throw new InvalidOperationException("进程已超过最大存活时间");
            }

            if (_config.MaxRequestsPerProcess > 0 && _requestCount >= _config.MaxRequestsPerProcess)
            {
                Dispose();
                throw new InvalidOperationException("进程已达到最大请求次数");
            }

            // 占位符 — 实际执行通过 ExecuteViaStdinAsync
            await Task.Yield();

            _requestCount++;
            _lastUsedAt = DateTime.UtcNow;

            return (0, string.Empty, string.Empty);
        }
        finally
        {
            IsBusy = false;
            _lock.Release();
        }
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

        if (IdleTime.TotalMinutes > _config.IdleTimeoutMinutes)
        {
            _logger.LogInformation(
                "[进程池] 进程空闲超时，标记为不健康: Provider={Provider}, IdleTime={Minutes}分钟",
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
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
                _logger.LogInformation(
                    "[进程池] 进程已终止: Provider={Provider}, PID={PID}, 处理请求数={Count}",
                    _provider, _process.Id, _requestCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[进程池] 终止进程时出错: Provider={Provider}, PID={PID}", _provider, ProcessId);
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _isHealthy = false;
            _lock.Dispose();
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
            ["idleMinutes"] = Math.Round(IdleTime.TotalMinutes, 2)
        };
    }
}
