// ファイル概要: auto-dealer-demo AI チャット REST API コントローラー。
// 顧客向けエンドポイント (AllowAnonymous) と
// オペレーター向けエンドポイント (Authorize) を提供します。

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers.Api;

[ApiController]
[Route("{project}/api/chat")]
[Produces("application/json")]
public class AutoDealerChatController : ControllerBase
{
    private readonly AutoDealerChatService _chat;
    private readonly ILogger<AutoDealerChatController> _logger;

    public AutoDealerChatController(AutoDealerChatService chat, ILogger<AutoDealerChatController> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────
    // 顧客向けエンドポイント（認証不要）
    // ─────────────────────────────────────────────────────

    /// <summary>新規チャットセッションを開始します。</summary>
    [AllowAnonymous]
    [HttpPost("session")]
    public async Task<IActionResult> StartSession([FromBody] ChatStartSessionRequest req)
    {
        try
        {
            var result = await _chat.StartSessionAsync(req.Channel ?? "web");
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "セッション開始エラー");
            return StatusCode(500, new { error = "セッションの開始に失敗しました。" });
        }
    }

    /// <summary>顧客メッセージを送信し AI 応答を取得します。</summary>
    [AllowAnonymous]
    [HttpPost("session/{conversationId}/message")]
    public async Task<IActionResult> SendMessage(string conversationId, [FromBody] ChatSendMessageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "メッセージが空です。" });

        try
        {
            var result = await _chat.SendMessageAsync(conversationId, req.Message);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージ処理エラー conv={Id}", conversationId);
            return StatusCode(500, new { error = "メッセージの処理に失敗しました。" });
        }
    }

    /// <summary>オペレーターの返信を顧客側がポーリングして取得します。</summary>
    [AllowAnonymous]
    [HttpGet("session/{conversationId}/updates")]
    public async Task<IActionResult> GetUpdates(string conversationId, [FromQuery] string? since)
    {
        DateTime? sinceDate = null;
        if (!string.IsNullOrWhiteSpace(since) && DateTime.TryParse(since, out var parsed))
            sinceDate = parsed;

        var messages = await _chat.GetUpdatesAsync(conversationId, sinceDate);
        return Ok(messages);
    }

    /// <summary>会話の評価を送信します。</summary>
    [AllowAnonymous]
    [HttpPost("session/{conversationId}/feedback")]
    public async Task<IActionResult> SubmitFeedback(string conversationId, [FromBody] ChatFeedbackRequest req)
    {
        if (req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { error = "評価は 1〜5 で入力してください。" });

        await _chat.SubmitFeedbackAsync(conversationId, req.Rating, req.Comment);
        return Ok(new { success = true });
    }

    // ─────────────────────────────────────────────────────
    // オペレーター向けエンドポイント（ログイン必須）
    // ─────────────────────────────────────────────────────

    /// <summary>オペレーターが顧客に返信します。</summary>
    [Authorize]
    [HttpPost("session/{conversationId}/operator-reply")]
    public async Task<IActionResult> OperatorReply(string conversationId, [FromBody] ChatOperatorReplyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "メッセージが空です。" });

        var operatorId = User.Identity?.Name ?? "unknown";
        await _chat.OperatorReplyAsync(conversationId, operatorId, req.Message);
        return Ok(new { success = true });
    }

    /// <summary>エスカレーションを引き受けます。</summary>
    [Authorize]
    [HttpPost("session/{conversationId}/accept")]
    public async Task<IActionResult> AcceptHandover(string conversationId, [FromBody] ChatAcceptHandoverRequest req)
    {
        var operatorId = User.Identity?.Name ?? "unknown";
        var accepted = await _chat.AcceptHandoverAsync(req.HandoverId, operatorId);
        if (!accepted)
            return Conflict(new { error = "このエスカレーションは既に他のオペレーターが担当しています。" });
        return Ok(new { success = true });
    }

    /// <summary>エスカレーションを解決済みにします。</summary>
    [Authorize]
    [HttpPost("session/{conversationId}/resolve")]
    public async Task<IActionResult> ResolveHandover(string conversationId, [FromBody] ChatResolveRequest req)
    {
        var operatorId = User.Identity?.Name ?? "unknown";
        await _chat.ResolveHandoverAsync(conversationId, operatorId, req.ResolutionNotes);
        return Ok(new { success = true });
    }

    /// <summary>エスカレーション詳細を取得します（オペレーター画面用）。</summary>
    [Authorize]
    [HttpGet("handover/{handoverId}")]
    public async Task<IActionResult> GetHandoverDetail(string handoverId)
    {
        var detail = await _chat.GetHandoverDetailAsync(handoverId);
        if (detail == null)
            return NotFound(new { error = "指定されたエスカレーションが見つかりません。" });
        return Ok(detail);
    }

    /// <summary>会話履歴を取得します（オペレーター画面用）。</summary>
    [Authorize]
    [HttpGet("session/{conversationId}/history")]
    public async Task<IActionResult> GetConversationHistory(string conversationId)
    {
        var history = await _chat.GetMessagesAsync(conversationId);
        return Ok(history);
    }
}

// ─────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────

public record ChatStartSessionRequest(string? Channel);
public record ChatSendMessageRequest(string Message);
public record ChatFeedbackRequest(int Rating, string? Comment);
public record ChatOperatorReplyRequest(string Message);
public record ChatAcceptHandoverRequest(string HandoverId);
public record ChatResolveRequest(string? ResolutionNotes);
