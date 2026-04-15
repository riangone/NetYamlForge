using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// 対話管理インターフェース
/// </summary>
public interface IConversationManager
{
    /// <summary>
    /// 新しい対話セッションを開始
    /// </summary>
    Task<Conversation> StartConversationAsync(StartConversationRequest request, string? projectId = null);

    /// <summary>
    /// 対話セッションを取得
    /// </summary>
    Task<Conversation?> GetConversationAsync(string conversationId, string? projectId = null);

    /// <summary>
    /// 対話セッションを終了
    /// </summary>
    Task<bool> CloseConversationAsync(string conversationId, string? projectId = null);

    /// <summary>
    /// 対話セッションをエスカレーション状態に更新
    /// </summary>
    Task<bool> MarkConversationAsEscalatedAsync(string conversationId, string? projectId = null);

    /// <summary>
    /// 顧客を対話に紐付け
    /// </summary>
    Task<bool> LinkCustomerToConversationAsync(string conversationId, string customerId, string? projectId = null);

    /// <summary>
    /// コンテキストデータを更新
    /// </summary>
    Task<bool> UpdateContextAsync(string conversationId, Dictionary<string, object> context, string? projectId = null);

    /// <summary>
    /// 有効期限切れの対話セッションをクリーンアップ
    /// </summary>
    Task<int> CleanupExpiredSessionsAsync(int timeoutMinutes = 60, string? projectId = null);
}
