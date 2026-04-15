using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services.Providers;
using TaskStatus = NetYamlForge.AI.Models.TaskStatus;

namespace NetYamlForge.AI.Services;

/// <summary>
/// 常驻 CLI 聊天服务
/// 通过进程池管理常驻 CLI 实例，实现高效的 Stream-JSON 双向通信
/// </summary>
public class DaemonChatService
{
    private readonly ConcurrentDictionary<string, DaemonProcessInstance> _activeProcesses = new();
    private readonly ConcurrentDictionary<string, DaemonProcessInstance> _pool = new();
    private readonly ICLIService _innerService;
    private readonly CliConfig _config;
    private readonly CliProcessPoolConfig _poolConfig;
    private readonly ProcessExecutor _executor;
    private readonly SkillLoader _skillLoader;
    private readonly ILogger _logger;
    private readonly string _provider;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public DaemonChatService(
        ICLIService innerService,
        IOptions<CliConfig> config,
        ProcessExecutor executor,
        SkillLoader skillLoader,
        ILogger<DaemonChatService> logger)
    {
        _innerService = innerService;
        _config = config.Value;
        _poolConfig = _config.ProcessPool;
        _executor = executor;
        _skillLoader = skillLoader;
        _logger = logger;
        _provider = innerService.ToolName;

        _cleanupTimer = new Timer(
            callback: CleanupCallback,
            state: null,
            dueTime: TimeSpan.FromSeconds(_poolConfig.HealthCheckIntervalSeconds),
            period: TimeSpan.FromSeconds(_poolConfig.HealthCheckIntervalSeconds));

        _logger.LogInformation(
            "[常驻服务] 初始化: Provider={Provider}, DaemonMode={DaemonMode}, PoolSize={PoolSize}",
            _provider, _poolConfig.EnableDaemonMode, _poolConfig.MaxPoolSize);
    }

    /// <summary>
    /// 执行流式聊天（优先使用常驻进程）
    /// </summary>
    public async IAsyncEnumerable<ProgressUpdate> ChatStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_poolConfig.EnableDaemonMode)
        {
            _logger.LogDebug("[常驻服务] 守护进程模式未启用，回退到标准执行");
            await foreach (var update in _innerService.ExecuteStreamingAsync(
                message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct))
            {
                yield return update;
            }
            yield break;
        }

        var updates = await ExecuteWithDaemonFallbackAsync(
            message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct);

        foreach (var update in updates)
        {
            yield return update;
        }
    }

    /// <summary>
    /// 执行常驻进程，失败时回退到标准执行
    /// </summary>
    private async Task<List<ProgressUpdate>> ExecuteWithDaemonFallbackAsync(
        string message,
        string? workingDirectory,
        string? sessionId,
        List<string>? allowedTools,
        string? systemPromptOverride,
        CancellationToken ct)
    {
        DaemonProcessInstance? process = null;
        bool processAcquired = false;
        var results = new List<ProgressUpdate>();

        try
        {
            process = await AcquireOrCreateProcessAsync(sessionId, ct);
            if (process == null)
            {
                _logger.LogWarning("[常驻服务] 无法获取常驻进程，回退到标准执行");
                return await CollectStreamingResultsAsync(
                    _innerService.ExecuteStreamingAsync(
                        message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct));
            }

            processAcquired = true;
            process.Touch();

            _logger.LogDebug(
                "[常驻服务] 使用常驻进程: Provider={Provider}, PID={PID}, SessionId={SessionId}",
                _provider, process.ProcessId, sessionId);

            await foreach (var response in SendDaemonMessageAsync(
                process, message, sessionId, systemPromptOverride, allowedTools, ct))
            {
                results.Add(response);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[常驻服务] 常驻进程执行异常: Provider={Provider}, Error={Error}",
                _provider, ex.Message);

            if (process != null)
            {
                RemoveProcessFromPool(process);
                process = null;
            }

            _logger.LogInformation("[常驻服务] 回退到标准执行");
            return await CollectStreamingResultsAsync(
                _innerService.ExecuteStreamingAsync(
                    message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct));
        }
        finally
        {
            if (processAcquired && process != null && process.IsHealthy)
            {
                ReturnProcessToPool(process);
            }
            else if (process != null)
            {
                RemoveProcessFromPool(process);
            }
        }
    }

    /// <summary>
    /// 收集流式结果到列表
    /// </summary>
    private async Task<List<ProgressUpdate>> CollectStreamingResultsAsync(
        IAsyncEnumerable<ProgressUpdate> updates)
    {
        var results = new List<ProgressUpdate>();
        await foreach (var update in updates)
        {
            results.Add(update);
        }
        return results;
    }

    /// <summary>
    /// 执行非流式聊天（获取最终结果）
    /// </summary>
    public async Task<string> ChatAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        string? systemPromptOverride = null,
        CancellationToken ct = default)
    {
        if (!_poolConfig.EnableDaemonMode)
        {
            return await _innerService.ExecuteAsync(
                message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct);
        }

        DaemonProcessInstance? process = null;
        bool processAcquired = false;

        try
        {
            process = await AcquireOrCreateProcessAsync(sessionId, ct);
            if (process == null)
            {
                _logger.LogWarning("[常驻服务] 无法获取常驻进程，回退到标准执行");
                return await _innerService.ExecuteAsync(
                    message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct);
            }

            processAcquired = true;
            process.Touch();

            var result = await process.SendMessageAndGetResultAsync(
                message, sessionId, systemPromptOverride, allowedTools, ct);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[常驻服务] 常驻进程执行异常: Provider={Provider}, Error={Error}",
                _provider, ex.Message);

            if (process != null)
            {
                RemoveProcessFromPool(process);
            }

            return await _innerService.ExecuteAsync(
                message, workingDirectory, sessionId, allowedTools, systemPromptOverride, ct);
        }
        finally
        {
            if (processAcquired && process != null && process.IsHealthy)
            {
                ReturnProcessToPool(process);
            }
            else if (process != null)
            {
                RemoveProcessFromPool(process);
            }
        }
    }

    /// <summary>
    /// 获取或创建常驻进程
    /// </summary>
    private async Task<DaemonProcessInstance?> AcquireOrCreateProcessAsync(string? sessionId, CancellationToken ct)
    {
        // 尝试从池中获取健康空闲进程
        foreach (var kvp in _pool)
        {
            var proc = kvp.Value;
            if (proc.IsHealthy && !proc.IsBusy)
            {
                _pool.TryRemove(kvp.Key, out _);
                _activeProcesses[kvp.Key] = proc;
                _logger.LogDebug(
                    "[常驻服务] 从池中获取进程: PID={PID}, Requests={Count}",
                    proc.ProcessId, proc.RequestCount);
                return proc;
            }
        }

        // 创建新进程
        return await CreateNewDaemonProcessAsync(sessionId, ct);
    }

    /// <summary>
    /// 创建新的常驻进程
    /// </summary>
    private async Task<DaemonProcessInstance?> CreateNewDaemonProcessAsync(string? sessionId, CancellationToken ct)
    {
        var command = GetCommandPath();
        var daemonArgs = BuildDaemonArguments(sessionId);
        var workingDir = _config.DefaultWorkingDirectory;
        var envVars = GetEnvironmentVariables();

        var process = new DaemonProcessInstance(
            _provider, command, daemonArgs, workingDir, envVars, _poolConfig, _logger);

        var started = await process.StartAsync(ct);
        if (!started)
        {
            _logger.LogError("[常驻服务] 常驻进程启动失败: Provider={Provider}", _provider);
            return null;
        }

        var processKey = $"{_provider}_{process.ProcessId}";
        _activeProcesses[processKey] = process;

        _logger.LogInformation(
            "[常驻服务] 新常驻进程: Provider={Provider}, PID={PID}, SessionId={SessionId}",
            _provider, process.ProcessId, process.SessionId);

        return process;
    }

    /// <summary>
    /// 通过常驻进程发送消息
    /// </summary>
    private async IAsyncEnumerable<ProgressUpdate> SendDaemonMessageAsync(
        DaemonProcessInstance process,
        string message,
        string? sessionId,
        string? systemPromptOverride,
        List<string>? allowedTools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var protocol = DaemonMessageProtocol.ForProvider(_provider, _logger);

        await foreach (var response in process.SendMessageAsync(
            message, sessionId, systemPromptOverride, allowedTools, ct))
        {
            var progressUpdate = protocol.ParseProgressMessage(response.JsonLine);
            if (progressUpdate != null)
            {
                // 保留 session_id
                if (string.IsNullOrEmpty(progressUpdate.SessionId) && !string.IsNullOrEmpty(process.SessionId))
                {
                    progressUpdate.SessionId = process.SessionId;
                }
                yield return progressUpdate;
            }

            if (response.IsComplete)
                yield break;
        }
    }

    /// <summary>
    /// 将进程归还到池中
    /// </summary>
    private void ReturnProcessToPool(DaemonProcessInstance process)
    {
        var processKey = $"{_provider}_{process.ProcessId}";
        _activeProcesses.TryRemove(processKey, out _);

        if (_pool.Count < _poolConfig.MaxPoolSize)
        {
            _pool[processKey] = process;
            _logger.LogDebug(
                "[常驻服务] 进程归还池: PID={PID}, 池大小={Size}",
                process.ProcessId, _pool.Count);
        }
        else
        {
            _logger.LogInformation(
                "[常驻服务] 池已满，释放进程: PID={PID}",
                process.ProcessId);
            process.Dispose();
        }
    }

    /// <summary>
    /// 从池中移除进程（不健康或异常）
    /// </summary>
    private void RemoveProcessFromPool(DaemonProcessInstance process)
    {
        var processKey = $"{_provider}_{process.ProcessId}";
        _activeProcesses.TryRemove(processKey, out _);
        _pool.TryRemove(processKey, out _);
        process.Dispose();
    }

    /// <summary>
    /// 定期清理不健康/超时的进程
    /// </summary>
    private void CleanupCallback(object? state)
    {
        if (_disposed) return;

        try
        {
            var processesToRemove = new List<DaemonProcessInstance>();

            foreach (var kvp in _pool)
            {
                var process = kvp.Value;
                if (!process.HealthCheck())
                {
                    processesToRemove.Add(process);
                }
            }

            foreach (var process in processesToRemove)
            {
                var processKey = $"{_provider}_{process.ProcessId}";
                _pool.TryRemove(processKey, out _);
                process.Dispose();
                _logger.LogDebug("[常驻服务] 清理不健康进程: PID={PID}", process.ProcessId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[常驻服务] 定期清理异常");
        }
    }

    #region 辅助方法

    private string GetCommandPath()
    {
        return _provider switch
        {
            "qwen" or "qwen-code" or "qwen_code" => string.IsNullOrEmpty(_config.QwenCode.Path) ? "qwen" : _config.QwenCode.Path,
            "claude" or "claude-code" or "claude_code" => string.IsNullOrEmpty(_config.Claude.Path) ? "claude" : _config.Claude.Path,
            _ => _provider
        };
    }

    private List<string> BuildDaemonArguments(string? sessionId)
    {
        var args = new List<string>
        {
            "--dangerously-skip-permissions",
            "--output-format", "stream-json"
        };

        // Qwen Code 特定参数
        if (_provider is "qwen" or "qwen-code" or "qwen_code")
        {
            if (!string.IsNullOrEmpty(_config.QwenCode.Model))
            {
                args.Add("--model");
                args.Add(_config.QwenCode.Model);
            }

            // 会话恢复
            if (!string.IsNullOrEmpty(sessionId) && _poolConfig.EnablePersistentSessions)
            {
                args.Add("--resume");
                args.Add(sessionId);
            }

            // 系统提示
            var frameworkPrompt = _skillLoader.GetSystemPrompt();
            if (!string.IsNullOrEmpty(frameworkPrompt))
            {
                args.Add("--append-system-prompt");
                args.Add(frameworkPrompt);
            }
        }

        // Claude Code 特定参数
        if (_provider is "claude" or "claude-code" or "claude_code")
        {
            args.Add("--dangerously-skip-permissions");

            if (!string.IsNullOrEmpty(_config.Claude.Model))
            {
                args.Add("--model");
                args.Add(_config.Claude.Model);
            }

            if (!string.IsNullOrEmpty(_config.Claude.ChatEffort))
            {
                args.Add("--effort");
                args.Add(_config.Claude.ChatEffort);
            }
        }

        return args;
    }

    private IReadOnlyDictionary<string, string>? GetEnvironmentVariables()
    {
        var env = new Dictionary<string, string>();

        var existingPath = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(existingPath))
        {
            env["PATH"] = existingPath;
        }

        // Qwen Code 环境变量
        if (_provider is "qwen" or "qwen-code" or "qwen_code")
        {
            if (!string.IsNullOrEmpty(_config.QwenCode.ApiKey))
                env["DASHSCOPE_API_KEY"] = _config.QwenCode.ApiKey;
            if (!string.IsNullOrEmpty(_config.QwenCode.BaseUrl))
                env["DASHSCOPE_BASE_URL"] = _config.QwenCode.BaseUrl;
        }

        // Claude Code 环境变量
        if (_provider is "claude" or "claude-code" or "claude_code")
        {
            if (!string.IsNullOrEmpty(_config.Claude.ApiKey))
                env["ANTHROPIC_API_KEY"] = _config.Claude.ApiKey;
        }

        return env.Count > 0 ? env : null;
    }

    #endregion

    #region 公共 API

    /// <summary>
    /// 获取进程池统计
    /// </summary>
    public Dictionary<string, object> GetPoolStats()
    {
        return new Dictionary<string, object>
        {
            ["provider"] = _provider,
            ["activeProcesses"] = _activeProcesses.Count,
            ["pooledProcesses"] = _pool.Count,
            ["maxPoolSize"] = _poolConfig.MaxPoolSize,
            ["processes"] = _pool.Select(p => p.Value.GetStats()).ToList()
        };
    }

    /// <summary>
    /// 清空进程池
    /// </summary>
    public void ClearPool()
    {
        _logger.LogInformation("[常驻服务] 清空进程池: Provider={Provider}", _provider);

        foreach (var kvp in _pool)
        {
            kvp.Value.Dispose();
        }
        _pool.Clear();

        foreach (var kvp in _activeProcesses)
        {
            kvp.Value.Dispose();
        }
        _activeProcesses.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer.Dispose();
        ClearPool();

        _logger.LogInformation("[常驻服务] 常驻服务已释放: Provider={Provider}", _provider);
    }

    #endregion
}
