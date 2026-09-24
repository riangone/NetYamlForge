namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 声明式 AI 角色定义加载与解析契约
/// </summary>
public interface IRoleDefinitionProvider
{
    /// <summary>
    /// 获取指定名称的角色定义。优先从项目特定配置加载，不存在时回退至系统默认角色定义。
    /// </summary>
    RoleDefinition? GetRole(string roleName, string? projectName = null);

    /// <summary>
    /// 获取指定项目或系统范围内的全部角色定义字典。
    /// </summary>
    IReadOnlyDictionary<string, RoleDefinition> GetRoles(string? projectName = null);

    /// <summary>
    /// 重新加载角色定义缓存
    /// </summary>
    void Reload(string? projectName = null);
}
