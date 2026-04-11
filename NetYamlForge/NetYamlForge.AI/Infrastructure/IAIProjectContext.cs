namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// AI 聊天服务的项目上下文接口（适配层）
/// 用于解耦 AI 服务对主框架 ProjectScope 的依赖
/// </summary>
public interface IAIProjectContext
{
    /// <summary>
    /// 当前项目名称
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// 是否已设置项目上下文
    /// </summary>
    bool IsSet { get; }

    /// <summary>
    /// 获取数据库连接路径/字符串
    /// </summary>
    string GetDatabasePath();

    /// <summary>
    /// 创建数据库连接
    /// </summary>
    System.Data.IDbConnection CreateConnection();
}
