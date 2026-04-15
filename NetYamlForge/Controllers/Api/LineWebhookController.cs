using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace NetYamlForge.Controllers.Api;

/// <summary>
/// LINE Messaging API Webhook コントローラー
/// AI 処理は NetYamlForge.AI.Web (standalone) が担当します。
/// このコントローラーは Webhook の受信と AI サービスへの転送のみを行います。
/// </summary>
[ApiController]
[Route("api/line")]
public class LineWebhookController : ControllerBase
{
    private readonly ILineMessagingService _lineService;
    private readonly ILogger<LineWebhookController> _logger;
    private readonly LineConfig _config;

    public LineWebhookController(
        ILineMessagingService lineService,
        IOptions<LineConfig> configOptions,
        ILogger<LineWebhookController> logger)
    {
        _lineService = lineService;
        _config = configOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// LINE Webhook 受信
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook([FromBody] LineWebhookRequest request)
    {
        try
        {
            // チャネルシークレット検証
            if (!await ValidateChannelSecret())
            {
                return Unauthorized();
            }

            foreach (var @event in request.Events)
            {
                await HandleEvent(@event);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE Webhook 処理に失敗");
            return StatusCode(500, "Internal Server Error");
        }
    }

    /// <summary>
    /// LINE 認証用エンドポイント
    /// </summary>
    [HttpGet("verify")]
    public IActionResult Verify()
    {
        return Ok("verified");
    }

    private async Task<bool> ValidateChannelSecret()
    {
        // X-Line-Signature ヘッダーを検証
        if (!Request.Headers.TryGetValue("X-Line-Signature", out var signature))
        {
            return false;
        }

        var body = await new StreamReader(Request.Body).ReadToEndAsync();
        var expectedSignature = Convert.ToBase64String(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(_config.ChannelSecret),
                System.Text.Encoding.UTF8.GetBytes(body)
            )
        );

        return signature == expectedSignature;
    }

    private async Task HandleEvent(LineEvent @event)
    {
        switch (@event.Type)
        {
            case "message":
                await HandleMessageEvent(@event);
                break;
            case "follow":
                await HandleFollowEvent(@event);
                break;
            case "unfollow":
                await HandleUnfollowEvent(@event);
                break;
            case "postback":
                await HandlePostbackEvent(@event);
                break;
        }
    }

    private async Task HandleMessageEvent(LineEvent @event)
    {
        var userId = @event.Source.UserId;
        var message = @event.Message;

        if (message == null || string.IsNullOrEmpty(userId))
            return;

        // AI サービス（NetYamlForge.AI.Web）に転送して応答を取得
        var aiResponse = await _lineService.GetAIResponseAsync(userId, message.Text);

        // LINE に返信
        await _lineService.ReplyMessageAsync(@event.ReplyToken, aiResponse);
    }

    private async Task HandleFollowEvent(LineEvent @event)
    {
        var userId = @event.Source.UserId;
        if (string.IsNullOrEmpty(userId))
            return;

        // 友達追加歓迎メッセージ
        await _lineService.PushMessageAsync(userId, new[]
        {
            new LineTextMessage("こんにちは！AI アシスタントです。\nお気軽にメッセージをどうぞ！")
        });
    }

    private async Task HandleUnfollowEvent(LineEvent @event)
    {
        var userId = @event.Source.UserId;
        _logger.LogInformation("LINE 友達解除：{UserId}", userId);
    }

    private async Task HandlePostbackEvent(LineEvent @event)
    {
        var userId = @event.Source.UserId;
        var data = @event.Postback.Data;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(data))
            return;

        var aiResponse = await _lineService.GetAIResponseAsync(userId, data);
        await _lineService.ReplyMessageAsync(@event.ReplyToken, aiResponse);
    }
}

/// <summary>LINE Webhook リクエスト</summary>
public class LineWebhookRequest
{
    public List<LineEvent> Events { get; set; } = new();
}

/// <summary>LINE イベント</summary>
public class LineEvent
{
    public string Type { get; set; } = string.Empty;
    public string ReplyToken { get; set; } = string.Empty;
    public LineSource Source { get; set; } = new();
    public LineMessage? Message { get; set; }
    public LinePostback? Postback { get; set; }
}

/// <summary>LINE ソース</summary>
public class LineSource
{
    public string Type { get; set; } = string.Empty;
    public string? UserId { get; set; }
}

/// <summary>LINE メッセージ</summary>
public class LineMessage
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>LINE ポストバック</summary>
public class LinePostback
{
    public string Data { get; set; } = string.Empty;
}

/// <summary>LINE メッセージ基底</summary>
public abstract class LineMessageBase
{
    public string Type { get; set; } = "text";
}

/// <summary>LINE テキストメッセージ</summary>
public class LineTextMessage : LineMessageBase
{
    public string Text { get; set; } = string.Empty;

    public LineTextMessage(string text)
    {
        Type = "text";
        Text = text;
    }
}

/// <summary>LINE ボタンテンプレートメッセージ</summary>
public class LineTemplateMessage : LineMessageBase
{
    public LineTemplate Template { get; set; } = new();

    public LineTemplateMessage(string text, List<LineAction> actions)
    {
        Type = "template";
        Template = new LineTemplate
        {
            Type = "buttons",
            Text = text,
            Actions = actions
        };
    }
}

/// <summary>LINE テンプレート</summary>
public class LineTemplate
{
    public string Type { get; set; } = "buttons";
    public string? Text { get; set; }
    public List<LineAction> Actions { get; set; } = new();
}

/// <summary>LINE アクション</summary>
public class LineAction
{
    public string Type { get; set; } = "message";
    public string? Label { get; set; }
    public string? Text { get; set; }
    public string? Data { get; set; }
}

/// <summary>LINE AI 応答</summary>
public class LineAIResponse
{
    public List<LineMessageBase> Messages { get; set; } = new();
}

/// <summary>LINE 設定</summary>
public class LineConfig
{
    public bool Enabled { get; set; }
    public string ChannelAccessToken { get; set; } = string.Empty;
    public string ChannelSecret { get; set; } = string.Empty;
    /// <summary>AI サービス (NetYamlForge.AI.Web) のベース URL</summary>
    public string AIServiceBaseUrl { get; set; } = "http://localhost:5200";
}

/// <summary>LINE Messaging サービスインターフェース</summary>
public interface ILineMessagingService
{
    Task<LineAIResponse> GetAIResponseAsync(string userId, string message);
    Task ReplyMessageAsync(string replyToken, LineAIResponse response);
    Task PushMessageAsync(string userId, IEnumerable<LineMessageBase> messages);
}

/// <summary>
/// LINE Messaging サービス実装
/// AI 処理は NetYamlForge.AI.Web へ HTTP 転送します。
/// </summary>
public class LineMessagingService : ILineMessagingService
{
    private readonly HttpClient _lineHttpClient;
    private readonly HttpClient _aiHttpClient;
    private readonly LineConfig _config;
    private readonly ILogger<LineMessagingService> _logger;

    // ユーザーごとのセッション ID マップ（本来は永続化が必要）
    private static readonly ConcurrentDictionary<string, string> UserSessionMap = new();

    public LineMessagingService(
        IHttpClientFactory httpClientFactory,
        IOptions<LineConfig> configOptions,
        ILogger<LineMessagingService> logger)
    {
        _config = configOptions.Value;
        _logger = logger;

        _lineHttpClient = httpClientFactory.CreateClient("LineApi");
        _lineHttpClient.BaseAddress = new Uri("https://api.line.me/v2/bot/");
        _lineHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _config.ChannelAccessToken);

        _aiHttpClient = httpClientFactory.CreateClient("AIService");
        _aiHttpClient.BaseAddress = new Uri(_config.AIServiceBaseUrl.TrimEnd('/') + "/");
        _aiHttpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<LineAIResponse> GetAIResponseAsync(string userId, string message)
    {
        try
        {
            // NetYamlForge.AI.Web の API エンドポイントに転送
            var sessionId = UserSessionMap.GetOrAdd(userId, _ => Guid.NewGuid().ToString());
            var payload = JsonSerializer.Serialize(new
            {
                sessionId,
                userId,
                message,
                channel = "line"
            });

            var response = await _aiHttpClient.PostAsync(
                "api/ai/chat",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(json);
                var replyText = result.TryGetProperty("message", out var msgProp)
                    ? msgProp.GetString() ?? "応答を受信しました。"
                    : "応答を受信しました。";

                return new LineAIResponse
                {
                    Messages = new List<LineMessageBase> { new LineTextMessage(replyText) }
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI サービスへの転送に失敗しました");
        }

        return new LineAIResponse
        {
            Messages = new List<LineMessageBase>
            {
                new LineTextMessage("現在 AI サービスに接続できません。しばらく後にお試しください。")
            }
        };
    }

    public async Task ReplyMessageAsync(string replyToken, LineAIResponse response)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { replyToken, messages = response.Messages }),
                Encoding.UTF8,
                "application/json"
            );
            await _lineHttpClient.PostAsync("message/reply", content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE メッセージ返信に失敗");
        }
    }

    public async Task PushMessageAsync(string userId, IEnumerable<LineMessageBase> messages)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { to = userId, messages }),
                Encoding.UTF8,
                "application/json"
            );
            await _lineHttpClient.PostAsync("message/push", content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE プッシュメッセージ送信に失敗");
        }
    }
}
