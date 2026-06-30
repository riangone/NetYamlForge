using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services;
using NetYamlForge.Models;
using System.Security.Claims;
using System.Threading.Tasks;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using System;

namespace NetYamlForge.Services.Tenant;

public class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolverMiddleware> _logger;

    public TenantResolverMiddleware(RequestDelegate next, ILogger<TenantResolverMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ProjectScope projectScope, TenantContext tenantContext)
    {
        if (projectScope.IsSet)
        {
            var project = projectScope.Current;
            var tenantConfig = project.Multitenancy ?? new TenantConfig { Strategy = "physical" };

            tenantContext.Strategy = tenantConfig.Strategy;

            // Resolve Tenant ID
            string? tenantId = null;
            var resolver = tenantConfig.TenantResolver;
            if (resolver != null)
            {
                tenantId = resolver.Source.ToLowerInvariant() switch
                {
                    "header" => context.Request.Headers[resolver.Key].ToString(),
                    "query" => context.Request.Query[resolver.Key].ToString(),
                    "cookie" => context.Request.Cookies[resolver.Key],
                    "claim" => context.User.FindFirst(resolver.Key)?.Value,
                    _ => null
                };
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                tenantId = context.Request.Headers["X-Tenant-ID"].ToString();
                if (string.IsNullOrWhiteSpace(tenantId))
                    tenantId = context.Request.Query["tenantId"].ToString();
                if (string.IsNullOrWhiteSpace(tenantId))
                    tenantId = context.User.FindFirst("tenant_id")?.Value;
            }

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                tenantContext.TenantId = tenantId;

                if (tenantConfig.Strategy.Equals("physical", StringComparison.OrdinalIgnoreCase))
                {
                    var systemDbPath = Path.Combine(Directory.GetCurrentDirectory(), "system.db");
                    var systemConnStr = $"Data Source={systemDbPath};Pooling=False";
                    
                    string? dbConnStr = null;
                    try
                    {
#pragma warning disable DCS003
                        using var db = new SqliteConnection(systemConnStr);
#pragma warning restore DCS003
                        db.Open();
                        db.Execute(@"
                            CREATE TABLE IF NOT EXISTS tenants (
                                id TEXT PRIMARY KEY,
                                project_name TEXT NOT NULL,
                                connection_string TEXT,
                                is_active INTEGER NOT NULL DEFAULT 1,
                                created_at TEXT NOT NULL DEFAULT (datetime('now'))
                            );
                        ");
                        dbConnStr = await db.QueryFirstOrDefaultAsync<string>(
                            "SELECT connection_string FROM tenants WHERE id = @Id AND project_name = @Project AND is_active = 1",
                            new { Id = tenantId, Project = project.Name });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to query tenant database configuration from system.db");
                    }

                    if (!string.IsNullOrEmpty(dbConnStr))
                    {
                        tenantContext.ConnectionString = dbConnStr;
                    }
                    else
                    {
                        if (project.DatabaseType.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseConn = project.ConnectionString;
                            if (baseConn.Contains("Data Source="))
                            {
                                var parts = baseConn.Split("Data Source=");
                                var dbFilePart = parts[1].Split(';')[0];
                                var dir = Path.GetDirectoryName(dbFilePart) ?? "database";
                                var ext = Path.GetExtension(dbFilePart);
                                var filename = Path.GetFileNameWithoutExtension(dbFilePart);
                                var newDbFile = Path.Combine(dir, $"{filename}_{tenantId}{ext}");
                                tenantContext.ConnectionString = $"Data Source={newDbFile};Pooling=False";
                            }
                        }
                        else
                        {
                            tenantContext.ConnectionString = project.ConnectionString;
                        }
                    }
                }
                else
                {
                    tenantContext.ConnectionString = project.ConnectionString;
                }
            }
            else
            {
                tenantContext.ConnectionString = project.ConnectionString;
            }
        }

        await _next(context);
    }
}
