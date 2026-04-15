using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Hubs;

/// <summary>
/// AI ディベートのリアルタイム通信ハブ。
/// クライアントはセッションIDごとのグループに参加し、メッセージを受信します。
/// </summary>
public class AIDebateHub : Hub
{
    private readonly ILogger<AIDebateHub> _logger;

    public AIDebateHub(ILogger<AIDebateHub> logger)
    {
        _logger = logger;
    }

    /// <summary>指定セッションのグループに参加します。</summary>
    public async Task JoinDebate(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"debate-{sessionId}");
        _logger.LogDebug("Client {ConnectionId} joined debate-{SessionId}", Context.ConnectionId, sessionId);
    }

    /// <summary>指定セッションのグループから退出します。</summary>
    public async Task LeaveDebate(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"debate-{sessionId}");
    }

    /// <summary>トピック ID でグループに参加します（AIDebateOrchestrator 対応）。</summary>
    public async Task JoinTopic(int topicId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"debate_{topicId}");
        _logger.LogDebug("Client {ConnectionId} joined debate_{TopicId}", Context.ConnectionId, topicId);
        await Clients.Caller.SendAsync("Connected", new { connectionId = Context.ConnectionId });
    }

    /// <summary>トピック ID のグループから退出します。</summary>
    public async Task LeaveTopic(int topicId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"debate_{topicId}");
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", new { connectionId = Context.ConnectionId });
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Client {ConnectionId} disconnected from AIDebateHub", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
