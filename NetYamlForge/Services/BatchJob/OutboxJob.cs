namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 永続化アウトボックスジョブのレコード表現
/// </summary>
public class OutboxJob
{
    public string Id { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public string ScheduledAt { get; set; } = string.Empty;
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProjectName { get; set; }
}
