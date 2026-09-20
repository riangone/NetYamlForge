using System.Text;

namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 角色专有 Prompt 构建器实现
/// </summary>
public class RolePromptBuilder : IRolePromptBuilder
{
    public string BuildPrompt(string role, string taskTitle, string taskDescription, string? priorContext = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Role: {role}");
        sb.AppendLine("You are an autonomous AI Agent operating in the NetYamlForge repository.");
        sb.AppendLine("NetYamlForge is a lightweight .NET 10 Web framework driven by YAML configurations and C# hooks.");
        sb.AppendLine();

        switch (role)
        {
            case RandomRoleBinder.RoleArchitect:
                sb.AppendLine("## Your Responsibilities as Architect:");
                sb.AppendLine("- You are the project architect. Focus on high-level structure and design patterns.");
                sb.AppendLine("- Analyze the requirements thoroughly.");
                sb.AppendLine("- Define architectural boundaries, interfaces, and component interactions.");
                sb.AppendLine("- Produce a clear Specification/RFC with concrete file paths and contract signatures.");
                sb.AppendLine("- Provide actionable, step-by-step guidance for the Developer.");
                break;

            case RandomRoleBinder.RoleDeveloper:
                sb.AppendLine("## Your Responsibilities as Developer:");
                sb.AppendLine("- Implement the feature or fix according to the Architect's specification.");
                sb.AppendLine("- Write clean, robust, and idiomatic C# code targeting .NET 10.");
                sb.AppendLine("- Respect existing architecture, conventions, and patterns in NetYamlForge.");
                sb.AppendLine("- Ensure code compiles cleanly with 0 errors.");
                break;

            case RandomRoleBinder.RoleTester:
                sb.AppendLine("## Your Responsibilities as Tester:");
                sb.AppendLine("- Design and write comprehensive xUnit unit and integration tests.");
                sb.AppendLine("- Verify test cases against edge cases, failure modes, and regression risks.");
                sb.AppendLine("- Execute or verify tests with `dotnet test`.");
                sb.AppendLine("- Report any issues, defects, or test failures with clear reproduction steps.");
                break;

            default:
                sb.AppendLine($"## Role Instructions for {role}:");
                sb.AppendLine("- Complete the assigned task following best engineering practices.");
                break;
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
        sb.AppendLine("Please proceed with your role-specific output now.");

        return sb.ToString();
    }
}
