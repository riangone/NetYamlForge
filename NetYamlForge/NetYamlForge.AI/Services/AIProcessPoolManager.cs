using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Services;

/// <summary>
/// AI CLI 进程池管理器
/// 负责管理多个持久化 CLI 进程的生命周期
/// </summary>
public class CliProcessPoolManager : IDisposable
{
    private readonly ILogger<CliProcessPoolManager> _logger;
    private readonly CliProcessPoolConfig _poolConfig;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PersistentAIProcess>> _pools = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private readonly Timer _healthCheckTimer;
    private bool _disposed;

    public CliProcessPoolManager(
        ILogger<CliProcessPoolManager> logger,
        CliProcessPoolConfig poolConfig)
    {
        _logger = logger;
        _poolConfig = poolConfig;

        _healthCheckTimer = new Timer(
            callback: HealthCheckCallback,
            state: null,
            dueTime: TimeSpan.FromSeconds(_poolConfig.HealthCheckIntervalSeconds),
            period: TimeSpan.FromSeconds(_poolConfig.HealthCheckIntervalSeconds));

        _logger.LogInformation(
            "[进程池] 进程池管理器初始化: MaxPoolSize={MaxPoolSize}, IdleTimeout={IdleTimeout}分钟, HealthCheck={HealthCheck}秒",
            _poolConfig.MaxPoolSize,
            _poolConfig.IdleTimeoutMinutes,
            _poolConfig.HealthCheckIntervalSeconds);
    }

    /// <summary>
    /// 从池中获取或创建进程
    /// </summary>
    public async Task<PersistentAIProcess> AcquireProcessAsync(
        string provider,
        Func<Task<PersistentAIProcess>> createProcessFunc,
        CancellationToken ct = default)
    {
        if (!_poolConfig.EnableDaemonMode)
        {
            _logger.LogDebug("[进程池] 守护进程模式未启用，跳过池获取");
            return await createProcessFunc();
        }

        var semaphore = _semaphores.GetOrAdd(
            provider,
            _ => new SemaphoreSlim(_poolConfig.MaxPoolSize, _poolConfig.MaxPoolSize));

        await semaphore.WaitAsync(ct);

        try
        {
            var pool = _pools.GetOrAdd(provider, _ => new ConcurrentQueue<PersistentAIProcess>());

            // 尝试从队列中获取健康的空闲进程
            while (pool.TryDequeue(out var process))
            {
                if (process.IsHealthy && !process.IsBusy)
                {
                    _logger.LogDebug(
                        "[进程池] 从池中获取进程: Provider={Provider}, PID={PID}, Requests={Count}",
                        provider, process.ProcessId, process.RequestCount);
                    process.Touch();
                    return process;
                }
                else
                {
                    _logger.LogDebug(
                        "[进程池] 丢弃不健康/忙碌的进程: Provider={Provider}, PID={PID}, Healthy={Healthy}, Busy={Busy}",
                        provider, process.ProcessId, process.IsHealthy, process.IsBusy);
                    process.Dispose();
                }
            }

            // 池中没有可用进程，创建新进程
            _logger.LogInformation(
                "[进程池] 池中无可用进程，创建新进程: Provider={Provider}",
                provider);

            var newProcess = await createProcessFunc();
            return newProcess;
        }
        catch
        {
            // 发生异常时释放信号量
            semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// 将进程归还到池中
    /// </summary>
    public void ReturnProcess(string provider, PersistentAIProcess process)
    {
        if (!_poolConfig.EnableDaemonMode)
        {
            _logger.LogDebug("[进程池] 守护进程模式未启用，跳过池归还");
            process.Dispose();
            return;
        }

        if (!process.IsHealthy)
        {
            _logger.LogInformation(
                "[进程池] 丢弃不健康进程: Provider={Provider}, PID={PID}",
                provider, process.ProcessId);
            process.Dispose();
        }
        else
        {
            var pool = _pools.GetOrAdd(provider, _ => new ConcurrentQueue<PersistentAIProcess>());

            // 检查池大小是否超过限制
            if (pool.Count < _poolConfig.MaxPoolSize)
            {
                pool.Enqueue(process);
                _logger.LogDebug(
                    "[进程池] 进程归还到池: Provider={Provider}, PID={PID}, 池大小={Size}",
                    provider, process.ProcessId, pool.Count);
            }
            else
            {
                _logger.LogInformation(
                    "[进程池] 池已满，丢弃进程: Provider={Provider}, PID={PID}, 池大小={Size}",
                    provider, process.ProcessId, pool.Count);
                process.Dispose();
            }
        }

        // 释放信号量
        if (_semaphores.TryGetValue(provider, out var semaphore))
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 清理指定提供者的进程池
    /// </summary>
    public void ClearPool(string provider)
    {
        if (_pools.TryRemove(provider, out var pool))
        {
            _logger.LogInformation("[进程池] 清理进程池: Provider={Provider}, 进程数={Count}", provider, pool.Count);
            foreach (var process in pool)
            {
                process.Dispose();
            }
        }

        if (_semaphores.TryRemove(provider, out var semaphore))
        {
            semaphore.Dispose();
        }
    }

    /// <summary>
    /// 清理所有进程池
    /// </summary>
    public void ClearAllPools()
    {
        _logger.LogInformation("[进程池] 清理所有进程池");
        foreach (var kvp in _pools)
        {
            foreach (var process in kvp.Value)
            {
                process.Dispose();
            }
        }
        _pools.Clear();

        foreach (var semaphore in _semaphores.Values)
        {
            semaphore.Dispose();
        }
        _semaphores.Clear();
    }

    /// <summary>
    /// 获取所有进程池的统计信息
    /// </summary>
    public Dictionary<string, Dictionary<string, object>> GetPoolStats()
    {
        var stats = new Dictionary<string, Dictionary<string, object>>();

        foreach (var kvp in _pools)
        {
            var provider = kvp.Key;
            var pool = kvp.Value;

            var healthyCount = pool.Count(p => p.IsHealthy);
            var busyCount = pool.Count(p => p.IsBusy);
            var totalRequests = pool.Sum(p => p.RequestCount);

            stats[provider] = new Dictionary<string, object>
            {
                ["poolSize"] = pool.Count,
                ["healthyCount"] = healthyCount,
                ["busyCount"] = busyCount,
                ["totalRequests"] = totalRequests,
                ["maxPoolSize"] = _poolConfig.MaxPoolSize,
                ["processes"] = pool.Select(p => p.GetStats()).ToList()
            };
        }

        return stats;
    }

    /// <summary>
    /// 定期健康检查回调
    /// </summary>
    private void HealthCheckCallback(object? state)
    {
        if (_disposed) return;

        try
        {
            foreach (var kvp in _pools)
            {
                var provider = kvp.Key;
                var pool = kvp.Value;

                var processesToRemove = new List<PersistentAIProcess>();

                foreach (var process in pool)
                {
                    if (!process.HealthCheck())
                    {
                        processesToRemove.Add(process);
                    }
                }

                foreach (var process in processesToRemove)
                {
                    if (pool.TryDequeue(out var removed))
                    {
                        removed.Dispose();
                        _logger.LogDebug(
                            "[进程池] 健康检查回收进程: Provider={Provider}, PID={PID}",
                            provider, process.ProcessId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[进程池] 健康检查异常");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _healthCheckTimer.Dispose();
        ClearAllPools();

        foreach (var semaphore in _semaphores.Values)
        {
            semaphore.Dispose();
        }

        _logger.LogInformation("[进程池] 进程池管理器已释放");
    }
}

/// <summary>
/// AIProcessPoolManager 别名（向后兼容）
/// </summary>
public class AIProcessPoolManager : CliProcessPoolManager
{
    public AIProcessPoolManager(
        ILogger<CliProcessPoolManager> logger,
        CliProcessPoolConfig poolConfig)
        : base(logger, poolConfig)
    {
    }
}
