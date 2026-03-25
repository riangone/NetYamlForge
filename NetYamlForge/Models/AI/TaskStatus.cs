namespace NetYamlForge.Models.AI;

/// <summary>
/// AI 任务状态
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// 等待处理
    /// </summary>
    Pending,
    
    /// <summary>
    /// 正在执行（CLI 进程运行中）
    /// </summary>
    Running,
    
    /// <summary>
    /// 已完成
    /// </summary>
    Completed,
    
    /// <summary>
    /// 失败
    /// </summary>
    Failed,
    
    /// <summary>
    /// 已取消（CLI 进程已终止）
    /// </summary>
    Cancelled
}
