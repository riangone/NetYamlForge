using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.AI.Web.Controllers;

/// <summary>
/// AI チャット製品 メインページコントローラー
/// </summary>
[Authorize]
public class HomeController : Controller
{
    /// <summary>
    /// メインチャットページ
    /// </summary>
    public IActionResult Index() => View();

    /// <summary>
    /// AI ディベートページ
    /// </summary>
    public IActionResult Debate() => View();

    /// <summary>
    /// エラーページ（認証不要）
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
