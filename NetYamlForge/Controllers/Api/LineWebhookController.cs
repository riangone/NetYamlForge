using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI;

namespace NetYamlForge.Controllers.Api;

/// <summary>
/// LINE Messaging API Webhook コントローラー
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

        // AI にメッセージを送信して応答を取得
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
            new LineTextMessage("こんにちは！自動車ディーラー AI アシスタントです。\n営業時間、予約、車両のお問い合わせなど、お気軽にお尋ねください！")
        });
    }

    private async Task HandleUnfollowEvent(LineEvent @event)
    {
        var userId = @event.Source.UserId;
        _logger.LogInformation("LINE 友達解除：{UserId}", userId);
        // ユーザーステータスを更新
    }

    private async Task HandlePostbackEvent(LineEvent @event)
    {
        var userId = @event.Source.UserId;
        var data = @event.Postback.Data;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(data))
            return;

        // クイック返信アクションを処理
        var aiResponse = await _lineService.GetAIResponseAsync(userId, data);
        await _lineService.ReplyMessageAsync(@event.ReplyToken, aiResponse);
    }
}

/// <summary>
/// LINE Webhook リクエスト
/// </summary>
public class LineWebhookRequest
{
    public List<LineEvent> Events { get; set; } = new();
}

/// <summary>
/// LINE イベント
/// </summary>
public class LineEvent
{
    public string Type { get; set; } = string.Empty;
    public string ReplyToken { get; set; } = string.Empty;
    public LineSource Source { get; set; } = new();
    public LineMessage? Message { get; set; }
    public LinePostback? Postback { get; set; }
}

/// <summary>
/// LINE ソース
/// </summary>
public class LineSource
{
    public string Type { get; set; } = string.Empty;
    public string? UserId { get; set; }
}

/// <summary>
/// LINE メッセージ
/// </summary>
public class LineMessage
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// LINE ポストバック
/// </summary>
public class LinePostback
{
    public string Data { get; set; } = string.Empty;
}

/// <summary>
/// LINE メッセージインターフェース
/// </summary>
public abstract class LineMessageBase
{
    public string Type { get; set; } = "text";
}

/// <summary>
/// LINE テキストメッセージ
/// </summary>
public class LineTextMessage : LineMessageBase
{
    public string Text { get; set; } = string.Empty;

    public LineTextMessage(string text)
    {
        Type = "text";
        Text = text;
    }
}

/// <summary>
/// LINE ボタンテンプレートメッセージ
/// </summary>
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

/// <summary>
/// LINE テンプレート
/// </summary>
public class LineTemplate
{
    public string Type { get; set; } = "buttons";
    public string? Text { get; set; }
    public List<LineAction> Actions { get; set; } = new();
}

/// <summary>
/// LINE アクション
/// </summary>
public class LineAction
{
    public string Type { get; set; } = "message";
    public string? Label { get; set; }
    public string? Text { get; set; }
    public string? Data { get; set; }
}

/// <summary>
/// LINE 応答
/// </summary>
public class LineAIResponse
{
    public List<LineMessageBase> Messages { get; set; } = new();
}

/// <summary>
/// LINE 設定
/// </summary>
public class LineConfig
{
    public bool Enabled { get; set; }
    public string ChannelAccessToken { get; set; } = string.Empty;
    public string ChannelSecret { get; set; } = string.Empty;
}

/// <summary>
/// LINE Messaging サービス
/// </summary>
public interface ILineMessagingService
{
    Task<LineAIResponse> GetAIResponseAsync(string userId, string message);
    Task ReplyMessageAsync(string replyToken, LineAIResponse response);
    Task PushMessageAsync(string userId, IEnumerable<LineMessageBase> messages);
}

/// <summary>
/// LINE Messaging サービス実装
/// </summary>
public class LineMessagingService : ILineMessagingService
{
    private readonly HttpClient _httpClient;
    private readonly LineConfig _config;
    private readonly IConversationManager _conversationManager;
    private readonly IIntentClassifier _intentClassifier;
    private readonly IResponseGenerator _responseGenerator;
    private readonly ILogger<LineMessagingService> _logger;

    // ユーザーごとの対話 ID マップ（本来は永続化が必要）
    private static readonly ConcurrentDictionary<string, string> UserConversationMap = new();

    public LineMessagingService(
        HttpClient httpClient,
        IOptions<LineConfig> configOptions,
        IConversationManager conversationManager,
        IIntentClassifier intentClassifier,
        IResponseGenerator responseGenerator,
        ILogger<LineMessagingService> logger)
    {
        _httpClient = httpClient;
        _config = configOptions.Value;
        _conversationManager = conversationManager;
        _intentClassifier = intentClassifier;
        _responseGenerator = responseGenerator;
        _logger = logger;
        
        _httpClient.BaseAddress = new Uri("https://api.line.me/v2/bot/");
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ChannelAccessToken);
    }

    public async Task<LineAIResponse> GetAIResponseAsync(string userId, string message)
    {
        try
        {
            // ユーザーの対話 ID を取得または作成
            var conversationId = await GetOrCreateConversationAsync(userId);

            // 意図分類
            var intentResult = await _intentClassifier.ClassifyAsync(message);

            // 応答生成
            var response = await _responseGenerator.GenerateResponseAsync(intentResult);

            // LINE 形式に変換
            return new LineAIResponse
            {
                Messages = new List<LineMessageBase>
                {
                    new LineTextMessage(response.Message)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE AI 応答生成に失敗");
            return new LineAIResponse
            {
                Messages = new List<LineMessageBase>
                {
                    new LineTextMessage("申し訳ございませんが、エラーが発生しました。")
                }
            };
        }
    }

    public async Task ReplyMessageAsync(string replyToken, LineAIResponse response)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { replyToken, messages = response.Messages }),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            await _httpClient.PostAsync("message/reply", content);
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
                System.Text.Encoding.UTF8,
                "application/json"
            );

            await _httpClient.PostAsync("message/push", content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE プッシュメッセージ送信に失敗");
        }
    }

    private async Task<string> GetOrCreateConversationAsync(string userId)
    {
        if (UserConversationMap.TryGetValue(userId, out var conversationId))
        {
            return conversationId;
        }

        // 新しい対話を開始
        var conversation = await _conversationManager.StartConversationAsync(
            new StartConversationRequest { Channel = "line" });

        UserConversationMap[userId] = conversation.ConversationId;
        return conversation.ConversationId;
    }
}
