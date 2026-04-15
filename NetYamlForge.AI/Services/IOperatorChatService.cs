using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

public interface IOperatorChatService
{
    Task<IEnumerable<OperatorHandoverDetail>> GetPendingHandoversAsync();
    Task<IEnumerable<ChatMessage>> GetMessagesAsync(string conversationId);
    Task<OperatorHandoverDetail?> GetHandoverByConversationAsync(string conversationId);
    Task OperatorReplyAsync(string conversationId, string operatorId, string message);
    Task<bool> AcceptHandoverAsync(string handoverId, string operatorId);
    Task ResolveHandoverAsync(string conversationId, string operatorId, string? resolutionNotes);
}
