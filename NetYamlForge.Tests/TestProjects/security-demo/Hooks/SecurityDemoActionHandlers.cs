// ファイル概要: security-demo プロジェクトのカスタムアクションハンドラー実装
// （publish / unpublish / archive）。blog の同名ハンドラーと同じパターンを踏襲。
// すべて Dapper のパラメータ化クエリのみを使用しており、文字列連結による
// SQL 組み立ては一切行っていない（悪い例は ../docs/unsafe-examples/ 参照）。

using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;
using Dapper;

namespace NetYamlForge.Projects.SecurityDemo.Hooks;

public sealed class PublishNoteHandler : ICustomActionHandler
{
    private readonly ILogger<PublishNoteHandler> _logger;
    public string Name => "publish_note";

    public PublishNoteHandler(ILogger<PublishNoteHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (int.TryParse(ctx.RecordId, out int id))
        {
            await db.ExecuteAsync(
                "UPDATE Note SET Status = 'published', PublishedAt = @Now WHERE Id = @Id",
                new { Id = id, Now = DateTime.UtcNow }, tx);
            _logger.LogInformation("Published note {Id}", id);
            return ActionHandlerResult.Success();
        }
        return ActionHandlerResult.Failure("Invalid RecordId");
    }
}

public sealed class UnpublishNoteHandler : ICustomActionHandler
{
    private readonly ILogger<UnpublishNoteHandler> _logger;
    public string Name => "unpublish_note";

    public UnpublishNoteHandler(ILogger<UnpublishNoteHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (int.TryParse(ctx.RecordId, out int id))
        {
            await db.ExecuteAsync(
                "UPDATE Note SET Status = 'draft', PublishedAt = NULL WHERE Id = @Id",
                new { Id = id }, tx);
            _logger.LogInformation("Unpublished note {Id}", id);
            return ActionHandlerResult.Success();
        }
        return ActionHandlerResult.Failure("Invalid RecordId");
    }
}

public sealed class ArchiveNoteHandler : ICustomActionHandler
{
    private readonly ILogger<ArchiveNoteHandler> _logger;
    public string Name => "archive_note";

    public ArchiveNoteHandler(ILogger<ArchiveNoteHandler> logger) => _logger = logger;

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (int.TryParse(ctx.RecordId, out int id))
        {
            await db.ExecuteAsync(
                "UPDATE Note SET Status = 'archived' WHERE Id = @Id",
                new { Id = id }, tx);
            _logger.LogInformation("Archived note {Id}", id);
            return ActionHandlerResult.Success();
        }
        return ActionHandlerResult.Failure("Invalid RecordId");
    }
}
