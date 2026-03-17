// ファイル概要: ログイン・ログアウト・アクセス拒否画面を制御します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Security.Claims;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

// ルートなしアクセス（/Account/Login）とプロジェクト付きアクセス（/chinook/Account/Login）の両方を受け付けます。
[Route("Account/{action}")]
[Route("{project}/Account/{action}")]
public class AccountController : Controller
{
    private readonly IUserAuthService _users;
    private readonly ILogger<AccountController> _logger;
    private readonly IAuditLogService _audit;
    private readonly ProjectScope _projectScope;

    public AccountController(IUserAuthService users, ILogger<AccountController> logger, IAuditLogService audit, ProjectScope projectScope)
    {
        _users = users;
        _logger = logger;
        _audit = audit;
        _projectScope = projectScope;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _users.ValidateCredentialsAsync(model.UserName, model.Password);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, user.DisplayName),
            new("lang", user.PreferredLanguage),
            new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        try
        {
            await _users.UpdateLastLoginAsync(user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update LastLoginAt for user '{UserName}'", user.UserName);
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.PreferredLanguage)));

        _logger.LogInformation("User '{UserName}' signed in", user.UserName);
        await TryWriteAuditAsync("login", "account", "Sign in", user.UserName);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        // プロジェクトスコープが設定されている場合はプロジェクトホームへ
        var projectName = _projectScope.IsSet ? _projectScope.Current.Name : null;
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            if (string.Equals(projectName, "salesforce-crm", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Page", new { project = projectName, pageName = "OperationWorkbench" });
            }
            return RedirectToAction("Project", "Home", new { project = projectName });
        }

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        var userName = User.Identity?.Name ?? "anonymous";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("User '{UserName}' signed out", userName);
        await TryWriteAuditAsync("logout", "account", "Sign out", userName);

        var projectName = _projectScope.IsSet ? _projectScope.Current.Name : null;
        return RedirectToAction(nameof(Login), new { project = projectName });
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private async Task TryWriteAuditAsync(string action, string? entity, string? detail, string? userName)
    {
        try
        {
            await _audit.WriteAsync(action, entity, detail, userName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit write failed for action={Action}, user={UserName}", action, userName);
        }
    }
}
