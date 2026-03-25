namespace NetYamlForge.Models.AI;

/// <summary>
/// CLI 工具信息
/// </summary>
public class CliToolInfo
{
    /// <summary>
    /// 工具名称（命令）
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// 是否已安装
    /// </summary>
    public bool Installed { get; set; }
    
    /// <summary>
    /// 版本号
    /// </summary>
    public string? Version { get; set; }
    
    /// <summary>
    /// 是否已认证（OAuth）
    /// </summary>
    public bool Authenticated { get; set; }
    
    /// <summary>
    /// 支持的功能
    /// </summary>
    public List<string> Capabilities { get; set; } = new();
}
