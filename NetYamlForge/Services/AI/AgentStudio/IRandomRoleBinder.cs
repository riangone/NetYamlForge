namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 角色分配项：角色与绑定的 CLI Provider
/// </summary>
public record RoleAssignment(string Role, string Provider, string? Model = null);

/// <summary>
/// 任务的角色分配矩阵：Architect / Developer / Tester
/// </summary>
public record AgentTaskAllocation(
    RoleAssignment Architect,
    RoleAssignment Developer,
    RoleAssignment Tester
)
{
    public IReadOnlyList<RoleAssignment> All => [Architect, Developer, Tester];
}

/// <summary>
/// 角色随机洗牌绑定器契约
/// </summary>
public interface IRandomRoleBinder
{
    /// <summary>
    /// 将支持的 AI CLI（claude, antigravity, opencode）随机无偏绑定到三个角色（Architect, Developer, Tester）
    /// </summary>
    AgentTaskAllocation Shuffle(IReadOnlyList<string>? customProviders = null);
}
