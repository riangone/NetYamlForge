// ファイル概要: オペレーター向けチャット管理 Razor ビューコントローラー。
// エスカレーション一覧・会話詳細・返信フォームを提供します。
// HTMX によるリアルタイムポーリングに対応します。

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers;

[Authorize]
[Route("{project}/OperatorChat")]
public class OperatorChatController : BaseProjectController
{
    private readonly IOperatorChatService _chat;
    private readonly ILogger<OperatorChatController> _logger;

    public OperatorChatController(IOperatorChatService chat, ILogger<OperatorChatController> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    /// <summary>エスカレーション一覧画面。</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string project)
    {
        var handovers = await _chat.GetPendingHandoversAsync();
        ViewBag.Project = project;
        return View(handovers);
    }

    /// <summary>会話詳細 + 返信フォーム。</summary>
    [HttpGet("Detail/{conversationId}")]
    public async Task<IActionResult> Detail(string project, string conversationId)
    {
        var messages = await _chat.GetMessagesAsync(conversationId);
        var handover = await _chat.GetHandoverByConversationAsync(conversationId);
        ViewBag.Project = project;
        ViewBag.ConversationId = conversationId;
        ViewBag.Handover = handover;
        return View(messages);
    }

    /// <summary>HTMX: メッセージ一覧部分更新。</summary>
    [HttpGet("Messages/{conversationId}")]
    public async Task<IActionResult> Messages(string project, string conversationId)
    {
        var messages = await _chat.GetMessagesAsync(conversationId);
        ViewBag.Project = project;
        ViewBag.ConversationId = conversationId;
        return PartialView("_Messages", messages);
    }

    /// <summary>返信送信（HTMX post）。</summary>
    [HttpPost("Reply/{conversationId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(string project, string conversationId, [FromForm] string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return BadRequest("メッセージが空です。");

        var operatorId = User.Identity?.Name ?? "unknown";
        await _chat.OperatorReplyAsync(conversationId, operatorId, message);

        // HTMX: 最新メッセージ一覧を返す
        var messages = await _chat.GetMessagesAsync(conversationId);
        ViewBag.Project = project;
        ViewBag.ConversationId = conversationId;
        return PartialView("_Messages", messages);
    }

    /// <summary>エスカレーションを引き受けます。</summary>
    [HttpPost("Accept/{handoverId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(string project, string handoverId)
    {
        var operatorId = User.Identity?.Name ?? "unknown";
        var accepted = await _chat.AcceptHandoverAsync(handoverId, operatorId);
        if (!accepted)
            TempData["Error"] = "このエスカレーションは既に他のオペレーターが担当しています。";

        return RedirectToAction(nameof(Index), new { project });
    }

    /// <summary>エスカレーションを解決します。</summary>
    [HttpPost("Resolve/{conversationId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(string project, string conversationId, [FromForm] string? resolutionNotes)
    {
        var operatorId = User.Identity?.Name ?? "unknown";
        await _chat.ResolveHandoverAsync(conversationId, operatorId, resolutionNotes);
        TempData["Success"] = "対応を完了しました。";
        return RedirectToAction(nameof(Index), new { project });
    }
}
