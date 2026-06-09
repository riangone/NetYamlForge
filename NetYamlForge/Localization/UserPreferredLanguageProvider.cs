using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace NetYamlForge.Localization;

/// <summary>
/// 自定义本地化提供程序，如果客户端未设置语言 Cookie，则尝试根据当前登录用户的 lang Claim 决定语言。
/// </summary>
public class UserPreferredLanguageProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        if (httpContext == null)
        {
            throw new ArgumentNullException(nameof(httpContext));
        }

        // 1. 优先尝试从 Cookie 中获取（如果用户曾经手动切换过语言，以 Cookie 优先）
        var cookieName = CookieRequestCultureProvider.DefaultCookieName;
        if (httpContext.Request.Cookies.TryGetValue(cookieName, out var cookieValue) && 
            !string.IsNullOrWhiteSpace(cookieValue))
        {
            var result = CookieRequestCultureProvider.ParseCookieValue(cookieValue);
            if (result != null)
            {
                return Task.FromResult<ProviderCultureResult?>(result);
            }
        }

        // 2. 其次，如果用户已登录，从 Claim 中获取
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            // 尝试获取 Claim 中记录的 "lang"（首选语言）
            var langClaim = httpContext.User.FindFirst("lang");
            if (langClaim != null && !string.IsNullOrWhiteSpace(langClaim.Value))
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(langClaim.Value));
            }
        }

        return Task.FromResult<ProviderCultureResult?>(null);
    }
}
