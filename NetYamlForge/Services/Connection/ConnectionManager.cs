using System.Collections.Concurrent;
using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services;

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
    /// 释放连接回池（而不是关闭）
    /// </summary>
    void ReleaseConnection(IDbConnection connection);

    /// <summary>
    /// 获取指定项目的连接
    /// </summary>
    Task<IDbConnection> GetConnectionAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放指定项目的连接回池
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
    /// Phase 2: 重置指定项目的连接池（关闭所有连接并重建）
    /// </summary>
    void ResetPool(string projectName);

    /// <summary>
    /// Phase 2: 重置所有连接池
    /// </summary>
    void ResetAllPools();
}

/// <summary>
/// 连接管理器实现
/// </summary>
public class ConnectionManager : IConnectionManager
{
    private readonly ProjectManager _projectManager;
    private readonly ILogger<ConnectionManager> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ConcurrentDictionary<string, ConnectionPool> _pools;
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
        _pools = new ConcurrentDictionary<string, ConnectionPool>();
        _defaultOptions = defaultOptions ?? new ConnectionPoolOptions();
    }

    /// <summary>
    /// 获取或创建项目的连接池
    /// </summary>
    private ConnectionPool GetOrCreatePool(string projectName)
    {
        return _pools.GetOrAdd(projectName, name =>
        {
            if (!_projectManager.TryGet(name, out var project) || project == null)
                throw new InvalidOperationException($"Project not found: {name}");

            var dbType = project.DatabaseType.ToLowerInvariant();
            var connectionString = AddPoolParametersIfNeeded(project.DatabaseType, project.ConnectionString);

#pragma warning disable DCS003
            Func<IDbConnection> factory = dbType switch
            {
                "sqlserver" => () => new Microsoft.Data.SqlClient.SqlConnection(connectionString),
                "postgresql" or "postgres" => () => new Npgsql.NpgsqlConnection(connectionString),
                "mysql" or "mariadb" => () => new MySql.Data.MySqlClient.MySqlConnection(connectionString),
                _ => () => new Microsoft.Data.Sqlite.SqliteConnection(connectionString)
            };
#pragma warning restore DCS003

            _logger.LogInformation("Created connection pool for project {ProjectName} (DB: {DbType})",
                name, dbType);

            return new ConnectionPool(name, factory, _defaultOptions,
                _loggerFactory.CreateLogger<ConnectionPool>());
        });
    }

    /// <summary>
    /// 为连接字符串添加原生连接池参数
    /// </summary>
    private static string AddPoolParametersIfNeeded(string dbType, string connectionString)
    {
        // SQLite 不需要额外参数（使用应用层连接池）
        if (string.IsNullOrEmpty(dbType) || dbType == "sqlite")
            return connectionString;

        // 为 PostgreSQL/MySQL/SQL Server 添加原生连接池参数
        // 注意：如果连接字符串已包含这些参数，则不重复添加
        return dbType.ToLowerInvariant() switch
        {
            "sqlserver" => AddSqlPoolParamsIfNeeded(connectionString),
            "postgresql" or "postgres" => AddNpgsqlPoolParamsIfNeeded(connectionString),
            "mysql" or "mariadb" => AddMySqlPoolParamsIfNeeded(connectionString),
            _ => connectionString
        };
    }

    /// <summary>
    /// 为 SQL Server 连接字符串添加池参数
    /// </summary>
    private static string AddSqlPoolParamsIfNeeded(string connectionString)
    {
        if (connectionString.Contains("Max Pool Size", StringComparison.OrdinalIgnoreCase))
            return connectionString; // 已配置

        var separator = connectionString.EndsWith(";") ? "" : ";";
        return $"{connectionString}{separator}Max Pool Size=100;Min Pool Size=5;Connection Lifetime=300;";
    }

    /// <summary>
    /// 为 PostgreSQL 连接字符串添加池参数
    /// </summary>
    private static string AddNpgsqlPoolParamsIfNeeded(string connectionString)
    {
        if (connectionString.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
            return connectionString; // 已配置

        var separator = connectionString.EndsWith(";") ? "" : ";";
        return $"{connectionString}{separator}MaxPoolSize=100;MinPoolSize=5;Connection Idle Lifetime=300;";
    }

    /// <summary>
    /// 为 MySQL 连接字符串添加池参数
    /// </summary>
    private static string AddMySqlPoolParamsIfNeeded(string connectionString)
    {
        if (connectionString.Contains("MaximumPoolSize", StringComparison.OrdinalIgnoreCase))
            return connectionString; // 已配置

        var separator = connectionString.EndsWith(";") ? "" : ";";
        return $"{connectionString}{separator}MaximumPoolSize=100;MinimumPoolSize=5;ConnectionLifeTime=300;";
    }

    public async Task<IDbConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        // 1. 尝试从 HttpContext 获取当前项目的 ProjectScope（最高优先级，最准确）
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            var projectScope = httpContext.RequestServices.GetService<ProjectScope>();
            if (projectScope != null && projectScope.IsSet)
            {
                return await GetConnectionAsync(projectScope.Current.Name, cancellationToken);
            }
        }

        // 2. 备选方案：尝试从 ScopeFactory 获取（可能在某些后台任务中手动设置了作用域）
        using var scope = _scopeFactory.CreateScope();
        var spProjectScope = scope.ServiceProvider.GetService<ProjectScope>();
        
        if (spProjectScope != null && spProjectScope.IsSet)
        {
            return await GetConnectionAsync(spProjectScope.Current.Name, cancellationToken);
        }

        throw new InvalidOperationException("No project scope set. Use GetConnectionAsync(projectName) instead.");
    }

    public async Task<IDbConnection> GetConnectionAsync(string projectName, CancellationToken cancellationToken = default)
    {
        var pool = GetOrCreatePool(projectName);
        var connection = await pool.AcquireAsync(cancellationToken);

        // 确保连接是打开的
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        return connection;
    }

    public void ReleaseConnection(IDbConnection connection)
    {
        // 从当前请求作用域获取 ProjectScope
        using var scope = _scopeFactory.CreateScope();
        var projectScope = scope.ServiceProvider.GetService<ProjectScope>();
        
        if (projectScope == null || !projectScope.IsSet)
        {
            // 没有项目上下文，直接关闭连接
            try
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                connection.Dispose();
            }
            catch { }
            return;
        }

        ReleaseConnection(projectScope.Current.Name, connection);
    }

    public void ReleaseConnection(string projectName, IDbConnection connection)
    {
        if (_pools.TryGetValue(projectName, out var pool))
        {
            pool.Release(connection);
        }
        else
        {
            // 池不存在，直接关闭连接
            try
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                connection.Dispose();
            }
            catch { }
        }
    }

    public Dictionary<string, ConnectionPoolStats> GetAllPoolStats()
    {
        return _pools.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.GetStats()
        );
    }

    public ConnectionPoolStats GetPoolStats(string projectName)
    {
        if (_pools.TryGetValue(projectName, out var pool))
        {
            return pool.GetStats();
        }
        return new ConnectionPoolStats();
    }

    /// <summary>
    /// Phase 2: 重置指定项目的连接池（关闭所有连接并重建池）
    /// </summary>
    public void ResetPool(string projectName)
    {
        if (_pools.TryRemove(projectName, out var pool))
        {
            _logger.LogInformation("Resetting connection pool for project {ProjectName}", projectName);
            pool.Dispose();
        }
    }

    /// <summary>
    /// Phase 2: 重置所有连接池
    /// </summary>
    public void ResetAllPools()
    {
        _logger.LogInformation("Resetting all connection pools");
        foreach (var kvp in _pools)
        {
            kvp.Value.Dispose();
        }
        _pools.Clear();
    }

    public void Dispose()
    {
        foreach (var pool in _pools.Values)
        {
            pool.Dispose();
        }
        _pools.Clear();
    }
}
