using System.Text.Json;

namespace NetYamlForge.Models.AI;

/// <summary>
/// AI 任务实体
/// </summary>
public class AITask
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string CliTool { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Project { get; set; }
    public string? SessionId { get; set; }  // CLI session ID
    public TaskStatus Status { get; set; }
    public int Progress { get; set; }
    public string? Result { get; set; }     // CLI 返回的完整结果
    public string? Error { get; set; }
    public List<string> Logs { get; set; } = new();
    public int? ProcessId { get; set; }     // CLI 进程 ID（用于取消）
    public List<string>? AllowedTools { get; set; }  // 允许使用的工具列表
    public string? Context { get; set; }  // ✨ 角色上下文（staff/customer/employee 等）
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
