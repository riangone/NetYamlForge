namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 配置
/// </summary>
public class CliConfig
{
    public const string SectionName = "AICli";
    
    /// <summary>
    /// 默认 CLI 工具
    /// </summary>
    public string DefaultTool { get; set; } = "claude";
    
    /// <summary>
    /// 任务超时时间（秒）
    /// </summary>
    public int TaskTimeoutSeconds { get; set; } = 600;  // 10 分钟
    
    /// <summary>
    /// 最大并发任务数
    /// </summary>
    public int MaxConcurrentTasks { get; set; } = 2;
    
    /// <summary>
    /// CLI 命令路径（可选，默认从 PATH 查找）
    /// </summary>
    public string? ClaudePath { get; set; }
    public string? QwenCodePath { get; set; }
    
    /// <summary>
    /// 默认工作目录（可选，默认使用项目目录）
    /// </summary>
    public string? DefaultWorkingDirectory { get; set; }
    
    /// <summary>
    /// 工具权限控制
    /// </summary>
    public List<string> DefaultAllowedTools { get; set; } = new()
    {
        "Read", "Write", "Edit", "Bash", "Git"
    };
}
