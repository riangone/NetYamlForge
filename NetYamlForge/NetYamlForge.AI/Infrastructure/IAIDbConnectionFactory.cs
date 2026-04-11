namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// 数据库连接工厂接口（适配层）
/// 用于解耦 AI 服务对主框架 IDbConnectionFactory 的依赖
/// </summary>
public interface IAIDbConnectionFactory
{
    /// <summary>
    /// 创建数据库连接
    /// </summary>
    System.Data.IDbConnection CreateConnection(string? projectName = null);
}
