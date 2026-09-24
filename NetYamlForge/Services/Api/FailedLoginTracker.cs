using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.Config;

namespace NetYamlForge.Services.Api;

public interface IFailedLoginTracker
{
    bool IsLockedOut(string key);
    void RecordFailedAttempt(string key);
    void Reset(string key);
}

public class FailedLoginTracker : IFailedLoginTracker
{
    private readonly ConcurrentDictionary<string, FailedAttemptInfo> _attempts = new();
    private readonly BruteForceProtection _config;
    private readonly ILogger<FailedLoginTracker> _logger;

    public FailedLoginTracker(IOptions<RateLimitingOptions> options, ILogger<FailedLoginTracker> logger)
    {
        _config = options.Value.BruteForce;
        _logger = logger;
    }

    public bool IsLockedOut(string key)
    {
        if (!_config.Enabled) return false;

        if (_attempts.TryGetValue(key, out var info))
        {
            if (info.FailCount >= _config.FailedAttemptThreshold)
            {
                var elapsed = DateTime.UtcNow - info.LastFailureAt;
                if (elapsed.TotalSeconds < _config.LockoutSeconds)
                {
                    _logger.LogWarning("Locked out key {Key}, failed {Count} times, {Elapsed}s / {Lockout}s remaining",
                        key, info.FailCount, (int)(_config.LockoutSeconds - elapsed.TotalSeconds), _config.LockoutSeconds);
                    return true;
                }

                _attempts.TryRemove(key, out _);
            }
        }

        return false;
    }

    public void RecordFailedAttempt(string key)
    {
        if (!_config.Enabled) return;

        _attempts.AddOrUpdate(key,
            _ => new FailedAttemptInfo { FailCount = 1, LastFailureAt = DateTime.UtcNow },
            (_, existing) =>
            {
                var elapsed = DateTime.UtcNow - existing.LastFailureAt;
                if (elapsed.TotalSeconds >= _config.LockoutSeconds)
                {
                    return new FailedAttemptInfo { FailCount = 1, LastFailureAt = DateTime.UtcNow };
                }

                existing.FailCount++;
                existing.LastFailureAt = DateTime.UtcNow;
                return existing;
            });

        if (_attempts.TryGetValue(key, out var info) && info.FailCount >= _config.FailedAttemptThreshold)
        {
            _logger.LogWarning("Brute force threshold reached for key {Key} ({Count} failures)", key, info.FailCount);
        }
    }

    public void Reset(string key)
    {
        _attempts.TryRemove(key, out _);
    }

    private class FailedAttemptInfo
    {
        public int FailCount { get; set; }
        public DateTime LastFailureAt { get; set; }
    }
}
