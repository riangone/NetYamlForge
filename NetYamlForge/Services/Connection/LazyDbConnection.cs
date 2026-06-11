using System.Data;

namespace NetYamlForge.Services.Connection;

/// <summary>
/// 惰性数据库连接包装器 - 在真正需要使用数据库（如打开连接、创建命令）时才向连接池申请物理连接。
/// 同时通过延迟获取，消除了 DI 注入时的同步阻塞（Sync-over-Async）反模式。
/// </summary>
public class LazyDbConnection : IDbConnection
{
    private readonly Func<IDbConnection> _connectionFactory;
    private IDbConnection? _innerConnection;
    private bool _disposed;

    public LazyDbConnection(Func<IDbConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>
    /// 获取底层的实际连接（惰性加载）
    /// </summary>
    public IDbConnection InnerConnection
    {
        get
        {
            if (_innerConnection == null)
            {
                _innerConnection = _connectionFactory();
                if (_innerConnection.State != ConnectionState.Open)
                {
                    _innerConnection.Open();
                }
            }
            return _innerConnection;
        }
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public string ConnectionString
    {
        get => InnerConnection.ConnectionString ?? string.Empty;
        set => InnerConnection.ConnectionString = value ?? string.Empty;
    }

    public int ConnectionTimeout => InnerConnection.ConnectionTimeout;

    public string Database => InnerConnection.Database;

    public ConnectionState State => _innerConnection == null ? ConnectionState.Open : _innerConnection.State;

    public IDbTransaction BeginTransaction() => InnerConnection.BeginTransaction();

    public IDbTransaction BeginTransaction(IsolationLevel il) => InnerConnection.BeginTransaction(il);

    public void ChangeDatabase(string databaseName) => InnerConnection.ChangeDatabase(databaseName);

    public void Close()
    {
        _innerConnection?.Close();
    }

    public IDbCommand CreateCommand() => InnerConnection.CreateCommand();

    public void Open()
    {
        var conn = InnerConnection;
        if (conn.State != ConnectionState.Open)
        {
            conn.Open();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _innerConnection?.Dispose();
            _innerConnection = null;
        }
    }
}
