// ファイル概要: 言語切替要求を受け取りカルチャCookieを設定します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

public class LocalizationController : Controller
{
    [HttpPost]
    public IActionResult SetLanguage(string culture, string? returnUrl = null)
    {
        var value = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture));
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            value,
            new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "DynamicEntity");
    }
}
