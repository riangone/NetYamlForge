using System;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.Config;

namespace NetYamlForge.Services.Api;

public class GlobalRateLimitingMiddleware
{
    private const string ApiTokenScheme = "ApiToken";

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalRateLimitingMiddleware> _logger;
    private readonly RateLimitingOptions _options;
    private readonly IFailedLoginTracker _loginTracker;
    private readonly IClientIpResolver _clientIpResolver;
    private readonly IRateLimiterRegistry _limiterRegistry;
    private readonly IStandardRateLimitResponses _rateLimitResponses;

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
        : this(
            next,
            logger,
            options,
            loginTracker,
            new ClientIpResolver(options),
            new RateLimiterRegistry(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<RateLimiterRegistry>.Instance),
            new StandardRateLimitResponses(Microsoft.Extensions.Logging.Abstractions.NullLogger<StandardRateLimitResponses>.Instance))
    {
    }

    public GlobalRateLimitingMiddleware(
        RequestDelegate next,
        ILogger<GlobalRateLimitingMiddleware> logger,
        IOptions<RateLimitingOptions> options,
        IFailedLoginTracker loginTracker,
        IClientIpResolver clientIpResolver,
        IRateLimiterRegistry limiterRegistry,
        IStandardRateLimitResponses rateLimitResponses)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _loginTracker = loginTracker;
        _clientIpResolver = clientIpResolver;
        _limiterRegistry = limiterRegistry;
        _rateLimitResponses = rateLimitResponses;
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
            // UseAuthorization より前に ApiToken を事前認証し、複合キー（IP|User）に
            // ユーザー身元を反映できるようにする。
            await PreAuthenticateApiTokenAsync(httpContext);

            if (await TryEnforceRateLimit(httpContext, _options.Api, "API", null))
                return;
        }
        else if (IsAuthEndpoint(path))
        {
            string? formUsername = null;

            if (HttpMethods.IsPost(httpContext.Request.Method)
                && (_options.BruteForce.Enabled || _options.Auth.Enabled))
            {
                formUsername = await ReadUsernameFromFormAsync(httpContext);
            }

            if (_options.BruteForce.Enabled)
            {
                var clientIp = _clientIpResolver.GetClientIp(httpContext);
                if (_loginTracker.IsLockedOut(clientIp)
                    || (formUsername != null && _loginTracker.IsLockedOut(formUsername)))
                {
                    _logger.LogWarning(
                        "Global rate limit / brute force lockout: Client IP {ClientIp} or user {Username} is locked out",
                        clientIp, formUsername);

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
                if (await TryEnforceRateLimit(httpContext, _options.Auth, "Auth", formUsername))
                    return;
            }
        }

        await _next(httpContext);
    }

    private static bool IsApiEndpoint(string path)
    {
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.Contains("/api/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/entities/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthEndpoint(string path)
    {
        foreach (var authPath in AuthPaths)
        {
            if (path.Contains(authPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// トークン系ヘッダー/クエリを持つ API リクエストについて ApiToken スキームを事前認証し、
    /// 成功時はプリンシパルを HttpContext.User に設定します。
    /// これにより PartitionPageKey が "Anonymous" に退化するのを防ぎます。
    /// </summary>
    private async Task PreAuthenticateApiTokenAsync(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return;
        }

        if (!HasApiTokenSource(httpContext.Request))
        {
            return;
        }

        try
        {
            var result = await httpContext.AuthenticateAsync(ApiTokenScheme);
            if (result.Succeeded && result.Principal != null)
            {
                httpContext.User = result.Principal;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pre-authentication via ApiToken scheme failed");
        }
    }

    private static bool HasApiTokenSource(HttpRequest request)
    {
        var authHeader = request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader)
            && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.Headers["X-Api-Token"].FirstOrDefault())
            || !string.IsNullOrWhiteSpace(request.Headers["X-Api-Key"].FirstOrDefault()))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(request.Query["api_token"].FirstOrDefault());
    }

    private async Task<bool> TryEnforceRateLimit(
        HttpContext httpContext,
        RateLimitRule rule,
        string group,
        string? formUsername)
    {
        var ip = _clientIpResolver.GetClientIp(httpContext);
        var user = RateLimitPartitioners.ResolveUser(httpContext, formUsername);
        string partitionKey = RateLimitPartitioners.BuildCompositeKey(group, rule.LimitBy, ip, user);

        var limiter = _limiterRegistry.GetOrCreate(partitionKey, () => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = rule.PermitLimit,
            Window = TimeSpan.FromSeconds(rule.WindowSeconds),
            QueueLimit = rule.QueueLimit,
            SegmentsPerWindow = 6
        }));

        // QueueLimit=0 のためキューへ入らず、許容値を超えていれば即座に拒否される。
        using var lease = limiter.AttemptAcquire(1);
        if (!lease.IsAcquired)
        {
            var retryAfter = rule.WindowSeconds;
            if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan leaseRetryAfter)
                && leaseRetryAfter.TotalSeconds > 0)
            {
                retryAfter = Math.Max(1, (int)Math.Ceiling(leaseRetryAfter.TotalSeconds));
            }

            _logger.LogWarning("Global rate limit exceeded for {Group} key {Key}", group, partitionKey);
            await _rateLimitResponses.WriteAsync(httpContext, group, retryAfter, rule.PermitLimit, rule.WindowSeconds);
            return true;
        }

        return false;
    }

    private async Task<string?> ReadUsernameFromFormAsync(HttpContext httpContext)
    {
        try
        {
            httpContext.Request.EnableBuffering();
            var form = await httpContext.Request.ReadFormAsync();
            httpContext.Request.Body.Position = 0;
            return form[_options.BruteForce.UsernameFormKey].FirstOrDefault();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (BadHttpRequestException)
        {
            return null;
        }
    }
}