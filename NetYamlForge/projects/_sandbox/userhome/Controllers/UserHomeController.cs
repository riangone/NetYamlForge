using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services;
using NetYamlForge.Services.Tenant;
using NetYamlForge.Projects.UserHome.Models;

namespace NetYamlForge.Projects.UserHome.Controllers;

/// <summary>
/// 用户个人主页控制器
/// </summary>
[Authorize]
public class UserHomeController : Controller
{
    private readonly ITenantUserService _tenantUsers;
    private readonly IHomePageConfigProvider _homePageConfigProvider;
    private readonly ProjectManager _projectManager;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<UserHomeController> _logger;

    public UserHomeController(
        ITenantUserService tenantUsers,
        IHomePageConfigProvider homePageConfigProvider,
        ProjectManager projectManager,
        ProjectScope projectScope,
        ILogger<UserHomeController> logger)
    {
        _tenantUsers = tenantUsers;
        _homePageConfigProvider = homePageConfigProvider;
        _projectManager = projectManager;
        _projectScope = projectScope;
        _logger = logger;
    }

    /// <summary>
    /// 用户个人主页 - 登录后默认进入此页面
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var userDetail = await _tenantUsers.GetUserDetailAsync(userId);

        if (userDetail == null)
        {
            _logger.LogWarning("用户 ID={UserId} 的详细信息未找到", userId);
            return RedirectToAction("Login", "TenantAccount");
        }

        // 获取用户可访问的项目
        var projects = await _tenantUsers.GetAccessibleProjectsAsync(userId);

        // 获取默认项目配置
        var defaultProject = userDetail.DefaultProjectName ?? projects.FirstOrDefault()?.Name;
        var config = _homePageConfigProvider.GetConfig("userhome");

        // 确保 ProjectScope 设置为 userhome 项目（当通过属性路由访问时 middleware 可能未设置）
        if (!_projectScope.IsSet && _projectManager.TryGet("userhome", out var userhomeProject) && userhomeProject != null)
        {
            _projectScope.Set(userhomeProject);
        }

        var model = new UserHomeViewModel
        {
            User = userDetail,
            Projects = projects,
            DefaultProject = defaultProject,
            RecentProjects = GetRecentProjects(),
            QuickActions = BuildQuickActions(),
            Stats = await BuildUserStats(userId)
        };

        return View(model);
    }

    /// <summary>
    /// 切换到指定项目
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SwitchProject(string projectName, string? returnUrl = null)
    {
        var userId = GetUserId();
        var hasAccess = await _tenantUsers.HasProjectAccessAsync(userId, projectName);

        if (!hasAccess)
        {
            TempData["ErrorMessage"] = $"您没有权限访问项目 '{projectName}'";
            return RedirectToAction("Index");
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect(Url.Content($"~/{projectName}/Dashboard"));
    }

    /// <summary>
    /// 设置默认项目
    /// </summary>
    [HttpPost]
    public IActionResult SetDefaultProject(string projectName)
    {
        // TODO: 持久化用户的默认项目设置
        TempData["SuccessMessage"] = $"已将 '{projectName}' 设置为默认项目";
        return RedirectToAction("Index");
    }

    #region Private Helpers

    private int GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        throw new UnauthorizedAccessException("用户未登录");
    }

    private List<string> GetRecentProjects()
    {
        // TODO: 从数据库或缓存中获取用户最近访问的项目
        // 当前返回空列表，后续可以实现
        return new List<string>();
    }

    private List<QuickActionItem> BuildQuickActions()
    {
        return new List<QuickActionItem>
        {
            new QuickActionItem { Label = "我的任务", Icon = "task", Url = "#", Description = "查看分配给我的任务" },
            new QuickActionItem { Label = "最近文档", Icon = "file", Url = "#", Description = "查看我最近编辑的文档" },
            new QuickActionItem { Label = "消息通知", Icon = "bell", Url = "#", Description = "查看系统消息和通知" },
            new QuickActionItem { Label = "个人设置", Icon = "settings", Url = "/UserHome/Settings", Description = "修改个人信息和偏好设置" }
        };
    }

    private async Task<UserStatsViewModel> BuildUserStats(int userId)
    {
        var projects = await _tenantUsers.GetAccessibleProjectsAsync(userId);

        return new UserStatsViewModel
        {
            TotalProjects = projects.Count,
            IsAdmin = User.IsInRole("Admin"),
            UserType = User.FindFirst("user_type")?.Value ?? "unknown",
            LastLoginAt = DateTime.Now // TODO: 从用户记录中获取真实最后登录时间
        };
    }

    #endregion
}
