namespace NetYamlForge.AI.Models;

/// <summary>
/// AI コマンド実行ログ（ユーザーが入力した指令と実行結果を記録）
/// </summary>
public class CommandLog
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string CliTool { get; set; } = "";
    public string InputText { get; set; } = "";
    public string? ProjectName { get; set; }
    public string? SessionId { get; set; }
    public string Status { get; set; } = ""; // Pending, Running, Completed, Failed, Cancelled
    public string? ResultText { get; set; }
    public string? ErrorText { get; set; }
    public long? DurationMs { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? CompletedAt { get; set; }
}
