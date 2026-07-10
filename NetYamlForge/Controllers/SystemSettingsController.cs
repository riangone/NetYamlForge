// ファイル概要：プロジェクトに紐づかない、アプリ全体のグローバル設定を管理する画面。
// 現状は AI CLI チェーン（Services/AI/CliChainOptions、appsettings.json の "AiCliChain" セクション）のみだが、
// 今後増えるグローバル設定（他システム連携のキーなど）もこのコントローラー配下にアクションを追加していく想定。

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.Config;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.Config;

namespace NetYamlForge.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("SystemSettings")]
public class SystemSettingsController : Controller
{
    private readonly IOptionsMonitor<CliChainOptions> _cliChainOptions;
    private readonly IAppSettingsWriter _writer;
    private readonly ICliModelCatalogService _modelCatalog;
    private readonly ILogger<SystemSettingsController> _logger;

    public SystemSettingsController(
        IOptionsMonitor<CliChainOptions> cliChainOptions,
        IAppSettingsWriter writer,
        ICliModelCatalogService modelCatalog,
        ILogger<SystemSettingsController> logger)
    {
        _cliChainOptions = cliChainOptions;
        _writer = writer;
        _modelCatalog = modelCatalog;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var vm = AiCliChainSettingsViewModel.FromOptions(_cliChainOptions.CurrentValue);
        return View(vm);
    }

    [HttpPost("AiCliChain")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAiCliChain(AiCliChainSettingsViewModel model)
    {
        var validRows = model.Providers
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Command))
            .ToList();

        if (validRows.Count == 0)
        {
            TempData["ErrorMessage"] = "少なくとも1つの CLI プロバイダー（名前・実行コマンド）を設定してください。";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var options = model.ToOptions();
            await _writer.UpdateSectionAsync(CliChainOptions.SectionName, options);
            _logger.LogWarning("管理者 {User} がシステム設定（AI CLI チェーン）を更新しました", User.Identity?.Name);
            TempData["SuccessMessage"] = "AI CLI 設定を保存しました。数秒以内に自動的に反映されます。";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI CLI 設定の保存に失敗しました");
            TempData["ErrorMessage"] = $"保存に失敗しました: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// 「既定モデル」ドロップダウンの選択肢を CLI 実機から取得する（antigravity/opencode は `{command} models` を
    /// 実行、claude はエイリアスの静的カタログを返す）。設定画面の「⟳ 最新の一覧を取得」ボタンから呼ばれる。
    /// </summary>
    [HttpGet("Models/{provider}")]
    public async Task<IActionResult> Models(string provider, CancellationToken cancellationToken)
    {
        var result = await _modelCatalog.GetModelsAsync(provider, cancellationToken);
        return Json(new
        {
            provider,
            success = result.Success,
            source = result.Source,
            models = result.Models,
            error = result.Error
        });
    }
}
