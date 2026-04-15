using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NetYamlForge.AI.Web.Controllers;

/// <summary>
/// 認証コントローラー（NetYamlForge Identity から独立した設定ベース認証）
/// </summary>
public class AccountController : Controller
{
    private readonly IConfiguration _config;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IConfiguration config, ILogger<AccountController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(returnUrl ?? "/");

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ViewBag.Error = "ユーザー名とパスワードを入力してください";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        var users = _config.GetSection("Auth:Users").Get<List<AuthUserConfig>>() ?? [];
        var user = users.FirstOrDefault(u =>
            u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase) &&
            u.Password == password);

        if (user is null)
        {
            _logger.LogWarning("Failed login attempt for user: {Username} from {IP}",
                username, HttpContext.Connection.RemoteIpAddress);
            ViewBag.Error = "ユーザー名またはパスワードが正しくありません";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Username),
            new(ClaimTypes.Name,           user.DisplayName ?? user.Username),
        };

        foreach (var role in user.Roles ?? [])
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc   = DateTimeOffset.UtcNow.AddDays(7)
            });

        _logger.LogInformation("User {Username} logged in successfully", user.Username);
        return LocalRedirect(returnUrl is { Length: > 0 } r ? r : "/");
    }

    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        var userName = User.Identity?.Name;
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("User {Username} logged out", userName);
        return RedirectToAction(nameof(Login));
    }
}

/// <summary>
/// appsettings.json の Auth:Users エントリ
/// </summary>
public sealed class AuthUserConfig
{
    public string       Username    { get; init; } = "";
    public string       Password    { get; init; } = "";
    public string?      DisplayName { get; init; }
    public List<string>? Roles      { get; init; }
}
