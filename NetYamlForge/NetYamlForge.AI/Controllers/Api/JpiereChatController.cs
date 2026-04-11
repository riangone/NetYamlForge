// ファイル概要: jpiere-cs AI チャット REST API コントローラー。
// JPiere 契約サービスの業務役割に特化した AI チャットエンドポイントを提供します。

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services;

namespace NetYamlForge.AI.Controllers.Api;

[ApiController]
[Route("jpiere-cs/api/ai/chat")]
[Route("{project:regex(^jpiere-cs$)}/api/ai/chat")]
[Produces("application/json")]
public class JpiereChatController : ControllerBase
{
    private readonly JpiereChatService _chat;
    private readonly ILogger<JpiereChatController> _logger;

    public JpiereChatController(JpiereChatService chat, ILogger<JpiereChatController> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    /// <summary>認証済みユーザーのロールを取得します。</summary>
    private string GetUserRole() =>
        User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "employee";

    /// <summary>認証済みユーザーのIDを取得します。</summary>
    private string? GetUserId() =>
        User.Identity?.Name;

    // ─────────────────────────────────────────────────────
    // 顧客・社員向けエンドポイント（認証必須）
    // ─────────────────────────────────────────────────────

    /// <summary>新規チャットセッションを開始します。</summary>
    [Authorize]
    [HttpPost("session")]
    public async Task<IActionResult> StartSession([FromBody] ChatStartSessionRequest req)
    {
        try
        {
            string? userId = GetUserId();
            string userRole = GetUserRole();

            var result = await _chat.StartSessionAsync(
                req.Channel ?? "web",
                req.GuestSessionId,
                userId,
                userRole);

            _logger.LogInformation("[JpiereChat] セッション開始：userId={UserId}, role={Role}, convId={ConvId}",
                userId, userRole, result.ConversationId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JPiere セッション開始エラー");
            return StatusCode(500, new { error = "セッションの開始に失敗しました。" });
        }
    }

    /// <summary>ユーザーーメッセージを送信し AI 応答を取得します。</summary>
    [Authorize]
    [HttpPost("session/{conversationId}/message")]
    public async Task<IActionResult> SendMessage(string conversationId, [FromBody] ChatSendMessageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "メッセージが空です。" });

        try
        {
            string userRole = GetUserRole();
            var result = await _chat.SendMessageAsync(conversationId, req.Message, userRole);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JPiere メッセージ処理エラー conv={Id}", conversationId);
            return StatusCode(500, new { error = "メッセージの処理に失敗しました。" });
        }
    }

    /// <summary>会話メッセージ一覧を取得します。</summary>
    [Authorize]
    [HttpGet("session/{conversationId}/messages")]
    public async Task<IActionResult> GetSessionMessages(string conversationId)
    {
        try
        {
            var messages = await _chat.GetMessagesAsync(conversationId);
            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JPiere メッセージ取得エラー conv={Id}", conversationId);
            return StatusCode(500, new { error = "メッセージの取得に失敗しました。" });
        }
    }

    /// <summary>ログイン済みユーザー自身の最近の会話IDを取得します（認証セッションベース）。</summary>
    [Authorize]
    [HttpGet("my-session")]
    public async Task<IActionResult> GetMySession()
    {
        var userId = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var conversations = await _chat.GetUserRecentConversationsAsync(userId, 1);
            var mostRecent = conversations.FirstOrDefault();
            return Ok(new { conversationId = mostRecent?.ConversationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JPiere my-session 取得エラー userId={UserId}", userId);
            return StatusCode(500, new { error = "会話IDの取得に失敗しました。" });
        }
    }

    /// <summary>会話の評価を送信します。</summary>
    [Authorize]
    [HttpPost("session/{conversationId}/feedback")]
    public async Task<IActionResult> SubmitFeedback(string conversationId, [FromBody] ChatFeedbackRequest req)
    {
        if (req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { error = "評価は 1〜5 で入力してください。" });

        try
        {
            await _chat.SubmitFeedbackAsync(conversationId, req.Rating, req.Comment);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JPiere フィードバック送信エラー conv={Id}", conversationId);
            return StatusCode(500, new { error = "フィードバックの送信に失敗しました。" });
        }
    }
}
