using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services.Tenant;

namespace NetYamlForge.Controllers;

/// <summary>
/// 多租户认证控制器
/// </summary>
public class TenantAccountController : Controller
{
    private readonly ITenantUserService _tenantUsers;
    private readonly ILogger<TenantAccountController> _logger;

    public TenantAccountController(
        ITenantUserService tenantUsers,
        ILogger<TenantAccountController> logger)
    {
        _tenantUsers = tenantUsers;
        _logger = logger;
    }

    /// <summary>
    /// 登录页面
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    /// <summary>
    /// 登录提交
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // 验证用户凭据
        var user = await _tenantUsers.ValidateCredentialsAsync(model.UserName, model.Password);
        
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "用户名或密码错误");
            _logger.LogInformation("登录失败：用户名或密码错误 - {UserName}", model.UserName);
            return View(model);
        }

        // 获取用户可访问的项目
        var projects = await _tenantUsers.GetAccessibleProjectsAsync(user.Id);
        
        if (projects.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "该用户未分配任何项目，请联系管理员");
            _logger.LogWarning("用户 {UserName} 未分配任何项目", user.UserName);
            return View(model);
        }

        // 创建 Claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, user.DisplayName),
            new("user_type", user.UserType),
        };

        // 添加 Admin 角色 Claim（admin 用户或 employee 类型用户）
        if (user.UserName == "admin" || user.UserType == "employee")
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        // 添加项目角色 Claims
        foreach (var project in projects)
        {
            claims.Add(new Claim("projects", project.Name));
        }

        // 设置默认项目
        var defaultProject = user.DefaultProjectName ?? projects.First().Name;
        claims.Add(new Claim("default_project", defaultProject));

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        // 登录
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal);

        _logger.LogInformation("用户 {UserName} 登录成功", user.UserName);

        // 重定向
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        // 重定向到用户个人主页
        return RedirectToAction("Index", "UserHome");
    }

    /// <summary>
    /// 登出
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("用户登出");
        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// 项目选择器页面
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> SelectProject(string? returnUrl = null)
    {
        var userId = GetUserId();
        var projects = await _tenantUsers.GetAccessibleProjectsAsync(userId);

        var model = new SelectProjectViewModel
        {
            Projects = projects,
            ReturnUrl = returnUrl
        };

        return View(model);
    }

    /// <summary>
    /// 访问拒绝页面
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied(string? project = null)
    {
        var model = new AccessDeniedViewModel
        {
            ProjectName = project,
            Message = string.IsNullOrEmpty(project)
                ? "您没有权限访问该资源"
                : $"您没有权限访问项目 '{project}'"
        };

        return View(model);
    }

    /// <summary>
    /// 注册页面（可选：仅允许管理员或通过邀请链接注册）
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register(string? project = null, string? token = null)
    {
        ViewData["ProjectName"] = project;
        ViewData["Token"] = token;
        return View(new RegisterViewModel());
    }

    /// <summary>
    /// 注册提交
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? project = null, string? token = null)
    {
        if (!ModelState.IsValid)
        {
            ViewData["ProjectName"] = project;
            return View(model);
        }

        try
        {
            // 创建用户
            var request = new CreateUserRequest
            {
                UserName = model.UserName,
                Password = model.Password,
                DisplayName = model.DisplayName,
                Email = model.Email,
                Phone = model.Phone,
                UserType = "customer", // 默认注册为客户
                DefaultProjectName = project,
                ProjectRole = "customer",
                CreatedByUserId = 0 // 0 表示系统自动创建
            };

            var userId = await _tenantUsers.CreateUserWithProjectRoleAsync(request);

            _logger.LogInformation("新用户注册：{UserName} (ID={UserId})", model.UserName, userId);

            // 重定向到登录页面
            TempData["SuccessMessage"] = "注册成功，请登录";
            return RedirectToAction(nameof(Login));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户注册失败：{UserName}", model.UserName);
            ModelState.AddModelError(string.Empty, "注册失败：" + ex.Message);
            ViewData["ProjectName"] = project;
            return View(model);
        }
    }

    #region Helper Methods

    private int GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        throw new UnauthorizedAccessException("用户未登录");
    }

    #endregion
}
