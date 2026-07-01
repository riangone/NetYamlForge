using System;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Services.Tenant;

namespace NetYamlForge.Services.Api;

public class DynamicRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DynamicRateLimitingMiddleware> _logger;
    private static readonly ConcurrentDictionary<string, RateLimiter> _limiterCache = new();

    public DynamicRateLimitingMiddleware(RequestDelegate next, ILogger<DynamicRateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext, IEntityMetadataProvider metadataProvider, TenantContext tenantContext)
    {
        var path = httpContext.Request.Path.Value ?? "";
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? entityName = null;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "entities", StringComparison.OrdinalIgnoreCase))
            {
                entityName = parts[i + 1];
                break;
            }
        }

        if (string.IsNullOrEmpty(entityName) || !metadataProvider.TryGet(entityName, out var meta) || meta.RateLimiting?.Enabled != true)
        {
            await _next(httpContext);
            return;
        }

        var rateLimitConfig = meta.RateLimiting;
        
        string limitByVal = "Global";
        if (string.Equals(rateLimitConfig.LimitBy, "IP", StringComparison.OrdinalIgnoreCase))
        {
            limitByVal = httpContext.Connection.RemoteIpAddress?.ToString() ?? "UnknownIP";
        }
        else if (string.Equals(rateLimitConfig.LimitBy, "User", StringComparison.OrdinalIgnoreCase))
        {
            limitByVal = httpContext.User.Identity?.Name ?? "Anonymous";
        }
        else if (string.Equals(rateLimitConfig.LimitBy, "Tenant", StringComparison.OrdinalIgnoreCase))
        {
            limitByVal = tenantContext.TenantId ?? "DefaultTenant";
        }

        string partitionKey = $"{entityName}_{rateLimitConfig.LimitBy}_{limitByVal}";
        var limiter = GetOrCreateLimiter(partitionKey, rateLimitConfig);

        using var lease = await limiter.AcquireAsync(1);
        if (!lease.IsAcquired)
        {
            _logger.LogWarning("Rate limit exceeded for key {Key}", partitionKey);
            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            httpContext.Response.Headers["Retry-After"] = "60";
            await httpContext.Response.WriteAsJsonAsync(new { error = "Too Many Requests", retryAfter = 60 });
            return;
        }

        await _next(httpContext);
    }

    private RateLimiter GetOrCreateLimiter(string partitionKey, RateLimitingDefinition config)
    {
        return _limiterCache.GetOrAdd(partitionKey, _ => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = config.PermitLimit,
            Window = TimeSpan.FromSeconds(config.WindowSeconds),
            QueueLimit = config.QueueLimit,
            SegmentsPerWindow = 6
        }));
    }
}
