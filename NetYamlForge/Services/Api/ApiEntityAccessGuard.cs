using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services.Api;

public class ApiEntityAccessGuard
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ProjectScope _projectScope;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _systemDbConnectionString;

    public ApiEntityAccessGuard(
        IHttpContextAccessor httpContextAccessor, 
        ProjectScope projectScope,
        IServiceProvider serviceProvider,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _httpContextAccessor = httpContextAccessor;
        _projectScope = projectScope;
        _serviceProvider = serviceProvider;
        var dbPath = config["SystemDbPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "system.db");
        _systemDbConnectionString = dbPath.Contains(';') ? dbPath : new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
    }

    public IActionResult? ValidateApiAccess(EntityDefinition meta, bool writeRequired)
    {
        var apiMode = (meta.Api ?? "disabled").ToLowerInvariant();
        if (apiMode == "disabled")
        {
            return new ObjectResult(new ProblemDetails
            {
                Title = "API Access Disabled",
                Detail = $"API access to entity '{meta.Table}' is disabled by configuration.",
                Status = StatusCodes.Status403Forbidden
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
        if (writeRequired && apiMode == "readonly")
        {
            return new ObjectResult(new ProblemDetails
            {
                Title = "API Read-Only",
                Detail = $"API access to entity '{meta.Table}' is read-only.",
                Status = StatusCodes.Status403Forbidden
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        // --- Fine-grained Role-Based Access Control ---
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            var user = httpContext.User;
            var authenticatedIdentity = user?.Identities?.FirstOrDefault(i => i.IsAuthenticated);
            if (authenticatedIdentity != null)
            {
                var userName = authenticatedIdentity.FindFirst(ClaimTypes.Name)?.Value 
                    ?? user.FindFirst(ClaimTypes.Name)?.Value;
                var isAdmin = user.IsInRole("Admin") 
                    || user.HasClaim(ClaimTypes.Role, "Admin") 
                    || authenticatedIdentity.HasClaim(ClaimTypes.Role, "Admin");

                if (!string.IsNullOrEmpty(userName) && !isAdmin && _projectScope.IsSet)
                {
                    try
                    {
                        // Use system.db connection to prevent using dynamic project connection which lacks auth tables
#pragma warning disable DCS003
                        using var db = new SqliteConnection(_systemDbConnectionString);
#pragma warning restore DCS003
                        db.Open();
                        
                        // Check if user has roles in the project DB (project-specific and/or global)
                        var query = @"
                            SELECT pr.role_name 
                            FROM app_user_project_role pr 
                            JOIN app_user u ON pr.user_id = u.id 
                            WHERE u.user_name = @UserName AND pr.project_name = @ProjectName
                            UNION
                            SELECT role_name 
                            FROM app_user_role 
                            WHERE user_name = @UserName";

                        var roles = db.Query<string>(
                            query,
                            new { UserName = userName, ProjectName = _projectScope.Current.Name }).ToList();

                        if (writeRequired)
                        {
                            var writeRoles = _projectScope.Current.ApiWriteRoles;
                            bool hasWriteRole;
                            if (writeRoles != null && writeRoles.Any())
                            {
                                hasWriteRole = roles.Any(r => writeRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
                            }
                            else
                            {
                                hasWriteRole = roles.Any(r => 
                                    string.Equals(r, "worker", StringComparison.OrdinalIgnoreCase) || 
                                    string.Equals(r, "manager", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(r, "operator", StringComparison.OrdinalIgnoreCase));
                            }

                            if (!hasWriteRole)
                            {
                                return new ObjectResult(new ProblemDetails
                                {
                                    Title = "Access Denied",
                                    Detail = $"User '{userName}' does not have write access to entity '{meta.Table}' in project '{_projectScope.Current.Name}'.",
                                    Status = StatusCodes.Status403Forbidden
                                })
                                {
                                    StatusCode = StatusCodes.Status403Forbidden
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log database access exceptions and fail secure if write is required
                        var logger = _serviceProvider.GetService<Microsoft.Extensions.Logging.ILogger<ApiEntityAccessGuard>>();
                        logger?.LogError(ex, "Error validating API access for user '{UserName}' on entity '{Table}'", userName, meta.Table);
                        
                        if (writeRequired)
                        {
                            return new ObjectResult(new ProblemDetails
                            {
                                Title = "Access Verification Error",
                                Detail = "An error occurred while verifying access permissions.",
                                Status = StatusCodes.Status500InternalServerError
                            })
                            {
                                StatusCode = StatusCodes.Status500InternalServerError
                            };
                        }
                    }
                }
            }
        }

        return null;
    }
}



