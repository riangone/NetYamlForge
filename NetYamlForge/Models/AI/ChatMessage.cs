namespace NetYamlForge.Models.AI;

public class ChatMessage
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string Content { get; set; } = "";
    public string Type { get; set; } = ""; // user | assistant
    public string CreatedAt { get; set; } = "";
}

public class SaveChatMessageRequest
{
    public string Content { get; set; } = "";
    public string Type { get; set; } = "";
}
