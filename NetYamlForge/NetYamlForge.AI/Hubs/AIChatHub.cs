using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Services;

namespace NetYamlForge.AI.Hubs;

/// <summary>
/// AI チャット SignalR ハブ
/// </summary>
public class AIChatHub : Hub
{
    private readonly IConversationManager _conversationManager;
    private readonly IDirectAIProcessor _aiProcessor;
    private readonly IHandoverManager _handoverManager;
    private readonly ILogger<AIChatHub> _logger;

    public AIChatHub(
        IConversationManager conversationManager,
        IDirectAIProcessor aiProcessor,
        IHandoverManager handoverManager,
        ILogger<AIChatHub> logger)
    {
        _conversationManager = conversationManager;
        _aiProcessor = aiProcessor;
        _handoverManager = handoverManager;
        _logger = logger;
    }

    /// <summary>
    /// 接続確立
    /// </summary>
    public async Task Connect(string channelId)
    {
        _logger.LogInformation("クライアント接続：{ConnectionId}, Channel: {ChannelId}", Context.ConnectionId, channelId);

        await Clients.Caller.SendAsync("connected", new
        {
            connectionId = Context.ConnectionId,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// メッセージ受信
    /// </summary>
    public async Task SendMessage(string conversationId, string content)
    {
        try
        {
            // 入力中インジケーターを消去
            await Clients.Caller.SendAsync("typing_stop");

            // 顧客メッセージを保存
            await Clients.Caller.SendAsync("message_received", new
            {
                sender = "customer",
                content,
                timestamp = DateTime.UtcNow
            });

            // 入力中インジケーターを表示
            await Clients.Caller.SendAsync("typing_start");

            // 対話セッションの存在確認
            var conversation = await _conversationManager.GetConversationAsync(conversationId);
            if (conversation == null)
            {
                await Clients.Caller.SendAsync("error", new { message = "対話セッションが見つかりません" });
                return;
            }

            // 直接 AI 処理
            var context = new ConversationContext
            {
                ConversationId = conversationId,
                CurrentIntent = conversation.LastIntent
            };
            var aiResult = await _aiProcessor.ProcessAsync(content, context);

            // 入力中インジケーターを消去
            await Clients.Caller.SendAsync("typing_stop");

            // AI 応答を送信
            await Clients.Caller.SendAsync("ai_response", new
            {
                message = aiResult.Message,
                entities = aiResult.Entities,
                sentiment = aiResult.SentimentScore,
                quickReplies = aiResult.QuickReplies,
                suggestHandover = aiResult.NeedsHandover,
                handoverReason = aiResult.HandoverReason,
                priority = aiResult.Priority,
                aiModel = aiResult.AiModel,
                timestamp = DateTime.UtcNow
            });

            // エスカレーションが必要
            if (aiResult.NeedsHandover && !conversation.Status.Equals("escalated", StringComparison.OrdinalIgnoreCase))
            {
                var handoverResult = await _handoverManager.CreateHandoverAsync(new HandoverRequest
                {
                    ConversationId = conversationId,
                    Reason = aiResult.HandoverReason ?? "ai_unable",
                    Priority = aiResult.Priority,
                    TargetDepartment = aiResult.TargetDepartment,
                    HandoverNotes = $"感情：{aiResult.SentimentLabel} ({aiResult.SentimentScore:F2}), エンティティ：{string.Join(", ", aiResult.Entities.Select(e => $"{e.Key}={e.Value}"))}"
                }, null);

                if (handoverResult.Success)
                {
                    var handoverMessage = _handoverManager.GetHandoverMessage(aiResult.HandoverReason ?? "ai_unable");
                    await Clients.Caller.SendAsync("ai_response", new
                    {
                        message = handoverMessage,
                        suggestHandover = true,
                        handoverId = handoverResult.HandoverId,
                        estimatedWaitMinutes = handoverResult.EstimatedWaitMinutes,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メッセージ処理に失敗");
            await Clients.Caller.SendAsync("error", new { message = "メッセージ処理に失敗しました" });
        }
    }

    /// <summary>
    /// 入力開始
    /// </summary>
    public async Task TypingStart()
    {
        await Clients.Caller.SendAsync("typing_start");
    }

    /// <summary>
    /// 入力終了
    /// </summary>
    public async Task TypingStop()
    {
        await Clients.Caller.SendAsync("typing_stop");
    }

    /// <summary>
    /// クイック返信選択
    /// </summary>
    public async Task QuickReplySelected(string conversationId, string actionValue)
    {
        await SendMessage(conversationId, actionValue);
    }

    /// <summary>
    /// 接続切断
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("クライアント切断：{ConnectionId}", Context.ConnectionId);

        // 対話セッションをクリーンアップ
        // TODO: 接続情報から対話 ID を特定してクローズ処理

        await base.OnDisconnectedAsync(exception);
    }
}
