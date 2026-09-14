using NetYamlForge.Services.Tenant;

namespace NetYamlForge.Projects.UserHome.Models;

/// <summary>
/// 用户主页视图模型
/// </summary>
public class UserHomeViewModel
{
    public UserDetail User { get; set; } = new();
    public IReadOnlyList<ProjectInfo> Projects { get; set; } = new List<ProjectInfo>();
    public string? DefaultProject { get; set; }
    public List<string> RecentProjects { get; set; } = new();
    public List<QuickActionItem> QuickActions { get; set; } = new();
    public UserStatsViewModel Stats { get; set; } = new();
}

/// <summary>
/// 快速操作项
/// </summary>
public class QuickActionItem
{
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Url { get; set; } = "#";
    public string Description { get; set; } = "";
}

/// <summary>
/// 用户统计信息
/// </summary>
public class UserStatsViewModel
{
    public int TotalProjects { get; set; }
    public bool IsAdmin { get; set; }
    public string UserType { get; set; } = "";
    public DateTime? LastLoginAt { get; set; }
}
