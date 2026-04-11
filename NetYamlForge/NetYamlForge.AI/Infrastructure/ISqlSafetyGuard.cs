namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// SQL 安全验证接口（适配层）
/// 用于解耦 AI 服务对主框架 SqlSafetyGuard 的依赖
/// </summary>
public interface ISqlSafetyGuard
{
    /// <summary>
    /// 检查是否包含不安全的 SQL 令牌
    /// </summary>
    bool IsUnsafeToken(string input);

    /// <summary>
    /// 检查是否为有效的 SQL 标识符
    /// </summary>
    bool IsValidIdentifier(string identifier);

    /// <summary>
    /// 清理/转 SQL 标识符
    /// </summary>
    string SanitizeIdentifier(string identifier);
}
