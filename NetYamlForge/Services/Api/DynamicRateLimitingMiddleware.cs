using System;
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
    private readonly IClientIpResolver _clientIpResolver;
    private readonly IRateLimiterRegistry _limiterRegistry;
    private readonly IStandardRateLimitResponses _rateLimitResponses;

    public DynamicRateLimitingMiddleware(
        RequestDelegate next,
        ILogger<DynamicRateLimitingMiddleware> logger,
        IClientIpResolver clientIpResolver,
        IRateLimiterRegistry limiterRegistry,
        IStandardRateLimitResponses rateLimitResponses)
    {
        _next = next;
        _logger = logger;
        _clientIpResolver = clientIpResolver;
        _limiterRegistry = limiterRegistry;
        _rateLimitResponses = rateLimitResponses;
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

        // value 統一走 IClientIpResolver（リバースプロキシ配下でも実 IP を解決）。
        var ip = _clientIpResolver.GetClientIp(httpContext);
        var user = RateLimitPartitioners.ResolveUser(httpContext, null);
        var tenant = tenantContext.TenantId ?? "DefaultTenant";
        string partitionKey = RateLimitPartitioners.BuildCompositeKey(entityName, rateLimitConfig.LimitBy, ip, user, tenant);

        var limiter = _limiterRegistry.GetOrCreate(partitionKey, () => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = rateLimitConfig.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitConfig.WindowSeconds),
            QueueLimit = rateLimitConfig.QueueLimit,
            SegmentsPerWindow = 6
        }));

        // QueueLimit=0 設定時はキューへ入れず即時 429 拒否。
        using var lease = limiter.AttemptAcquire(1);
        if (!lease.IsAcquired)
        {
            var retryAfter = rateLimitConfig.WindowSeconds;
            if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan leaseRetryAfter)
                && leaseRetryAfter.TotalSeconds > 0)
            {
                retryAfter = Math.Max(1, (int)Math.Ceiling(leaseRetryAfter.TotalSeconds));
            }

            _logger.LogWarning("Rate limit exceeded for key {Key}", partitionKey);
            await _rateLimitResponses.WriteAsync(httpContext, entityName, retryAfter, rateLimitConfig.PermitLimit, rateLimitConfig.WindowSeconds);
            return;
        }

        await _next(httpContext);
    }
}