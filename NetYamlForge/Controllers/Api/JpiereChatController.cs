// ファイル概要: jpiere-cs AI チャット REST API コントローラー。
// JPiere 契約サービスの業務役割に特化した AI チャットエンドポイントを提供します。

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers.Api;

[ApiController]
[Route("jpiere-cs/api/ai/chat")]
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

    // ─────────────────────────────────────────────────────
    // 顧客・社員向けエンドポイント（認証必須）
    // ─────────────────────────────────────────────────────

    /// <summary>新規チャットセッションを開始します。</summary>
    [Authorize]
    [HttpPost("session")]
    public async Task<IActionResult> StartSession([FromBody] JpiereChatStartSessionRequest req)
    {
        try
        {
            // 認証済みユーザーの ID とロールを取得
            string? userId = User.Identity?.Name;
            string? userRole = User.Claims
                .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "employee";

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
    public async Task<IActionResult> SendMessage(string conversationId, [FromBody] JpiereChatSendMessageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "メッセージが空です。" });

        try
        {
            // ユーザーロールを取得
            string userRole = User.Claims
                .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "employee";

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

    /// <summary>会話の評価を送信します。</summary>
    [Authorize]
    [HttpPost("session/{conversationId}/feedback")]
    public async Task<IActionResult> SubmitFeedback(string conversationId, [FromBody] JpiereChatFeedbackRequest req)
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

// ─────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────

public record JpiereChatStartSessionRequest(string? Channel, string? GuestSessionId);
public record JpiereChatSendMessageRequest(string Message);
public record JpiereChatFeedbackRequest(int Rating, string? Comment);
