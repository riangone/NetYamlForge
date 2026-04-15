// AI 双模式配置
// 用于控制 AI 服务是以嵌入式运行还是通过独立进程调用

namespace NetYamlForge.AI.Config;

/// <summary>
/// AI 服务模式配置
/// </summary>
public class AIModeConfig
{
    public const string SectionName = "AI";

    /// <summary>
    /// AI 服务模式
    /// - "embedded": 嵌入式（默认，AI 服务在主进程内运行）
    /// - "standalone": 独立进程（通过 HTTP API + SignalR 通信）
    /// </summary>
    public string Mode { get; set; } = "embedded";

    /// <summary>
    /// 独立进程模式下的 AI 服务地址
    /// 默认: http://localhost:5200
    /// </summary>
    public string ServiceBaseUrl { get; set; } = "http://localhost:5200";

    /// <summary>
    /// 是否启用 SignalR 连接到独立 AI 服务
    /// </summary>
    public bool EnableSignalRProxy { get; set; } = true;
}
