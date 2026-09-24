using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 声明式 AI 角色定义提供者实现。
/// 支持从项目的 ai/roles.yaml 或 roles.yaml 中加载自定义角色，若未定义则回退至系统内置角色。
/// </summary>
public class RoleDefinitionProvider : IRoleDefinitionProvider
{
    private readonly ProjectManager? _projectManager;
    private readonly ILogger<RoleDefinitionProvider> _logger;
    private readonly IDeserializer _deserializer;
    private readonly ConcurrentDictionary<string, Dictionary<string, RoleDefinition>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RoleDefinitionProvider(
        ProjectManager? projectManager = null,
        ILogger<RoleDefinitionProvider>? logger = null)
    {
        _projectManager = projectManager;
        _logger = logger ?? NullLogger<RoleDefinitionProvider>.Instance;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public RoleDefinition? GetRole(string roleName, string? projectName = null)
    {
        var roles = GetRoles(projectName);
        if (roles.TryGetValue(roleName, out var role))
        {
            return role;
        }

        // 若项目角色集中未找到，尝试从默认角色集中检索
        var defaultRoles = GetDefaultRoles();
        if (defaultRoles.TryGetValue(roleName, out var defaultRole))
        {
            return defaultRole;
        }

        return null;
    }

    public IReadOnlyDictionary<string, RoleDefinition> GetRoles(string? projectName = null)
    {
        var key = projectName ?? "__system__";
        return _cache.GetOrAdd(key, _ => LoadRoles(projectName));
    }

    public void Reload(string? projectName = null)
    {
        var key = projectName ?? "__system__";
        _logger.LogInformation("[RoleDefinitionProvider] Reloading roles for scope '{Scope}'", key);
        _cache[key] = LoadRoles(projectName);
    }

    private Dictionary<string, RoleDefinition> LoadRoles(string? projectName)
    {
        var merged = new Dictionary<string, RoleDefinition>(GetDefaultRoles(), StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(projectName) || _projectManager == null)
        {
            return merged;
        }

        if (!_projectManager.TryGet(projectName, out var project) || project == null)
        {
            return merged;
        }

        var candidatePaths = new[]
        {
            Path.Combine(project.ProjectDir, "ai", "roles.yaml"),
            Path.Combine(project.ProjectDir, "ai", "roles.yml"),
            Path.Combine(project.ProjectDir, "roles.yaml"),
            Path.Combine(project.ProjectDir, "roles.yml")
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var yaml = File.ReadAllText(path);
                    var config = _deserializer.Deserialize<RoleConfigFile>(yaml);
                    if (config?.Roles != null)
                    {
                        foreach (var (name, def) in config.Roles)
                        {
                            if (string.IsNullOrWhiteSpace(def.Name))
                            {
                                def.Name = name;
                            }
                            merged[name] = def;
                        }
                        _logger.LogInformation("[RoleDefinitionProvider] Successfully loaded {Count} roles from {Path}", config.Roles.Count, path);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RoleDefinitionProvider] Failed to load role configuration from {Path}", path);
                }
            }
        }

        return merged;
    }

    public static Dictionary<string, RoleDefinition> GetDefaultRoles()
    {
        return new Dictionary<string, RoleDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [RandomRoleBinder.RoleArchitect] = new()
            {
                Name = RandomRoleBinder.RoleArchitect,
                Description = "项目架构师，聚焦系统高层结构设计与模式规范",
                Responsibilities = new()
                {
                    "You are the project architect. Focus on high-level structure and design patterns.",
                    "Analyze the requirements thoroughly.",
                    "Define architectural boundaries, interfaces, and component interactions.",
                    "Produce a clear Specification/RFC with concrete file paths and contract signatures.",
                    "Provide actionable, step-by-step guidance for the Developer."
                },
                Guidelines = new()
                {
                    "Keep your analysis, code, or test generation focused strictly on the specified task.",
                    "Avoid broad recursive repository indexing. Only inspect files directly relevant to this task.",
                    "Provide your complete output in your response without hanging or waiting for user confirmation."
                }
            },
            [RandomRoleBinder.RoleDeveloper] = new()
            {
                Name = RandomRoleBinder.RoleDeveloper,
                Description = "核心软件工程师，负责高质量代码落地与模式遵从",
                Responsibilities = new()
                {
                    "Implement the feature or fix according to the Architect's specification.",
                    "Write clean, robust, and idiomatic C# code targeting .NET 10.",
                    "Respect existing architecture, conventions, and patterns in NetYamlForge.",
                    "Ensure code compiles cleanly with 0 errors."
                },
                Guidelines = new()
                {
                    "Keep your analysis, code, or test generation focused strictly on the specified task.",
                    "Avoid broad recursive repository indexing. Only inspect files directly relevant to this task.",
                    "Provide your complete output in your response without hanging or waiting for user confirmation."
                }
            },
            [RandomRoleBinder.RoleTester] = new()
            {
                Name = RandomRoleBinder.RoleTester,
                Description = "质量保障工程师，设计回归测试套件并验证系统健壮性",
                Responsibilities = new()
                {
                    "Design and write comprehensive xUnit unit and integration tests.",
                    "Verify test cases against edge cases, failure modes, and regression risks.",
                    "Execute or verify tests with `dotnet test`.",
                    "Report any issues, defects, or test failures with clear reproduction steps."
                },
                Guidelines = new()
                {
                    "Keep your analysis, code, or test generation focused strictly on the specified task.",
                    "Avoid broad recursive repository indexing. Only inspect files directly relevant to this task.",
                    "Provide your complete output in your response without hanging or waiting for user confirmation."
                }
            },
            ["CodeReviewer"] = new()
            {
                Name = "CodeReviewer",
                Description = "资深代码评审员，负责代码安全、质量与坏味道排查",
                Responsibilities = new()
                {
                    "Review code for security vulnerabilities, concurrency bugs, and architectural deviations.",
                    "Verify adherence to .NET 10 idioms and NetYamlForge conventions.",
                    "Provide constructive, actionable feedback."
                },
                Guidelines = new()
                {
                    "Be meticulous and specific with line numbers and proposed fixes.",
                    "Prioritize safety and maintainability."
                }
            },
            ["DevOpsEngineer"] = new()
            {
                Name = "DevOpsEngineer",
                Description = "DevOps 与基础设施专家，关注容器化、CI/CD 与运行期编排",
                Responsibilities = new()
                {
                    "Manage container configurations (Docker, Kubernetes) and CI/CD pipelines.",
                    "Ensure observability, logging, and deployment automation readiness."
                },
                Guidelines = new()
                {
                    "Ensure all deployment artifacts are reproducible and secure."
                }
            }
        };
    }
}
