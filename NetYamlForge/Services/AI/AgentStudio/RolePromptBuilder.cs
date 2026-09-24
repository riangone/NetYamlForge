using System.Text;

namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 角色专有 Prompt 构建器实现。
/// 依赖 IRoleDefinitionProvider 获取 YAML 声明式配置的角色职责与指南，消除 C# 内部硬编码。
/// </summary>
public class RolePromptBuilder : IRolePromptBuilder
{
    private readonly IRoleDefinitionProvider _roleDefinitionProvider;

    public RolePromptBuilder(IRoleDefinitionProvider? roleDefinitionProvider = null)
    {
        _roleDefinitionProvider = roleDefinitionProvider ?? new RoleDefinitionProvider();
    }

    public string BuildPrompt(
        string role,
        string taskTitle,
        string taskDescription,
        string? priorContext = null,
        string? projectName = null)
    {
        var sb = new StringBuilder();

        var roleDef = _roleDefinitionProvider.GetRole(role, projectName);

        var roleName = roleDef?.Name ?? role;
        sb.AppendLine($"# Role: {roleName}");

        if (!string.IsNullOrWhiteSpace(roleDef?.SystemPrompt))
        {
            sb.AppendLine(roleDef.SystemPrompt);
        }
        else
        {
            sb.AppendLine("You are an autonomous AI Agent operating in the NetYamlForge repository.");
            sb.AppendLine("NetYamlForge is a lightweight .NET 10 Web framework driven by YAML configurations and C# hooks.");
        }
        sb.AppendLine();

        if (roleDef != null && roleDef.Responsibilities.Count > 0)
        {
            sb.AppendLine($"## Your Responsibilities as {roleName}:");
            foreach (var resp in roleDef.Responsibilities)
            {
                sb.AppendLine($"- {resp}");
            }
        }
        else
        {
            sb.AppendLine($"## Role Instructions for {roleName}:");
            sb.AppendLine("- Complete the assigned task following best engineering practices.");
        }

        sb.AppendLine();
        sb.AppendLine("## Task Details:");
        sb.AppendLine($"**Task**: {taskTitle}");
        sb.AppendLine($"**Description**: {taskDescription}");

        if (!string.IsNullOrWhiteSpace(priorContext))
        {
            sb.AppendLine();
            sb.AppendLine("## Prior Stage Output & Context:");
            sb.AppendLine(priorContext);
        }

        sb.AppendLine();
        sb.AppendLine("## Operational Guidelines:");
        if (roleDef != null && roleDef.Guidelines.Count > 0)
        {
            foreach (var guide in roleDef.Guidelines)
            {
                sb.AppendLine($"- {guide}");
            }
        }
        else
        {
            sb.AppendLine("- Keep your analysis, code, or test generation focused strictly on the specified task.");
            sb.AppendLine("- Avoid broad recursive repository indexing. Only inspect files directly relevant to this task.");
            sb.AppendLine("- Provide your complete output in your response without hanging or waiting for user confirmation.");
        }
        sb.AppendLine();
        sb.AppendLine("Please proceed with your role-specific output now.");

        return sb.ToString();
    }
}
