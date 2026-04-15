using Microsoft.AspNetCore.SignalR;

namespace NetYamlForge.AI.Hubs;

/// <summary>
/// AI 进度 SignalR Hub
/// </summary>
public class AIProgressHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", new
        {
            ConnectionId = Context.ConnectionId,
            Timestamp = DateTime.UtcNow
        });
        await base.OnConnectedAsync();
    }
    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
