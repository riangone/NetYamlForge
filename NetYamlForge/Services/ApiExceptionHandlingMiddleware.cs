using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services;

/// <summary>
/// 未処理の例外をキャッチし、API 呼び出しの場合は統一された CommandResult 形式の JSON を返す中間ウェア。
/// 構造化ログを記録し、可観測性（Observability）を向上させます。
/// </summary>
public sealed class ApiExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionHandlingMiddleware> _logger;

    public ApiExceptionHandlingMiddleware(RequestDelegate next, ILogger<ApiExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception caught by middleware. Path: {Path}, TraceId: {TraceId}", 
                context.Request.Path, context.TraceIdentifier);

            var isApi = context.Request.Path.StartsWithSegments("/api") || 
                        (context.Request.Headers["Accept"].ToString() ?? "").Contains("application/json");

            if (isApi)
            {
                await HandleApiExceptionAsync(context, ex);
            }
            else
            {
                throw; // Razor ビュー等は標準のエラーページ（/Home/Error）へ委譲
            }
        }
    }

    private static Task HandleApiExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        
        var statusCode = HttpStatusCode.InternalServerError;
        var code = "internal_server_error";
        var message = exception.Message;

        switch (exception)
        {
            case UnauthorizedAccessException:
                statusCode = HttpStatusCode.Unauthorized;
                code = "unauthorized";
                break;
            case KeyNotFoundException:
                statusCode = HttpStatusCode.NotFound;
                code = "not_found";
                break;
            case InvalidOperationException ex when ex.Message.Contains("quota exceeded", StringComparison.OrdinalIgnoreCase):
                statusCode = HttpStatusCode.BadRequest;
                code = "quota_exceeded";
                break;
            case InvalidOperationException:
                statusCode = HttpStatusCode.BadRequest;
                code = "invalid_operation";
                break;
            case ArgumentException:
                statusCode = HttpStatusCode.BadRequest;
                code = "bad_request";
                break;
        }

        context.Response.StatusCode = (int)statusCode;

        // CommandResult に準拠したエラーレスポンス構造
        var responseObj = new
        {
            Ok = false,
            Error = new
            {
                Code = code,
                Message = message
            }
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(responseObj, options));
    }
}
