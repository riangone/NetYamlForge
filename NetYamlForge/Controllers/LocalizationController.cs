// ファイル概要: 言語切替要求を受け取りカルチャCookieを設定します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Security.Claims;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.Tenant;

namespace NetYamlForge.Controllers;

public class LocalizationController : Controller
{
    private readonly ITenantUserService _tenantUserService;
    private readonly ILogger<LocalizationController> _logger;

    public LocalizationController(ITenantUserService tenantUserService, ILogger<LocalizationController> logger)
    {
        _tenantUserService = tenantUserService;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLanguage(string culture, string? returnUrl = null)
    {
        var value = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture));
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            value,
            new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax });

        // 如果用户已登录，将语言设置持久化到数据库中
        if (User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
            {
                try
                {
                    await _tenantUserService.UpdatePreferredLanguageAsync(userId, culture);
                    _logger.LogInformation("Successfully updated preferred language to '{Culture}' for user {UserId}", culture, userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update preferred language for user {UserId}", userId);
                }
            }
        }

        // 1. 如果 returnUrl 为空，尝试从 Referer 标头获取
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            returnUrl = Request.Headers["Referer"].ToString();
        }

        // 2. 尝试执行重定向
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            // 如果是绝对 URL，验证 Host 是否匹配当前请求 of Host，确保安全性
            if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) && 
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                if (string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(returnUrl);
                }
            }
            // 如果是相对的本地 URL
            else if (returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\"))
            {
                return LocalRedirect(returnUrl);
            }
        }

        return RedirectToAction("Index", "DynamicEntity");
    }
}
