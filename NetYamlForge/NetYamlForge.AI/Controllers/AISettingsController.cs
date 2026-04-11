using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services;

namespace NetYamlForge.AI.Controllers;

/// <summary>
/// AI プロバイダー設定管理コントローラー（管理者専用）
/// </summary>
[Authorize(Roles = "Admin")]
[Route("[controller]")]
public class AISettingsController : Controller
{
    private readonly CLIServiceFactory _cliFactory;
    private readonly IOptionsMonitor<CliConfig> _cliConfig;
    private readonly IConfiguration _config;
    private readonly AISettingsService _settingsService;

    public AISettingsController(
        CLIServiceFactory cliFactory,
        IOptionsMonitor<CliConfig> cliConfig,
        IConfiguration config,
        AISettingsService settingsService)
    {
        _cliFactory = cliFactory;
        _cliConfig = cliConfig;
        _config = config;
        _settingsService = settingsService;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var tools = await _cliFactory.GetAvailableToolsAsync();
        var windowPriority = _config.GetSection("AiWindow:ProviderPriority").Get<List<string>>()
            ?? ["claude", "qwen", "gemini", "ollama"];
        var model = new AISettingsViewModel
        {
            Tools = tools,
            DefaultTool = _cliConfig.CurrentValue.DefaultTool,
            WindowProviderPriority = windowPriority,
        };
        return View(model);
    }

    [HttpPost("SetDefaultTool")]
    [ValidateAntiForgeryToken]
    public IActionResult SetDefaultTool([FromForm] string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            TempData["Error"] = "ツール名が指定されていません";
            return RedirectToAction(nameof(Index));
        }
        _settingsService.SetDefaultTool(toolName);
        TempData["Success"] = $"デフォルト AI を「{toolName}」に変更しました。";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("SetWindowProvider")]
    [ValidateAntiForgeryToken]
    public IActionResult SetWindowProvider([FromForm] string primaryProvider)
    {
        if (string.IsNullOrWhiteSpace(primaryProvider))
        {
            TempData["Error"] = "プロバイダーが指定されていません";
            return RedirectToAction(nameof(Index));
        }

        // 既存の優先順序を取得し、選択されたプロバイダーを先頭に移動
        var currentPriority = _config.GetSection("AiWindow:ProviderPriority").Get<List<string>>()
            ?? ["claude", "qwen", "gemini", "ollama"];
        var newPriority = new List<string> { primaryProvider };
        newPriority.AddRange(currentPriority.Where(p => !p.Equals(primaryProvider, StringComparison.OrdinalIgnoreCase)));

        _settingsService.SetWindowProviderPriority(newPriority);
        TempData["Success"] = $"AI 窓口チャットのプライマリ AI を「{primaryProvider}」に設定しました。";
        return RedirectToAction(nameof(Index));
    }
}
