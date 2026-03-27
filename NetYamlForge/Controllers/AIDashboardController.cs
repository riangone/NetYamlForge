using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

/// <summary>
/// AI 窓口ダッシュボードコントローラー
/// </summary>
public class AIDashboardController : Controller
{
    private readonly ILogger<AIDashboardController> _logger;

    public AIDashboardController(ILogger<AIDashboardController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// AI ダッシュボードページ
    /// </summary>
    [HttpGet("/ai/dashboard")]
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// 対話詳細ページ
    /// </summary>
    [HttpGet("/ai/conversations/{id}")]
    public IActionResult ConversationDetail(string id)
    {
        ViewBag.ConversationId = id;
        return View();
    }

    /// <summary>
    /// エスカレーション対応ページ
    /// </summary>
    [HttpGet("/ai/handovers")]
    public IActionResult Handovers()
    {
        return View();
    }

    /// <summary>
    /// ナレッジベース管理ページ
    /// </summary>
    [HttpGet("/ai/knowledge")]
    public IActionResult Knowledge()
    {
        return View();
    }
}
