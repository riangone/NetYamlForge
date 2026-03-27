using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers.Api;

/// <summary>
/// LINE Bot テスト・デバッグ用コントローラー
/// </summary>
[ApiController]
[Route("api/line/test")]
[Produces("application/json")]
public class LineTestController : ControllerBase
{
    private readonly ILogger<LineTestController> _logger;

    public LineTestController(ILogger<LineTestController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// LINE Bot 設定確認
    /// </summary>
    [HttpGet("config")]
    public ActionResult GetConfig()
    {
        var config = new
        {
            enabled = true,
            webhookPath = "/api/line/webhook",
            validateSignature = true,
            quickReplies = new[]
            {
                "営業時間を聞く",
                "予約を申し込む",
                "車両のお問い合わせ",
                "担当者につなぐ"
            }
        };

        return Ok(config);
    }

    /// <summary>
    /// Webhook 接続テスト
    /// </summary>
    [HttpPost("webhook-test")]
    public async Task<ActionResult> TestWebhook([FromBody] WebhookTestRequest request)
    {
        try
        {
            _logger.LogInformation("Webhook テスト受信：{EventType}", request.EventType);

            return Ok(new
            {
                success = true,
                message = "Webhook 受信成功",
                timestamp = DateTime.UtcNow,
                receivedEvent = request.EventType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook テスト失敗");
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// メッセージ応答テスト
    /// </summary>
    [HttpPost("message-test")]
    public async Task<ActionResult> TestMessage([FromBody] MessageTestRequest request)
    {
        try
        {
            _logger.LogInformation("メッセージテスト：{Message}", request.Message);

            // TODO: AI エンジンと連携して実際の応答をテスト

            return Ok(new
            {
                success = true,
                input = request.Message,
                response = "テスト応答：この機能は開発中です",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージテスト失敗");
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// リッチメニューテスト
    /// </summary>
    [HttpGet("rich-menu")]
    public ActionResult GetRichMenu()
    {
        var richMenu = new
        {
            size = new { width = 2500, height = 1686 },
            selected = true,
            name = "Main Rich Menu",
            chatBarText = "メニュー",
            areas = new[]
            {
                new
                {
                    bounds = new { x = 0, y = 0, width = 833, height = 843 },
                    action = new { type = "message", text = "営業時間を教えてください" }
                },
                new
                {
                    bounds = new { x = 834, y = 0, width = 833, height = 843 },
                    action = new { type = "message", text = "予約を申し込みたいです" }
                },
                new
                {
                    bounds = new { x = 1667, y = 0, width = 833, height = 843 },
                    action = new { type = "message", text = "車両について教えてください" }
                }
            }
        };

        return Ok(richMenu);
    }

    /// <summary>
    /// 統計情報
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult> GetStats()
    {
        // TODO: 実際の統計データを DB から取得
        var stats = new
        {
            totalUsers = 1250,
            activeUsers = 342,
            totalMessages = 15420,
            aiResponseRate = 78.5,
            averageResponseTime = 1.8,
            handoverRate = 12.3,
            satisfactionScore = 4.2,
            lastUpdated = DateTime.UtcNow
        };

        return Ok(stats);
    }

    /// <summary>
    /// 接続状態確認
    /// </summary>
    [HttpGet("health")]
    public ActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            lineApiStatus = "connected",
            webhookStatus = "active",
            lastWebhookReceived = DateTime.UtcNow.AddMinutes(-5),
            timestamp = DateTime.UtcNow
        });
    }
}

public class WebhookTestRequest
{
    public string EventType { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Message { get; set; }
}

public class MessageTestRequest
{
    public string Message { get; set; } = string.Empty;
}
