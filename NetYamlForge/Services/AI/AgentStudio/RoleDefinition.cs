namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 声明式 AI 角色定义
/// </summary>
public class RoleDefinition
{
    /// <summary>角色标识/名称（如 Architect, Developer, Tester）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>角色显示描述</summary>
    public string? Description { get; set; }

    /// <summary>核心职责清单</summary>
    public List<string> Responsibilities { get; set; } = new();

    /// <summary>操作指南与执行规范</summary>
    public List<string> Guidelines { get; set; } = new();

    /// <summary>自定义系统提示词覆盖（可选）</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>允许调用的工具列表（可选）</summary>
    public List<string> AllowedTools { get; set; } = new();
}

/// <summary>
/// roles.yaml 配置文件根结构
/// </summary>
public class RoleConfigFile
{
    public Dictionary<string, RoleDefinition> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
