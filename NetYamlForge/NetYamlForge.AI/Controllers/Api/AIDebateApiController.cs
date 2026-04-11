using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services;
using System.Security.Claims;

namespace NetYamlForge.AI.Controllers.Api;

/// <summary>
/// AI ディベートの REST API コントローラー。
/// セッション管理・ディベート開始・メッセージ取得を提供します。
/// </summary>
[ApiController]
[Route("api/ai-debate")]
[Authorize]
public class AIDebateApiController : ControllerBase
{
    private readonly AIDebateService _debateService;
    private readonly ILogger<AIDebateApiController> _logger;

    public AIDebateApiController(AIDebateService debateService, ILogger<AIDebateApiController> logger)
    {
        _debateService = debateService;
        _logger = logger;
    }

    /// <summary>最近のディベートセッション一覧を取得します。</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int limit = 30)
    {
        var sessions = await _debateService.GetRecentSessionsAsync(limit);
        return Ok(sessions);
    }

    /// <summary>新しいディベートセッションを作成します。</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateDebateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Topic))
            return BadRequest(new { error = "Topic は必須です" });

        if (string.IsNullOrWhiteSpace(request.LeftProvider))
            request.LeftProvider = "claude";

        if (string.IsNullOrWhiteSpace(request.RightProvider))
            request.RightProvider = "qwen";

        if (string.IsNullOrWhiteSpace(request.LeftRole))
            request.LeftRole = "Advocate";

        if (string.IsNullOrWhiteSpace(request.RightRole))
            request.RightRole = "Critic";

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value
                  ?? User.Identity?.Name;

        var session = await _debateService.CreateSessionAsync(request, userId);
        _logger.LogInformation("ディベートセッション {Id} を作成しました (Topic={Topic})", session.Id, session.Topic);
        return Ok(session);
    }

    /// <summary>指定セッションの詳細を取得します。</summary>
    [HttpGet("sessions/{id:long}")]
    public async Task<IActionResult> GetSession(long id)
    {
        var session = await _debateService.GetSessionAsync(id);
        if (session == null) return NotFound();
        return Ok(session);
    }

    /// <summary>ディベートを開始します（バックグラウンド実行）。</summary>
    [HttpPost("sessions/{id:long}/start")]
    public async Task<IActionResult> StartDebate(long id)
    {
        var session = await _debateService.GetSessionAsync(id);
        if (session == null) return NotFound(new { error = "セッションが見つかりません" });
        if (session.Status != "pending")
            return BadRequest(new { error = $"セッションは既に {session.Status} 状態です" });

        await _debateService.StartDebateAsync(id);
        return Accepted(new { sessionId = id, message = "ディベートを開始しました" });
    }

    /// <summary>指定セッションのメッセージ一覧を取得します。</summary>
    [HttpGet("sessions/{id:long}/messages")]
    public async Task<IActionResult> GetMessages(long id)
    {
        var session = await _debateService.GetSessionAsync(id);
        if (session == null) return NotFound();
        var messages = await _debateService.GetMessagesAsync(id);
        return Ok(messages);
    }
}
