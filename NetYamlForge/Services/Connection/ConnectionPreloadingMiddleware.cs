using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Connection;

/// <summary>
/// 异步连接预加载中间件 - 在请求开始时预先获取连接，避免同步阻塞异步
/// </summary>
public class ConnectionPreloadingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ConnectionPreloadingMiddleware> _logger;

    public ConnectionPreloadingMiddleware(
        RequestDelegate next,
        ILogger<ConnectionPreloadingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConnectionManager connectionManager, ProjectScope projectScope)
    {
        // 仅对需要数据库连接的路径预加载
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var shouldPreload = path.StartsWith("/api/") ||
                           path.StartsWith("/entity/") ||
                           path.StartsWith("/dashboard") ||
                           path.StartsWith("/page/");

        if (shouldPreload && projectScope.IsSet)
        {
            try
            {
                // 预加载连接到 HttpContext.Items，后续 DI 获取时可直接复用
                // 修复：使用带项目名称的 GetConnectionAsync，避免创建新 scope 后 ProjectScope 丢失
                var connection = await connectionManager.GetConnectionAsync(projectScope.Current.Name);
                context.Items["PreloadedConnection"] = connection;
                context.Items["PreloadedConnectionProject"] = projectScope.Current.Name;
                _logger.LogDebug("Preloaded connection for project {ProjectName}", projectScope.Current.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to preload connection for request {Path}", path);
                // 预加载失败不影响正常请求，后续会从工厂重新获取
            }
        }

        await _next(context);
    }
}

/// <summary>
/// 连接预加载扩展方法
/// </summary>
public static class ConnectionPreloadingExtensions
{
    public static IApplicationBuilder UseConnectionPreloading(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ConnectionPreloadingMiddleware>();
    }
}
