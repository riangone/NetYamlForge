using System.Text.Json.Serialization;

namespace NetYamlForge.Models.AI;

/// <summary>
/// AI 聊天请求
/// </summary>
public class AIChatRequest
{
    /// <summary>
    /// 用户输入的指令
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// 选择的 CLI 工具 (claude, qwen-code, etc.)
    /// </summary>
    public string CliTool { get; set; } = "claude";
    
    /// <summary>
    /// 目标项目名
    /// </summary>
    public string? Project { get; set; }
    
    /// <summary>
    /// 会话 ID（用于多轮对话，对应 CLI 的 --resume）
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// 是否启用流式输出 (--output-format stream-json)
    /// </summary>
    public bool Streaming { get; set; } = true;
    
    /// <summary>
    /// 允许的工具列表 (--allowedTools)
    /// </summary>
    public List<string>? AllowedTools { get; set; }

    /// <summary>
    /// ✨ 角色上下文（staff/customer/employee/contract_manager 等）
    /// </summary>
    public string? Context { get; set; }
}
