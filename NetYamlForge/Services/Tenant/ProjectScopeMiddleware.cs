using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Tenant;

/// <summary>
/// 项目范围中间件 - 验证用户是否有项目访问权限
/// </summary>
public class ProjectScopeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProjectScopeMiddleware> _logger;

    public ProjectScopeMiddleware(RequestDelegate next, ILogger<ProjectScopeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        
        // 提取项目前缀 (e.g., /auto-dealer-demo/Dashboard -> auto-dealer-demo)
        var projectName = ExtractProjectFromPath(path);
        
        if (!string.IsNullOrEmpty(projectName))
        {
            // 设置项目范围
            context.Items["ProjectName"] = projectName;
            
            // 如果是认证用户，验证是否有项目访问权限
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = GetUserId(context.User);
                
                if (userId > 0)
                {
                    var tenantUserService = context.RequestServices.GetRequiredService<ITenantUserService>();
                    var hasAccess = await tenantUserService.HasProjectAccessAsync(userId, projectName);
                    
                    if (!hasAccess)
                    {
                        _logger.LogWarning("用户 {UserId} 尝试访问未授权的项目 {ProjectName}", userId, projectName);

                        // 重定向到访问拒绝页面
                        context.Response.Redirect($"/TenantAccount/AccessDenied?project={projectName}");
                        return;
                    }
                }
            }
        }
        
        await _next(context);
    }

    private static string? ExtractProjectFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        
        // 跳过空路径和根路径
        if (path == "/" || path == "")
            return null;
        
        // 跳过认证相关路径
        if (path.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/Account", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/TenantAccount/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/TenantAccount", StringComparison.OrdinalIgnoreCase))
            return null;
        
        // 跳过静态资源
        if (path.StartsWith("/wwwroot/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
            return null;
        
        // 提取第一段路径作为项目名
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            var projectName = segments[0];

            // 跳过已知的非项目路径
            var skipPrefixes = new[] { "Account", "Dashboard", "Api", "signalr", "health", "swagger", "MyHome", "UserHome" };
            if (!skipPrefixes.Any(p => projectName.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return projectName;
            }
        }
        
        return null;
    }

    private static int GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        
        return 0;
    }
}

/// <summary>
/// 扩展方法
/// </summary>
public static class ProjectScopeMiddlewareExtensions
{
    public static IApplicationBuilder UseProjectScope(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ProjectScopeMiddleware>();
    }
}
