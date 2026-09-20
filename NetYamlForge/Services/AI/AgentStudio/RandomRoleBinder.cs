using System.Security.Cryptography;

namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 使用加密强随机或 Fisher-Yates 算法实现 AI CLI 与角色的无偏随机绑定
/// </summary>
public class RandomRoleBinder : IRandomRoleBinder
{
    public static readonly IReadOnlyList<string> DefaultProviders = ["claude", "antigravity", "opencode"];
    public const string RoleArchitect = "Architect";
    public const string RoleDeveloper = "Developer";
    public const string RoleTester = "Tester";

    public AgentTaskAllocation Shuffle(IReadOnlyList<string>? customProviders = null)
    {
        var providers = (customProviders != null && customProviders.Count >= 3)
            ? customProviders.Take(3).ToList()
            : DefaultProviders.ToList();

        // Fisher-Yates shuffle
        for (int i = providers.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (providers[i], providers[j]) = (providers[j], providers[i]);
        }

        return new AgentTaskAllocation(
            Architect: new RoleAssignment(RoleArchitect, providers[0]),
            Developer: new RoleAssignment(RoleDeveloper, providers[1]),
            Tester: new RoleAssignment(RoleTester, providers[2])
        );
    }
}
