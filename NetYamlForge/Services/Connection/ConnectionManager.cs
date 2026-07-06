using System.Collections.Concurrent;
using System.Data;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services;
using NetYamlForge.Services.Tenant;

namespace NetYamlForge.Services.Connection;

/// <summary>
/// 连接管理器接口 - 统一 DI 注入和工厂创建两种模式
/// </summary>
public interface IConnectionManager : IDisposable
{
    /// <summary>
    /// 获取当前项目的连接
    /// </summary>
    Task<IDbConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放连接（物理关闭或退回原生驱动池）
    /// </summary>
    void ReleaseConnection(IDbConnection connection);

    /// <summary>
    /// 获取指定项目的连接
    /// </summary>
    Task<IDbConnection> GetConnectionAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前项目的连接（同步）
    /// </summary>
    IDbConnection GetConnection();

    /// <summary>
    /// 获取指定项目的连接（同步）
    /// </summary>
    IDbConnection GetConnection(string projectName);

    /// <summary>
    /// 释放指定项目的连接
    /// </summary>
    void ReleaseConnection(string projectName, IDbConnection connection);

    /// <summary>
    /// 获取所有连接池的统计信息
    /// </summary>
    Dictionary<string, ConnectionPoolStats> GetAllPoolStats();

    /// <summary>
    /// 获取指定项目的连接池统计信息
    /// </summary>
    ConnectionPoolStats GetPoolStats(string projectName);

    /// <summary>
    /// 重置指定项目的连接统计（清理记录）
    /// </summary>
    void ResetPool(string projectName);

    /// <summary>
    /// 重置所有连接统计
    /// </summary>
    void ResetAllPools();
}

/// <summary>
/// 连接管理器实现（完全基于原生连接池，无双重池化）
/// </summary>
public class ConnectionManager : IConnectionManager
{
    private readonly ProjectManager _projectManager;
    private readonly ILogger<ConnectionManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ConcurrentDictionary<string, ConnectionPoolStats> _statsMap;
    private readonly ConnectionPoolOptions _defaultOptions;
    private readonly ILoggerFactory _loggerFactory;

    public ConnectionManager(
        ProjectManager projectManager,
        ILogger<ConnectionManager> logger,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        IHttpContextAccessor httpContextAccessor,
        ConnectionPoolOptions? defaultOptions = null)
    {
        _projectManager = projectManager;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _loggerFactory = loggerFactory;
        _httpContextAccessor = httpContextAccessor;
        _statsMap = new ConcurrentDictionary<string, ConnectionPoolStats>(StringComparer.OrdinalIgnoreCase);
        _defaultOptions = defaultOptions ?? new ConnectionPoolOptions();
    }

    /// <summary>
    /// 辅助统计：增加创建计数
    /// </summary>
    private void RecordConnectionAcquired(string projectName)
    {
        var stats = _statsMap.GetOrAdd(projectName, _ => new ConnectionPoolStats());
        lock (stats)
        {
            stats.TotalCreated++;
            stats.CurrentActiveConnections++;
        }
    }

    /// <summary>
    /// 辅助统计：增加释放计数
    /// </summary>
    private void RecordConnectionReleased(string projectName)
    {
        if (_statsMap.TryGetValue(projectName, out var stats))
        {
            lock (stats)
            {
                stats.TotalReused++; // 在没有自定义池时，我们将底层连接复用记作 Reused，体现原生池的效率
                stats.TotalDisposed++;
                stats.CurrentActiveConnections = Math.Max(0, stats.CurrentActiveConnections - 1);
            }
        }
    }

    /// <summary>
    /// 为连接字符串添加原生连接池参数
    /// </summary>
    private static string AddPoolParametersIfNeeded(string dbType, string connectionString)
    {
        if (string.IsNullOrEmpty(dbType) || dbType.ToLowerInvariant() == "sqlite")
            return connectionString;

        return dbType.ToLowerInvariant() switch
        {
            "sqlserver" => AddSqlPoolParamsIfNeeded(connectionString),
            "postgresql" or "postgres" => AddNpgsqlPoolParamsIfNeeded(connectionString),
            "mysql" or "mariadb" => AddMySqlPoolParamsIfNeeded(connectionString),
            _ => connectionString
        };
    }

    private static string AddSqlPoolParamsIfNeeded(string connectionString)
    {
        if (connectionString.Contains("Max Pool Size", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var separator = connectionString.EndsWith(";") ? "" : ";";
        return $"{connectionString}{separator}Max Pool Size=100;Min Pool Size=5;Connection Lifetime=300;";
    }

    private static string AddNpgsqlPoolParamsIfNeeded(string connectionString)
    {
        if (connectionString.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var separator = connectionString.EndsWith(";") ? "" : ";";
        return $"{connectionString}{separator}MaxPoolSize=100;MinPoolSize=5;Connection Idle Lifetime=300;";
    }

    private static string AddMySqlPoolParamsIfNeeded(string connectionString)
    {
        if (connectionString.Contains("MaximumPoolSize", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var separator = connectionString.EndsWith(";") ? "" : ";";
        return $"{connectionString}{separator}MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;";
    }

    public async Task<IDbConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            var projectScope = httpContext.RequestServices.GetService<ProjectScope>();
            if (projectScope != null && projectScope.IsSet)
            {
                return await GetConnectionAsync(projectScope.Current.Name, cancellationToken);
            }
        }

        if (_scopeFactory != null)
        {
            using var scope = _scopeFactory.CreateScope();
            if (scope != null)
            {
                var spProjectScope = scope.ServiceProvider?.GetService<ProjectScope>();
                if (spProjectScope != null && spProjectScope.IsSet)
                {
                    return await GetConnectionAsync(spProjectScope.Current.Name, cancellationToken);
                }
            }
        }

        throw new InvalidOperationException("No project scope set. Use GetConnectionAsync(projectName) instead.");
    }

    public async Task<IDbConnection> GetConnectionAsync(string projectName, CancellationToken cancellationToken = default)
    {
        if (!_projectManager.TryGet(projectName, out var project) || project == null)
            throw new InvalidOperationException($"Project not found: {projectName}");

        var dbType = project.DatabaseType.ToLowerInvariant();
        
        // Resolve tenant-specific connection string if TenantContext is available
        var connectionString = project.ConnectionString;
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            var tenantCtx = httpContext.RequestServices.GetService<TenantContext>();
            if (tenantCtx != null && !string.IsNullOrEmpty(tenantCtx.ConnectionString))
            {
                connectionString = tenantCtx.ConnectionString;
            }
        }
        else if (_scopeFactory != null)
        {
            using var scope = _scopeFactory.CreateScope();
            if (scope != null)
            {
                var tenantCtx = scope.ServiceProvider?.GetService<TenantContext>();
                if (tenantCtx != null && !string.IsNullOrEmpty(tenantCtx.ConnectionString))
                {
                    connectionString = tenantCtx.ConnectionString;
                }
            }
        }

        connectionString = AddPoolParametersIfNeeded(project.DatabaseType, connectionString);

#pragma warning disable DCS003
        IDbConnection connection = dbType switch
        {
            "sqlserver" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
            "postgresql" or "postgres" => new Npgsql.NpgsqlConnection(connectionString),
            "mysql" or "mariadb" => new MySql.Data.MySqlClient.MySqlConnection(connectionString),
            _ => new Microsoft.Data.Sqlite.SqliteConnection(connectionString)
        };
#pragma warning restore DCS003

        if (connection.State != ConnectionState.Open)
        {
            if (connection is System.Data.Common.DbConnection dbConn)
            {
                await dbConn.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }
        }

        // Enable WAL mode for SQLite so concurrent reads and writes don't block each other.
        // Centralized in SqliteConnectionHardening so every connection path uses identical settings.
        if (dbType == "sqlite")
        {
            await SqliteConnectionHardening.ApplyAsync(connection);
        }

        RecordConnectionAcquired(projectName);
        return connection;
    }

    public IDbConnection GetConnection()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            var projectScope = httpContext.RequestServices.GetService<ProjectScope>();
            if (projectScope != null && projectScope.IsSet)
            {
                return GetConnection(projectScope.Current.Name);
            }
        }

        if (_scopeFactory != null)
        {
            using var scope = _scopeFactory.CreateScope();
            if (scope != null)
            {
                var spProjectScope = scope.ServiceProvider?.GetService<ProjectScope>();
                if (spProjectScope != null && spProjectScope.IsSet)
                {
                    return GetConnection(spProjectScope.Current.Name);
                }
            }
        }

        throw new InvalidOperationException("No project scope set. Use GetConnection(projectName) instead.");
    }

    public IDbConnection GetConnection(string projectName)
    {
        if (!_projectManager.TryGet(projectName, out var project) || project == null)
            throw new InvalidOperationException($"Project not found: {projectName}");

        var dbType = project.DatabaseType.ToLowerInvariant();
        
        // Resolve tenant-specific connection string if TenantContext is available
        var connectionString = project.ConnectionString;
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            var tenantCtx = httpContext.RequestServices.GetService<TenantContext>();
            if (tenantCtx != null && !string.IsNullOrEmpty(tenantCtx.ConnectionString))
            {
                connectionString = tenantCtx.ConnectionString;
            }
        }
        else if (_scopeFactory != null)
        {
            using var scope = _scopeFactory.CreateScope();
            if (scope != null)
            {
                var tenantCtx = scope.ServiceProvider?.GetService<TenantContext>();
                if (tenantCtx != null && !string.IsNullOrEmpty(tenantCtx.ConnectionString))
                {
                    connectionString = tenantCtx.ConnectionString;
                }
            }
        }

        connectionString = AddPoolParametersIfNeeded(project.DatabaseType, connectionString);

#pragma warning disable DCS003
        IDbConnection connection = dbType switch
        {
            "sqlserver" => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
            "postgresql" or "postgres" => new Npgsql.NpgsqlConnection(connectionString),
            "mysql" or "mariadb" => new MySql.Data.MySqlClient.MySqlConnection(connectionString),
            _ => new Microsoft.Data.Sqlite.SqliteConnection(connectionString)
        };
#pragma warning restore DCS003

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        // Enable WAL mode for SQLite so concurrent reads and writes don't block each other.
        if (dbType == "sqlite")
        {
            SqliteConnectionHardening.Apply(connection);
        }

        RecordConnectionAcquired(projectName);
        return connection;
    }

    public void ReleaseConnection(IDbConnection connection)
    {
        if (connection == null) return;

        using var scope = _scopeFactory.CreateScope();
        var projectScope = scope.ServiceProvider.GetService<ProjectScope>();
        
        if (projectScope != null && projectScope.IsSet)
        {
            ReleaseConnection(projectScope.Current.Name, connection);
            return;
        }

        // 无项目上下文时直接关闭释放
        try
        {
            if (connection.State == ConnectionState.Open)
                connection.Close();
            connection.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close or dispose database connection in ReleaseConnection.");
        }
    }

    public void ReleaseConnection(string projectName, IDbConnection connection)
    {
        if (connection == null) return;

        try
        {
            if (connection.State == ConnectionState.Open)
                connection.Close();
            connection.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close or dispose database connection in ReleaseConnection with project {ProjectName}.", projectName);
        }
        finally
        {
            RecordConnectionReleased(projectName);
        }
    }

    public Dictionary<string, ConnectionPoolStats> GetAllPoolStats()
    {
        return _statsMap.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public ConnectionPoolStats GetPoolStats(string projectName)
    {
        if (_statsMap.TryGetValue(projectName, out var stats))
        {
            return stats;
        }
        return new ConnectionPoolStats();
    }

    public void ResetPool(string projectName)
    {
        if (_statsMap.TryRemove(projectName, out _))
        {
            _logger.LogInformation("Resetting connection stats for project {ProjectName}", projectName);
        }
    }

    public void ResetAllPools()
    {
        _logger.LogInformation("Resetting all connection stats");
        _statsMap.Clear();
    }

    public void Dispose()
    {
        _statsMap.Clear();
    }
}
