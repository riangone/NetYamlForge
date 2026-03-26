using System.Text.Json;

namespace NetYamlForge.Models.AI;

/// <summary>
/// 进度更新（流式）
/// </summary>
public class ProgressUpdate
{
    public string TaskId { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? Message { get; set; }
    public List<string> Logs { get; set; } = new();
    public TaskStatus Status { get; set; }
    
    /// <summary>
    /// CLI から返された session_id（マルチターン会話の継続に使用）
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Stream-JSON 模式的原始数据
    /// </summary>
    public JsonElement? StreamData { get; set; }
}
