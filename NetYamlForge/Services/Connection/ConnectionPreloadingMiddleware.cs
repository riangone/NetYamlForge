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
        var shouldPreload = path.Contains("/api/") ||
                           path.Contains("/entity/") ||
                           path.Contains("/dashboard") ||
                           path.Contains("/page/");

        bool preloaded = false;
        if (shouldPreload && projectScope.IsSet)
        {
            try
            {
                // 预加载连接到 HttpContext.Items，后续 DI 获取时可直接复用
                // 修复：使用带项目名称的 GetConnectionAsync，避免创建新 scope 后 ProjectScope 丢失
                var connection = await connectionManager.GetConnectionAsync(projectScope.Current.Name);
                context.Items["PreloadedConnection"] = connection;
                context.Items["PreloadedConnectionProject"] = projectScope.Current.Name;
                preloaded = true;
                _logger.LogDebug("Preloaded connection for project {ProjectName}", projectScope.Current.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to preload connection for request {Path}", path);
                // 预加载失败不影响正常请求，后续会从工厂重新获取
            }
        }

        try
        {
            await _next(context);
        }
        finally
        {
            if (preloaded && context.Items.TryGetValue("PreloadedConnection", out var connObj) && connObj is IDbConnection conn)
            {
                // 若预加载了连接，但在请求生命周期内从未被 IDbConnection 解析使用过（即没有被 Scoped 容器 Dispose 的机会），在此安全释放
                if (!context.Items.ContainsKey("PreloadedConnectionUsed"))
                {
                    try
                    {
                        conn.Dispose();
                        _logger.LogDebug("Disposed unused preloaded connection for project {Project}", projectScope.Current?.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to dispose unused preloaded connection");
                    }
                }
            }
        }
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
