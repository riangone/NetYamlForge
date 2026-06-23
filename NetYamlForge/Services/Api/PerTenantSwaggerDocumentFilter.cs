// ファイル概要：プロジェクト別 Swagger ドキュメントフィルター。
// リクエストパス /swagger/{project}/swagger.json からプロジェクト名を読み取り、
// そのプロジェクトに属する /api/{project}/ パスのみを残します。
// api == "disabled" のエンティティのパスも除去します。

using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NetYamlForge.Services.Api;

/// <summary>
/// Per-tenant Swagger document filter. When the request path matches
/// /swagger/{project}/swagger.json, removes all paths that don't belong to
/// /api/{project}/ and removes paths for entities where api == "disabled".
/// </summary>
public class PerTenantSwaggerDocumentFilter : IDocumentFilter
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider    _serviceProvider;

    public PerTenantSwaggerDocumentFilter(
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider     serviceProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _serviceProvider     = serviceProvider;
    }

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
            return;

        // Match /swagger/{project}/swagger.json (with optional path base prefix)
        var path = httpContext.Request.Path.Value ?? string.Empty;
        var projectName = ExtractProjectFromSwaggerPath(path);
        if (string.IsNullOrWhiteSpace(projectName))
            return;

        // Resolve ProjectManager to get entity metadata for the target project
        using var scope = _serviceProvider.CreateScope();
        var pm = scope.ServiceProvider.GetRequiredService<ProjectManager>();
        var project = pm.GetAll().FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));

        if (project is null)
            return;

        var metadataProvider = project.EntityMetadata;

        // Build the set of paths that belong to this project and are not disabled
        var allowedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (metadataProvider != null)
        {
            foreach (var kv in metadataProvider.GetAll())
            {
                var entityName = kv.Key;
                var meta       = kv.Value;
                var apiMode    = (meta.Api ?? "disabled").ToLowerInvariant();
                if (apiMode == "disabled")
                    continue;

                allowedPaths.Add($"/api/{projectName}/{entityName}");
                allowedPaths.Add($"/api/{projectName}/{entityName}/{{id}}");

                // Custom action paths
                if (meta.Actions != null)
                {
                    foreach (var actionKey in meta.Actions.Keys)
                    {
                        allowedPaths.Add($"/api/{projectName}/{entityName}/{{id}}/actions/{actionKey}");
                    }
                }
            }
        }

        // Remove all paths that don't belong to /api/{project}/ or are not allowed
        var pathsToRemove = swaggerDoc.Paths.Keys
            .Where(p => !allowedPaths.Contains(p))
            .ToList();

        foreach (var p in pathsToRemove)
            swaggerDoc.Paths.Remove(p);

        // Update document title to reflect the project
        swaggerDoc.Info.Title = $"NetYamlForge API – {projectName}";
    }

    /// <summary>
    /// Extracts the project name from a path like /swagger/{project}/swagger.json.
    /// Also handles path bases such as /nyf/swagger/{project}/swagger.json.
    /// </summary>
    private static string? ExtractProjectFromSwaggerPath(string path)
    {
        // Normalize: remove trailing slash, work case-insensitively
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Find "swagger" segment and get the next one as project name
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "swagger", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < segments.Length &&
                !string.Equals(segments[i + 1], "swagger.json", StringComparison.OrdinalIgnoreCase))
            {
                return segments[i + 1];
            }
        }
        return null;
    }
}
