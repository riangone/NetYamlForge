using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace NetYamlForge.Services.Api;

public interface IStandardRateLimitResponses
{
    Task WriteAsync(
        HttpContext ctx,
        string group,
        int retryAfterSeconds,
        int? permitLimit = null,
        int? windowSeconds = null);
}

/// <summary>
/// 429 レスポンスの統一ライター。Global / Dynamic の中間ウェアから共用し、
/// レスポンスヘッダーと JSON ボディの書式をワンソースに保ちます。
/// </summary>
public class StandardRateLimitResponses : IStandardRateLimitResponses
{
    private readonly ILogger<StandardRateLimitResponses> _logger;

    public StandardRateLimitResponses(ILogger<StandardRateLimitResponses> logger)
    {
        _logger = logger;
    }

    public async Task WriteAsync(
        HttpContext ctx,
        string group,
        int retryAfterSeconds,
        int? permitLimit = null,
        int? windowSeconds = null)
    {
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        ctx.Response.Headers["X-RateLimit-Group"] = group;

        if (permitLimit.HasValue)
        {
            ctx.Response.Headers["X-RateLimit-Limit"] = permitLimit.Value.ToString();
        }

        ctx.Response.Headers["X-RateLimit-Remaining"] = "0";

        if (windowSeconds.HasValue)
        {
            ctx.Response.Headers["X-RateLimit-Window"] = windowSeconds.Value.ToString();
        }

        await ctx.Response.WriteAsJsonAsync(new
        {
            error = "Too Many Requests",
            message = $"Rate limit exceeded. Please retry after {retryAfterSeconds}s.",
            retryAfter = retryAfterSeconds,
            group
        });
    }
}