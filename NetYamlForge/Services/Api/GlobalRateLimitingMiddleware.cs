using System;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.Config;

namespace NetYamlForge.Services.Api;

public class GlobalRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalRateLimitingMiddleware> _logger;
    private readonly RateLimitingOptions _options;
    private readonly IFailedLoginTracker _loginTracker;
    private static readonly ConcurrentDictionary<string, RateLimiter> _apiLimiters = new();
    private static readonly ConcurrentDictionary<string, RateLimiter> _authLimiters = new();

    private static readonly string[] AuthPaths = new[]
    {
        "/Account/Login",
        "/Account/Register",
        "/Account/CustomerRegister",
        "/TenantAccount/Login",
        "/TenantAccount/Register"
    };

    public GlobalRateLimitingMiddleware(
        RequestDelegate next,
        ILogger<GlobalRateLimitingMiddleware> logger,
        IOptions<RateLimitingOptions> options,
        IFailedLoginTracker loginTracker)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _loginTracker = loginTracker;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (!_options.Enabled)
        {
            await _next(httpContext);
            return;
        }

        var path = httpContext.Request.Path.Value ?? "";

        if (IsApiEndpoint(path) && _options.Api.Enabled)
        {
            if (await TryEnforceRateLimit(httpContext, _options.Api, _apiLimiters, "api"))
                return;
        }
        else if (IsAuthEndpoint(path))
        {
            if (_options.BruteForce.Enabled)
            {
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "UnknownIP";
                if (_loginTracker.IsLockedOut(clientIp))
                {
                    _logger.LogWarning("Global rate limit / brute force lockout: Client IP {ClientIp} is locked out", clientIp);
                    httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    httpContext.Response.Headers["Retry-After"] = _options.BruteForce.LockoutSeconds.ToString();
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "Too Many Requests",
                        message = "IP is temporarily locked out due to multiple failed login attempts.",
                        retryAfter = _options.BruteForce.LockoutSeconds
                    });
                    return;
                }
            }

            if (_options.Auth.Enabled)
            {
                if (await TryEnforceRateLimit(httpContext, _options.Auth, _authLimiters, "auth"))
                    return;
            }
        }

        await _next(httpContext);
    }

    private bool IsApiEndpoint(string path)
    {
        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAuthEndpoint(string path)
    {
        foreach (var authPath in AuthPaths)
        {
            if (path.Contains(authPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private async Task<bool> TryEnforceRateLimit(
        HttpContext httpContext,
        RateLimitRule rule,
        ConcurrentDictionary<string, RateLimiter> limiterCache,
        string ruleGroup)
    {
        string partitionKey = BuildPartitionKey(httpContext, rule, ruleGroup);
        var limiter = limiterCache.GetOrAdd(partitionKey, _ => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = rule.PermitLimit,
            Window = TimeSpan.FromSeconds(rule.WindowSeconds),
            QueueLimit = rule.QueueLimit,
            SegmentsPerWindow = 6
        }));

        using var lease = await limiter.AcquireAsync(1);
        if (!lease.IsAcquired)
        {
            var retryAfter = rule.WindowSeconds;
            _logger.LogWarning("Global rate limit exceeded for {Group} key {Key}", ruleGroup, partitionKey);

            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            httpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
            httpContext.Response.Headers["X-RateLimit-Limit"] = rule.PermitLimit.ToString();
            httpContext.Response.Headers["X-RateLimit-Remaining"] = "0";
            httpContext.Response.Headers["X-RateLimit-Window"] = rule.WindowSeconds.ToString();
            await httpContext.Response.WriteAsJsonAsync(new
            {
                error = "Too Many Requests",
                retryAfter = retryAfter,
                ruleGroup = ruleGroup
            });
            return true;
        }

        return false;
    }

    private string BuildPartitionKey(HttpContext httpContext, RateLimitRule rule, string ruleGroup)
    {
        string limitByVal;

        if (string.Equals(rule.LimitBy, "IP", StringComparison.OrdinalIgnoreCase))
        {
            limitByVal = httpContext.Connection.RemoteIpAddress?.ToString() ?? "UnknownIP";
        }
        else if (string.Equals(rule.LimitBy, "User", StringComparison.OrdinalIgnoreCase))
        {
            limitByVal = httpContext.User.Identity?.Name ?? "Anonymous";
        }
        else
        {
            limitByVal = "Global";
        }

        return $"{ruleGroup}_{rule.LimitBy}_{limitByVal}";
    }
}
