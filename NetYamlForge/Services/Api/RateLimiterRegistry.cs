using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.Config;

namespace NetYamlForge.Services.Api;

public interface IRateLimiterRegistry
{
    RateLimiter GetOrCreate(string key, Func<RateLimiter> factory);
    long RegisteredCount { get; }
}

/// <summary>
/// RateLimiter の一元管理レジストリ。IHostedService としてバックグラウンドで
/// CacheSweepSeconds 毎に、LimiterIdleTtlSeconds を超えて未使用のキーを掃引して
/// メモリリークを防ぎます。
/// </summary>
public class RateLimiterRegistry : IRateLimiterRegistry, IHostedService
{
    private readonly ConcurrentDictionary<string, LimiterEntry> _registry = new();
    private readonly RateLimitingOptions _options;
    private readonly ILogger<RateLimiterRegistry> _logger;
    private readonly SemaphoreSlim _creationGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _sweepTask;

    public RateLimiterRegistry(IOptions<RateLimitingOptions> options, ILogger<RateLimiterRegistry> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public long RegisteredCount => _registry.Count;

    public RateLimiter GetOrCreate(string key, Func<RateLimiter> factory)
    {
        if (_registry.TryGetValue(key, out var entry))
        {
            entry.LastSeenAt = DateTime.UtcNow;
            return entry.Limiter;
        }

        _creationGate.Wait();
        try
        {
            if (_registry.TryGetValue(key, out entry))
            {
                entry.LastSeenAt = DateTime.UtcNow;
                return entry.Limiter;
            }

            var limiter = factory();
            _registry.TryAdd(key, new LimiterEntry(limiter) { LastSeenAt = DateTime.UtcNow });
            return limiter;
        }
        finally
        {
            _creationGate.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sweepTask = Task.Run(() => SweepLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }

        if (_sweepTask != null)
        {
            try
            {
                await _sweepTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            _cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        await DisposeAllAsync().ConfigureAwait(false);
    }

    private async Task SweepLoopAsync(CancellationToken token)
    {
        var intervalSeconds = Math.Max(_options.CacheSweepSeconds, 1);

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                Sweep();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rate limiter registry sweep failed");
            }
        }
    }

    /// <summary>
    /// アイドル状態が LimiterIdleTtlSeconds を超えたエントリを破棄します。
    /// </summary>
    internal void Sweep()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(_options.LimiterIdleTtlSeconds, 0));

        foreach (var kvp in _registry)
        {
            if (kvp.Value.LastSeenAt < cutoff)
            {
                if (_registry.TryRemove(kvp.Key, out var removed))
                {
                    removed.Limiter.Dispose();
                }
            }
        }

        if (_registry.Count < 100)
        {
            return;
        }

        _logger.LogDebug("Rate limiter registry contains {Count} active limiters", _registry.Count);
    }

    public async ValueTask DisposeAllAsync()
    {
        foreach (var kvp in _registry)
        {
            kvp.Value.Limiter.Dispose();
        }
        _registry.Clear();
    }

    private sealed class LimiterEntry
    {
        public LimiterEntry(RateLimiter limiter) => Limiter = limiter;

        public RateLimiter Limiter { get; }
        public DateTime LastSeenAt { get; set; }
    }
}