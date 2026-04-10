using System.Data;

namespace NetYamlForge.Services.Connection;

/// <summary>
/// 连接作用域 - 自动管理连接的获取和释放
/// 使用模式：using var scope = await ConnectionScope.CreateAsync(_connectionManager);
/// </summary>
public class ConnectionScope : IAsyncDisposable, IDisposable
{
    private readonly IConnectionManager _connectionManager;
    private readonly string? _projectName;
    private readonly IDbConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// 获取连接
    /// </summary>
    public IDbConnection Connection => _connection
        ?? throw new ObjectDisposedException(nameof(ConnectionScope));

    private ConnectionScope(
        IConnectionManager connectionManager,
        string? projectName,
        IDbConnection connection)
    {
        _connectionManager = connectionManager;
        _projectName = projectName;
        _connection = connection;
    }

    /// <summary>
    /// 从当前 ProjectScope 创建连接作用域
    /// </summary>
    public static async Task<ConnectionScope> CreateAsync(IConnectionManager connectionManager)
    {
        var connection = await connectionManager.GetConnectionAsync();
        return new ConnectionScope(connectionManager, null, connection);
    }

    /// <summary>
    /// 从指定项目创建连接作用域
    /// </summary>
    public static async Task<ConnectionScope> CreateAsync(
        IConnectionManager connectionManager,
        string projectName)
    {
        var connection = await connectionManager.GetConnectionAsync(projectName);
        return new ConnectionScope(connectionManager, projectName, connection);
    }

    /// <summary>
    /// 释放连接回池
    /// </summary>
    private void Release()
    {
        if (_disposed || _connection == null)
            return;

        _disposed = true;

        try
        {
            if (_projectName != null)
            {
                _connectionManager.ReleaseConnection(_projectName, _connection);
            }
            else
            {
                _connectionManager.ReleaseConnection(_connection);
            }
        }
        catch
        {
            // 忽略释放异常
        }
    }

    public void Dispose()
    {
        Release();
    }

    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }
}
