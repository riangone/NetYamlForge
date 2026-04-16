// AssistantHub プロジェクト カスタムアクションハンドラー
//
// 会話のアーカイブ、メッセージの重要マーク、一括削除などを実装します。
// YAML での参照例:
//   actions:
//     archive:
//       handler: assistant_hub_archive_conversation

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.AssistantHub.Hooks;

/// <summary>
/// 会話をアーカイブするアクションハンドラー。
/// YAML: handler = "assistant_hub_archive_conversation" (scope: row)
/// </summary>
public class ArchiveConversationHandler : ICustomActionHandler
{
    public string Name => "assistant_hub_archive_conversation";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("会話 ID が指定されていません。");
        if (!int.TryParse(ctx.RecordId, out var id))
            return ActionHandlerResult.Failure("無効な会話 ID です。");

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await db.ExecuteAsync(
            "UPDATE conversations SET is_archived = 1, updated_at = @now WHERE id = @id",
            new { now, id }, tx);

        return affected <= 0
            ? ActionHandlerResult.Failure("対象会話が見つかりません。")
            : ActionHandlerResult.Success();
    }
}

/// <summary>
/// アーカイブを解除するアクションハンドラー。
/// YAML: handler = "assistant_hub_unarchive_conversation" (scope: row)
/// </summary>
public class UnarchiveConversationHandler : ICustomActionHandler
{
    public string Name => "assistant_hub_unarchive_conversation";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("会話 ID が指定されていません。");
        if (!int.TryParse(ctx.RecordId, out var id))
            return ActionHandlerResult.Failure("無効な会話 ID です。");

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await db.ExecuteAsync(
            "UPDATE conversations SET is_archived = 0, updated_at = @now WHERE id = @id",
            new { now, id }, tx);

        return affected <= 0
            ? ActionHandlerResult.Failure("対象会話が見つかりません。")
            : ActionHandlerResult.Success();
    }
}

/// <summary>
/// 90日以上前のアーカイブを一括削除するハンドラー。
/// YAML: handler = "assistant_hub_bulk_delete_archived" (scope: header)
/// </summary>
public class BulkDeleteArchivedHandler : ICustomActionHandler
{
    public string Name => "assistant_hub_bulk_delete_archived";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var ninetyDaysAgo = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-dd HH:mm:ss");

        // 関連するメッセージを削除
        await db.ExecuteAsync(
            @"DELETE FROM messages
              WHERE conversation_id IN (
                  SELECT id FROM conversations
                  WHERE is_archived = 1 AND updated_at < @threshold
              )",
            new { threshold = ninetyDaysAgo }, tx);

        // 会話を削除
        await db.ExecuteAsync(
            "DELETE FROM conversations WHERE is_archived = 1 AND updated_at < @threshold",
            new { threshold = ninetyDaysAgo }, tx);

        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// メッセージを重要にマークするハンドラー。
/// YAML: handler = "assistant_hub_mark_important" (scope: row)
/// </summary>
public class MarkImportantHandler : ICustomActionHandler
{
    public string Name => "assistant_hub_mark_important";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("メッセージ ID が指定されていません。");
        if (!int.TryParse(ctx.RecordId, out var id))
            return ActionHandlerResult.Failure("無効なメッセージ ID です。");

        var affected = await db.ExecuteAsync(
            "UPDATE messages SET is_important = 1 WHERE id = @id",
            new { id }, tx);

        return affected <= 0
            ? ActionHandlerResult.Failure("対象メッセージが見つかりません。")
            : ActionHandlerResult.Success();
    }
}

/// <summary>
/// メッセージを削除するハンドラー。
/// YAML: handler = "assistant_hub_delete_message" (scope: row)
/// </summary>
public class DeleteMessageHandler : ICustomActionHandler
{
    public string Name => "assistant_hub_delete_message";

    public async Task<ActionHandlerResult> ExecuteAsync(
        CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("メッセージ ID が指定されていません。");
        if (!int.TryParse(ctx.RecordId, out var id))
            return ActionHandlerResult.Failure("無効なメッセージ ID です。");

        var affected = await db.ExecuteAsync(
            "DELETE FROM messages WHERE id = @id",
            new { id }, tx);

        return affected <= 0
            ? ActionHandlerResult.Failure("対象メッセージが見つかりません。")
            : ActionHandlerResult.Success();
    }
}
