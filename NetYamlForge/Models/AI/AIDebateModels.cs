namespace NetYamlForge.Models.AI;

/// <summary>AI ディベートセッション</summary>
public class DebateSession
{
    public long Id { get; set; }
    public string Topic { get; set; } = "";
    public string LeftProvider { get; set; } = "claude";
    public string RightProvider { get; set; } = "qwen";
    public string LeftRole { get; set; } = "";
    public string RightRole { get; set; } = "";
    public string Status { get; set; } = "pending"; // pending / active / completed / error
    public string? CreatedBy { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

/// <summary>AI ディベートのメッセージ</summary>
public class DebateMessage
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public string Side { get; set; } = ""; // "left" or "right"
    public string Provider { get; set; } = "";
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public int MessageNumber { get; set; }
    public string CreatedAt { get; set; } = "";
}

/// <summary>ディベートセッション作成リクエスト</summary>
public class CreateDebateRequest
{
    public string Topic { get; set; } = "";
    public string LeftProvider { get; set; } = "claude";
    public string RightProvider { get; set; } = "qwen";
    public string LeftRole { get; set; } = "Advocate";
    public string RightRole { get; set; } = "Critic";
}

/// <summary>SignalR で流すメッセージイベント</summary>
public class DebateMessageEvent
{
    public long SessionId { get; set; }
    public long MessageId { get; set; }
    public string Side { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public int MessageNumber { get; set; }
    public string CreatedAt { get; set; } = "";
    public bool IsFinal { get; set; }
}

/// <summary>ディベート状態変更イベント</summary>
public class DebateStatusEvent
{
    public long SessionId { get; set; }
    public string Status { get; set; } = "";
    public string? Message { get; set; }
}
