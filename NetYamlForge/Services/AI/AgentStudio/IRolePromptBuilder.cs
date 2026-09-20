namespace NetYamlForge.Services.AI.AgentStudio;

/// <summary>
/// 角色专有 Prompt 构建器契约
/// </summary>
public interface IRolePromptBuilder
{
    /// <summary>
    /// 为指定角色构建包含上下文、规则与输出格式约束的完整 Prompt
    /// </summary>
    string BuildPrompt(string role, string taskTitle, string taskDescription, string? priorContext = null);
}
