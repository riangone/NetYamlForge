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
