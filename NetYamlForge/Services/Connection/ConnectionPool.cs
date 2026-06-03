using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Npgsql;

namespace NetYamlForge.Services.Connection;

/// <summary>
/// 连接池统计信息
/// </summary>
public class ConnectionPoolStats
{
    public int TotalCreated { get; set; }
    public int TotalReused { get; set; }
    public int TotalDisposed { get; set; }
    public int CurrentActiveConnections { get; set; }
    public int CurrentPooledConnections { get; set; }

    public double ReuseRate => (TotalCreated + TotalReused) > 0
        ? (double)TotalReused / (TotalCreated + TotalReused) * 100
        : 0;
}

/// <summary>
/// 连接池配置
/// </summary>
public class ConnectionPoolOptions
{
    /// <summary>
    /// 最大池化连接数（默认 32）
    /// </summary>
    public int MaxPoolSize { get; set; } = 32;

    /// <summary>
    /// 连接空闲超时（毫秒），超时后关闭（默认 60000 = 1 分钟）
    /// </summary>
    public int IdleTimeoutMs { get; set; } = 60000;

    /// <summary>
    /// 连接最大存活时间（毫秒），超时后强制重建（默认 300000 = 5 分钟）
    /// </summary>
    public int MaxLifetimeMs { get; set; } = 300000;

    /// <summary>
    /// 是否启用连接池（默认 true）
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 连接包装器，跟踪使用状态
/// </summary>
internal class PooledConnection : IDisposable
{
    public IDbConnection Connection { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastUsedAt { get; set; }
    public bool IsInUse { get; set; }

    public PooledConnection(IDbConnection connection)
    {
        Connection = connection;
        CreatedAt = DateTime.UtcNow;
        LastUsedAt = DateTime.UtcNow;
        IsInUse = false;
    }

    public bool IsExpired(ConnectionPoolOptions options)
    {
        var now = DateTime.UtcNow;
        var lifetime = now - CreatedAt;
        var idleTime = now - LastUsedAt;

        return lifetime.TotalMilliseconds > options.MaxLifetimeMs
            || idleTime.TotalMilliseconds > options.IdleTimeoutMs;
    }

    public void Dispose()
    {
        try
        {
            if (Connection.State == ConnectionState.Open)
            {
                Connection.Close();
            }
            Connection.Dispose();
        }
        catch
        {
            // 忽略关闭异常
        }
    }
}

/// <summary>
/// 应用层连接池实现
/// </summary>
public class ConnectionPool : IDisposable
{
    private readonly ILogger<ConnectionPool> _logger;
    private readonly ConnectionPoolOptions _options;
    private readonly string _projectName;
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly ConcurrentQueue<PooledConnection> _pool;
    private readonly SemaphoreSlim _poolSemaphore;
    private readonly ConnectionPoolStats _stats;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    // Phase 2 优化：使用 ConcurrentDictionary 跟踪底层原生连接实例
    private readonly ConcurrentDictionary<IDbConnection, PooledConnection> _activeConnections;

    public ConnectionPoolStats Stats => _stats;

    public ConnectionPool(
        string projectName,
        Func<IDbConnection> connectionFactory,
        ConnectionPoolOptions? options,
        ILogger<ConnectionPool> logger)
    {
        _projectName = projectName ?? throw new ArgumentNullException(nameof(projectName));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? new ConnectionPoolOptions();
        _logger = logger;
        _pool = new ConcurrentQueue<PooledConnection>();
        _poolSemaphore = new SemaphoreSlim(_options.MaxPoolSize, _options.MaxPoolSize);
        _stats = new ConnectionPoolStats();
        _activeConnections = new ConcurrentDictionary<IDbConnection, PooledConnection>();

        // 启动定时清理线程
        _cleanupTimer = new Timer(CleanupExpiredConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// 从池中获取连接，如果池为空则创建新连接
    /// </summary>
    public async Task<IDbConnection> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ConnectionPool));

        if (!_options.Enabled)
        {
            var conn = _connectionFactory();
            _stats.TotalCreated++;
            _stats.CurrentActiveConnections++;
            return conn;
        }

        // 等待池信号量
        await _poolSemaphore.WaitAsync(cancellationToken);

        // 尝试从池中获取可用连接
        while (_pool.TryDequeue(out var pooled))
        {
            if (pooled.IsExpired(_options))
            {
                // 连接已过期，关闭并从 active 列表移除
                _activeConnections.TryRemove(pooled.Connection, out _);
                pooled.Dispose();
                _stats.TotalDisposed++;
                continue;
            }

            // 复用连接
            pooled.IsInUse = true;
            pooled.LastUsedAt = DateTime.UtcNow;
            _stats.TotalReused++;
            _stats.CurrentActiveConnections++;
            _logger.LogDebug("Reusing connection for project {ProjectName} (reuse count: {Reused})",
                _projectName, _stats.TotalReused);
            
            return new PooledDbConnection(pooled.Connection, this);
        }

        // 池中没有可用连接，创建新连接
        var newConn = _connectionFactory();
        _stats.TotalCreated++;
        _stats.CurrentActiveConnections++;
        _logger.LogDebug("Created new connection for project {ProjectName} (total created: {Created})",
            _projectName, _stats.TotalCreated);

        var pooledNew = new PooledConnection(newConn)
        {
            IsInUse = true
        };
        _activeConnections[newConn] = pooledNew;

        return new PooledDbConnection(newConn, this);
    }

    /// <summary>
    /// 释放连接回池
    /// </summary>
    public void Release(IDbConnection connection)
    {
        if (_disposed || connection == null)
            return;

        // 如果是 PooledDbConnection，解包出底层原生连接
        var connToRelease = connection;
        if (connection is PooledDbConnection pooledDbConn)
        {
            connToRelease = pooledDbConn.InnerConnection;
        }

        if (!_options.Enabled)
        {
            // 未启用池，直接关闭
            try
            {
                if (connToRelease.State == ConnectionState.Open)
                    connToRelease.Close();
                connToRelease.Dispose();
            }
            catch { }
            _stats.CurrentActiveConnections--;
            return;
        }

        if (_activeConnections.TryGetValue(connToRelease, out var pooled))
        {
            // 确保同一个连接不会被重复归还
            if (!pooled.IsInUse)
            {
                return;
            }

            pooled.IsInUse = false;
            pooled.LastUsedAt = DateTime.UtcNow;

            if (_pool.Count < _options.MaxPoolSize)
            {
                _pool.Enqueue(pooled);
                _stats.CurrentPooledConnections = _pool.Count;
            }
            else
            {
                // 池已满，从 active 列表中彻底移除并关闭物理连接
                if (_activeConnections.TryRemove(connToRelease, out _))
                {
                    pooled.Dispose();
                    _stats.TotalDisposed++;
                    _logger.LogDebug("Pool full for project {ProjectName}, closing connection", _projectName);
                }
            }

            _stats.CurrentActiveConnections--;
            _poolSemaphore.Release();
        }
        else
        {
            // 底层连接不在活跃字典中，说明已经被释放或重置过，直接关闭它即可
            try
            {
                if (connToRelease.State == ConnectionState.Open)
                    connToRelease.Close();
                connToRelease.Dispose();
            }
            catch { }
        }
    }

    /// <summary>
    /// 清理过期连接
    /// </summary>
    private void CleanupExpiredConnections(object? state)
    {
        if (_disposed)
            return;

        var expiredConnections = new List<PooledConnection>();
        var remaining = new List<PooledConnection>();

        while (_pool.TryDequeue(out var pooled))
        {
            if (pooled.IsInUse)
            {
                remaining.Add(pooled);
            }
            else if (pooled.IsExpired(_options))
            {
                expiredConnections.Add(pooled);
            }
            else
            {
                remaining.Add(pooled);
            }
        }

        // 关闭并从 active 中移除过期连接
        foreach (var expired in expiredConnections)
        {
            _activeConnections.TryRemove(expired.Connection, out _);
            expired.Dispose();
            _stats.TotalDisposed++;
        }

        // 重新入队有效连接
        foreach (var valid in remaining)
        {
            _pool.Enqueue(valid);
        }

        _stats.CurrentPooledConnections = _pool.Count;

        if (expiredConnections.Count > 0)
        {
            _logger.LogInformation(
                "Cleaned up {Count} expired connections for project {ProjectName}",
                expiredConnections.Count, _projectName);
        }
    }

    /// <summary>
    /// 获取连接池统计信息
    /// </summary>
    public ConnectionPoolStats GetStats()
    {
        return new ConnectionPoolStats
        {
            TotalCreated = _stats.TotalCreated,
            TotalReused = _stats.TotalReused,
            TotalDisposed = _stats.TotalDisposed,
            CurrentActiveConnections = _stats.CurrentActiveConnections,
            CurrentPooledConnections = _stats.CurrentPooledConnections
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cleanupTimer.Dispose();

        // 清空队列
        _pool.Clear();

        // 彻底释放 active 字典里记录的所有原生连接
        foreach (var kv in _activeConnections)
        {
            kv.Value.Dispose();
        }
        _activeConnections.Clear();

        _poolSemaphore.Dispose();
        _logger.LogInformation("Connection pool disposed for project {ProjectName}", _projectName);
    }
}

/// <summary>
/// 连接包装代理类 - 在 using / Dispose 时自动安全释放回池
/// </summary>
internal class PooledDbConnection : IDbConnection
{
    private readonly IDbConnection _inner;
    private readonly ConnectionPool _pool;
    private bool _disposed;

    public IDbConnection InnerConnection => _inner;

    public PooledDbConnection(IDbConnection inner, ConnectionPool pool)
    {
        _inner = inner;
        _pool = pool;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _pool.Release(_inner);
        }
    }

    public string ConnectionString 
    { 
        get => _inner.ConnectionString ?? string.Empty; 
        set => _inner.ConnectionString = value; 
    }

    public int ConnectionTimeout => _inner.ConnectionTimeout;
    public string Database => _inner.Database;
    public ConnectionState State => _inner.State;

    public IDbTransaction BeginTransaction() => _inner.BeginTransaction();
    public IDbTransaction BeginTransaction(IsolationLevel il) => _inner.BeginTransaction(il);
    public void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
    public void Close() => Dispose(); // Close 动作同样重构为自动归还
    public IDbCommand CreateCommand() => _inner.CreateCommand();
    public void Open() => _inner.Open();
}
