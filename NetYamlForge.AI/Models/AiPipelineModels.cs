namespace NetYamlForge.AI.Models;

/// <summary>AI タスク実行レコード</summary>
public class AiTaskRecord
{
    public string TaskId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string Status { get; set; } = "pending";
    public string WorkDirectory { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int GeneratedFilesCount { get; set; }
}

/// <summary>AI タスク詳細ビューモデル</summary>
public class AiTaskDetailsModel
{
    public AiTaskRecord? TaskRecord { get; set; }
    public string WorkDirectory { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public List<string> Logs { get; set; } = new();
}
