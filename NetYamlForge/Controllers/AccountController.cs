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
using NetYamlForge.Services.Tenant;

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
    private readonly ITenantUserService _tenantUsers;

    public AccountController(IUserAuthService users, ILogger<AccountController> logger, IAuditLogService audit, ProjectScope projectScope, ITenantUserService tenantUsers)
    {
        _users = users;
        _logger = logger;
        _audit = audit;
        _projectScope = projectScope;
        _tenantUsers = tenantUsers;
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

        // カスタムロール（AppUserRole）を取得してクレームに追加
        var customRolesList = (await _users.GetUserRolesAsync(user.UserName)).ToList();
        
        // プロジェクト固有のロールも取得（マルチテナント対応）
        var projectName = _projectScope.IsSet ? _projectScope.Current.Name : null;
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var projectRoles = await _tenantUsers.GetProjectRolesAsync(user.Id, projectName);
            foreach (var role in projectRoles)
            {
                if (!customRolesList.Contains(role, StringComparer.OrdinalIgnoreCase))
                {
                    customRolesList.Add(role);
                }
            }
        }
        
        var customRoles = customRolesList.AsReadOnly();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, user.DisplayName),
            new("lang", user.PreferredLanguage),
            new(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User")
        };

        // マルチテナント用の UserType を取得
        var userDetail = await _tenantUsers.GetUserDetailAsync(user.Id);
        if (userDetail != null)
        {
            claims.Add(new("user_type", userDetail.UserType));
        }

        // Admin/User 以外のカスタムロールを追加（ナビゲーションフィルタリングに使用）
        foreach (var role in customRoles)
        {
            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "User", StringComparison.OrdinalIgnoreCase))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

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

        _logger.LogInformation("User '{UserName}' signed in (roles: {Roles})", user.UserName, string.Join(", ", customRoles));
        await TryWriteAuditAsync("login", "account", "Sign in", user.UserName);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        // ロール別ランディングページへリダイレクト
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var landingByRole = _projectScope.Current.Layout?.LandingPageByRole;
            if (landingByRole != null && landingByRole.Count > 0 && customRoles.Count > 0)
            {
                foreach (var role in customRoles)
                {
                    if (landingByRole.TryGetValue(role, out var landingUrl) && !string.IsNullOrWhiteSpace(landingUrl))
                    {
                        _logger.LogInformation("User '{UserName}' redirected to role landing page: {Url}", user.UserName, landingUrl);
                        return Redirect(landingUrl);
                    }
                }
            }
            return RedirectToAction("Project", "Home", new { project = projectName });
        }

        // デフォルト：ユーザー個人主ページへリダイレクト（MyHome）
        return RedirectToAction("Index", "UserHome");
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

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        return View(new RegisterViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // ユーザー名チェック
        if (await _users.IsUserNameTakenAsync(model.UserName))
        {
            ModelState.AddModelError(string.Empty, "このユーザー名は既に使用されています。");
            return View(model);
        }

        try
        {
            await _users.RegisterAsync(model);
            
            // 自動ログイン
            var user = await _users.ValidateCredentialsAsync(model.UserName, model.Password);
            if (user != null)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.UserName),
                    new(ClaimTypes.GivenName, user.DisplayName),
                    new("lang", user.PreferredLanguage),
                    new(ClaimTypes.Role, "customer")
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                
                Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.PreferredLanguage)));

                _logger.LogInformation("New user registered: {UserName}", model.UserName);
                
                if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }
                
                var projectName = _projectScope.IsSet ? _projectScope.Current.Name : null;
                if (!string.IsNullOrWhiteSpace(projectName))
                {
                    return RedirectToAction("Project", "Home", new { project = projectName });
                }
                
                return RedirectToAction("Index", "Home");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for user: {UserName}", model.UserName);
            ModelState.AddModelError(string.Empty, "登録中にエラーが発生しました。");
        }

        return View(model);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult CustomerRegister(string? returnUrl = null, string? guestSessionId = null)
    {
        return View(new CustomerRegisterViewModel { ReturnUrl = returnUrl, GuestSessionId = guestSessionId });
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> CustomerRegister(CustomerRegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // ユーザー名チェック
        if (await _users.IsUserNameTakenAsync(model.UserName))
        {
            ModelState.AddModelError(string.Empty, "このユーザー名は既に使用されています。");
            return View(model);
        }

        try
        {
            await _users.RegisterCustomerAsync(model);
            
            // 自動ログイン
            var user = await _users.ValidateCredentialsAsync(model.UserName, model.Password);
            if (user != null)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.UserName),
                    new(ClaimTypes.GivenName, user.DisplayName),
                    new("lang", user.PreferredLanguage),
                    new(ClaimTypes.Role, "customer")
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                
                Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.PreferredLanguage)));

                _logger.LogInformation("New customer registered: {UserName}", model.UserName);
                
                // 顧客ダッシュボードへリダイレクト
                var projectName = _projectScope.IsSet ? _projectScope.Current.Name : null;
                if (!string.IsNullOrWhiteSpace(projectName))
                {
                    return RedirectToAction("Page", "Home", new 
                    { 
                        project = projectName,
                        pageName = "CustomerDashboard"
                    });
                }
                
                return RedirectToAction("Index", "Home");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Customer registration failed for user: {UserName}", model.UserName);
            ModelState.AddModelError(string.Empty, "登録中にエラーが発生しました。");
        }

        return View(model);
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
