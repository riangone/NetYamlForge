namespace NetYamlForge.AI.Models.Debate;

public class DebateTopic
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string LeftAi { get; set; } = "claude";
    public string RightAi { get; set; } = "qwen";
    public string? LeftRole { get; set; }
    public string? RightRole { get; set; }
    public string Status { get; set; } = "pending"; // pending/running/completed/cancelled/error
    public int LeftCount { get; set; }
    public int RightCount { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class DebateMessage
{
    public int Id { get; set; }
    public int TopicId { get; set; }
    public string AiSide { get; set; } = ""; // "left" or "right"
    public string AiProvider { get; set; } = "";
    public string Content { get; set; } = "";
    public int MessageIndex { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class CreateTopicRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string LeftAi { get; set; } = "claude";
    public string RightAi { get; set; } = "qwen";
    public string? LeftRole { get; set; }
    public string? RightRole { get; set; }
}

public class DebateStatusUpdate
{
    public string Status { get; set; } = "";
    public int LeftCount { get; set; }
    public int RightCount { get; set; }
    public string? Error { get; set; }
}
