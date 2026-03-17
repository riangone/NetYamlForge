// ファイル概要: リクエスト単位の TraceId を統一し、レスポンスヘッダーとログスコープへ反映します。

using Microsoft.Extensions.Primitives;

namespace NetYamlForge.Middleware;

public sealed class RequestTraceMiddleware
{
    public const string TraceHeaderName = "X-Trace-Id";
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTraceMiddleware> _logger;

    public RequestTraceMiddleware(RequestDelegate next, ILogger<RequestTraceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = ResolveTraceId(context.Request.Headers[TraceHeaderName], context.TraceIdentifier);
        context.TraceIdentifier = traceId;
        context.Response.Headers[TraceHeaderName] = traceId;

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["TraceId"] = traceId
               }))
        {
            await _next(context);
        }
    }

    public static string ResolveTraceId(StringValues headerValue, string fallbackTraceId)
    {
        var candidate = headerValue.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate.Trim();
        }

        return fallbackTraceId;
    }
}
