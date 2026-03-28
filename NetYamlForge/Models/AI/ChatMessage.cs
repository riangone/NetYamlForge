namespace NetYamlForge.Models.AI;

public class ChatMessage
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string Content { get; set; } = "";
    public string Type { get; set; } = ""; // user | assistant
    public string CreatedAt { get; set; } = "";
    public string? Provider { get; set; } // AI 提供者标识 (qwen, claude, copilot, etc.)
    
    /// <summary>
    /// 格式化的显示时间（包含日期和时间）
    /// </summary>
    public string? DisplayTime
    {
        get
        {
            if (string.IsNullOrEmpty(CreatedAt)) return null;
            if (DateTime.TryParse(CreatedAt, out var dt))
            {
                return dt.ToString("yyyy/MM/dd HH:mm:ss");
            }
            return CreatedAt;
        }
    }
}

public class SaveChatMessageRequest
{
    public string Content { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Provider { get; set; } // AI 提供者标识
}
