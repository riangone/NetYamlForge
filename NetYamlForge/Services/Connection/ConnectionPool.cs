using System.Data;

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
    /// 是否启用连接池（默认 false，改用原生连接池）
    /// </summary>
    public bool Enabled { get; set; } = false;
}
