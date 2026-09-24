using System.Data;

namespace NetYamlForge.Services.Database;

/// <summary>
/// 数据库连接门面接口（Thin Facade）
/// 提供租户/项目维度的原生连接获取契约，直接委托给底层 ADO.NET 原生驱动连接池
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// 为指定项目获取打开的数据库连接（同步）
    /// </summary>
    IDbConnection CreateConnection(string? projectName);

    /// <summary>
    /// 为指定项目获取打开的数据库连接（异步）
    /// </summary>
    Task<IDbConnection> CreateConnectionAsync(string? projectName, CancellationToken cancellationToken = default);
}
