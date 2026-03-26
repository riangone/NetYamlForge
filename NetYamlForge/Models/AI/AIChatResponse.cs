using System.Text.Json.Serialization;

namespace NetYamlForge.Models.AI;

/// <summary>
/// AI 聊天响应
/// </summary>
public class AIChatResponse
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    
    /// <summary>
    /// 初始响应消息
    /// </summary>
    public string? Message { get; set; }
    
    /// <summary>
    /// 任务状态
    /// </summary>
    public TaskStatus Status { get; set; }
    
    /// <summary>
    /// 进度百分比 (0-100)
    /// </summary>
    public int Progress { get; set; }
    
    /// <summary>
    /// タスク完了時の結果テキスト（CLIの最終出力）
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// 会话 ID（用于后续对话）
    /// </summary>
    public string? SessionId { get; set; }
}
