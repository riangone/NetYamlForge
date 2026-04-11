// ファイル概要: auto-dealer-demo AI チャット REST API コントローラー。
// 顧客向けエンドポイント (AllowAnonymous) と
// オペレーター向けエンドポイント (Authorize) を提供します。

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services;
using NetYamlForge.AI.Services.Providers;

namespace NetYamlForge.AI.Controllers.Api;

[ApiController]
[Route("{project}/api/ai/chat")]
[Produces("application/json")]
public class AutoDealerChatController : ControllerBase
{
    private readonly AutoDealerChatService _chat;
    private readonly CLIServiceFactory _cliFactory;
    private readonly ILogger<AutoDealerChatController> _logger;
    private const int RequestTimeoutSeconds = 3600; // 最大リクエスト処理時間（60分）

    public AutoDealerChatController(AutoDealerChatService chat, CLIServiceFactory cliFactory, ILogger<AutoDealerChatController> logger)
    {
        _chat = chat;
        _cliFactory = cliFactory;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────
    // AI プロバイダー情報エンドポイント（認証不要）
    // ─────────────────────────────────────────────────────

    /// <summary>利用可能な AI プロバイダー一覧を返します。</summary>
    [AllowAnonymous]
    [HttpGet("providers")]
    public async Task<IActionResult> GetProviders()
    {
        try
        {
            var tools = await _cliFactory.GetAvailableToolsAsync();
            var result = tools.Select(kv => new
            {
                id = kv.Key,
                name = kv.Value.DisplayName ?? kv.Key,
                installed = kv.Value.Installed,
                authenticated = kv.Value.Authenticated,
                version = kv.Value.Version
            }).OrderByDescending(p => p.installed).ThenBy(p => p.id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロバイダー一覧取得エラー");
            return StatusCode(500, new { error = "プロバイダー情報の取得に失敗しました。" });
        }
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
            // 顧客 ID は認証済みユーザーのみ有効、ゲストは guestSessionId を使用
            string? customerId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                // ✅ 修正: ClaimTypes.Name からユーザー名を取得
                customerId = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value 
                             ?? User.Identity?.Name;
                _logger.LogInformation("認証済みユーザーのセッション開始: customerId={CustomerId}", customerId);
            }

            var result = await _chat.StartSessionAsync(
                req.Channel ?? "web",
                req.GuestSessionId,
                customerId);
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
            var task = _chat.SendMessageAsync(conversationId, req.Message, req.Provider);
            var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));

            if (completedTask != task)
            {
                _logger.LogWarning("メッセージ処理タイムアウト conv={Id} ({Timeout}秒)", conversationId, RequestTimeoutSeconds);
                return StatusCode(504, new { error = $"応答がタイムアウトしました（{RequestTimeoutSeconds}秒）。もう一度お試しください。" });
            }

            var result = await task;
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("メッセージ処理キャンセル conv={Id}", conversationId);
            return StatusCode(504, new { error = "リクエストがキャンセルされました。もう一度お試しください。" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージ処理エラー conv={Id}", conversationId);
            // 开发环境下返回详细错误信息，生产环境下返回通用消息
#if DEBUG
            return StatusCode(500, new { error = $"メッセージの処理に失敗しました: {ex.Message}", type = ex.GetType().Name, stackTrace = ex.StackTrace });
#else
            return StatusCode(500, new { error = "メッセージの処理に失敗しました。" });
#endif
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

    /// <summary>チャットセッションを終了します。</summary>
    [AllowAnonymous]
    [HttpPost("session/{conversationId}/close")]
    public async Task<IActionResult> CloseSession(string conversationId)
    {
        try
        {
            await _chat.CloseConversationAsync(conversationId);
            return Ok(new { message = "対話セッションを終了しました" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "セッション終了エラー conv={Id}", conversationId);
            return StatusCode(500, new { error = "セッションの終了に失敗しました。" });
        }
    }

    // ─────────────────────────────────────────────────────
    // 社員向けエンドポイント（ログイン必須）
    // ─────────────────────────────────────────────────────

    /// <summary>社員向けチャットセッションを開始します。</summary>
    [Authorize]
    [HttpPost("staff/session")]
    public async Task<IActionResult> StartStaffSession([FromBody] ChatStartSessionRequest req)
    {
        try
        {
            // デバッグログを追加
            _logger.LogInformation("社員セッション開始リクエストを受信しました。channel: {Channel}", req?.Channel ?? "staff");
            
            // StartSessionAsync は channel パラメータのみを受け取る
            var result = await _chat.StartSessionAsync("staff");
            
            _logger.LogInformation("社員セッションを開始しました。conversationId: {ConversationId}", result.ConversationId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            // 詳細なエラーログ（スタックトレース含む）
            _logger.LogError(ex, "社員セッション開始エラー。詳細: {Message}\nスタックトレース: {StackTrace}", ex.Message, ex.StackTrace);
            return StatusCode(500, new { error = "セッションの開始に失敗しました。", detail = ex.Message });
        }
    }

    /// <summary>ログイン社員の業務アシスタント会話一覧を取得します。</summary>
    [Authorize]
    [HttpGet("staff/conversations")]
    public async Task<IActionResult> GetStaffConversations([FromQuery] int limit = 50)
    {
        // TODO: 実装予定 - 現在は空のリストを返す
        // var userId = User.Identity?.Name;
        // if (string.IsNullOrEmpty(userId))
        //     return Unauthorized();
        // var conversations = await _chat.GetUserConversationsAsync(userId, "staff", limit);
        return Ok(new List<object>());
    }

    /// <summary>社員メッセージを送信し AI 応答を取得します。</summary>
    [Authorize]
    [HttpPost("staff/{conversationId}/message")]
    public async Task<IActionResult> SendStaffMessage(string conversationId, [FromBody] ChatSendMessageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "メッセージが空です。" });

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
            var task = _chat.SendStaffMessageAsync(conversationId, req.Message, req.Provider);
            var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));

            if (completedTask != task)
            {
                _logger.LogWarning("社員メッセージ処理タイムアウト conv={Id} ({Timeout}秒)", conversationId, RequestTimeoutSeconds);
                return StatusCode(504, new { error = $"応答がタイムアウトしました（{RequestTimeoutSeconds}秒）。もう一度お試しください。" });
            }

            var result = await task;
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("社員メッセージ処理キャンセル conv={Id}", conversationId);
            return StatusCode(504, new { error = "リクエストがキャンセルされました。もう一度お試しください。" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "社員メッセージ処理エラー conv={Id}", conversationId);
            return StatusCode(500, new { error = "メッセージの処理に失敗しました。" });
        }
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

    /// <summary>会話メッセージ一覧を取得します（顧客・社員向け、認証不要）。</summary>
    /// <remarks>
    /// conversationId は UUID 相当のランダム文字列のため、知っている人のみアクセス可能。
    /// チャットウィジェットがページリロード後に DB から履歴を復元するために使用します。
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("session/{conversationId}/messages")]
    public async Task<IActionResult> GetSessionMessages(string conversationId)
    {
        var messages = await _chat.GetMessagesAsync(conversationId);
        return Ok(messages);
    }

    /// <summary>ログイン済みユーザー自身の最近の会話IDを取得します（認証セッションベース）。</summary>
    /// <remarks>
    /// クッキー認証からユーザーを特定するため、どのブラウザ・端末からでも同じ会話を復元できます。
    /// </remarks>
    [Authorize]
    [HttpGet("my-session")]
    public async Task<IActionResult> GetMySession([FromQuery] string? mode = null)
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
            _logger.LogError(ex, "my-session 取得エラー userId={UserId}", userId);
            return StatusCode(500, new { error = "会話IDの取得に失敗しました。" });
        }
    }

    /// <summary>ユーザーの最近の会话IDを取得します（历史记录恢复用）。</summary>
    /// <remarks>
    /// ページリロード後に conversationId がlostされた場合、
    /// userId から最近のアクティブな会话を特定するために使用します。
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("user-history")]
    public async Task<IActionResult> GetUserRecentHistory([FromQuery] string userId, [FromQuery] int limit = 1)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId が必要です。" });

        try
        {
            var conversations = await _chat.GetUserRecentConversationsAsync(userId, limit);
            var mostRecent = conversations.FirstOrDefault();
            
            if (mostRecent == null)
                return Ok(new { conversationId = (string?)null, message = "会话が見つかりません。" });

            return Ok(new { conversationId = mostRecent.ConversationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ユーザー履歴取得エラー userId={UserId}", userId);
            return StatusCode(500, new { error = "履歴の取得に失敗しました。" });
        }
    }
}
