using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.PhotoVault.Hooks;

public class EnqueueAnnotationHandler : ICustomActionHandler
{
    public string Name => "enqueue_annotation";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("照片 ID 未指定");

        var photo = await db.QueryFirstOrDefaultAsync(
            @"SELECT photo_id, file_path, annotation_status FROM photos WHERE photo_id = @Id",
            new { Id = ctx.RecordId }, tx);

        if (photo == null)
            return ActionHandlerResult.Failure("未找到该照片");

        var now = DateTime.UtcNow;

        await db.ExecuteAsync(@"
            INSERT INTO processing_queue (photo_id, file_path, status, provider, priority, retry_count, queued_at)
            VALUES (@PhotoId, @FilePath, 'queued', 'lmstudio', 5, 0, @Now)",
            new { PhotoId = ctx.RecordId, FilePath = (string)photo.file_path, Now = now }, tx);

        await db.ExecuteAsync(
            "UPDATE photos SET annotation_status = 'pending', updated_at = @Now WHERE photo_id = @Id",
            new { Now = now, Id = ctx.RecordId }, tx);

        return ActionHandlerResult.Success();
    }
}

public class BatchEnqueuePendingHandler : ICustomActionHandler
{
    public string Name => "batch_enqueue_pending";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var pending = (await db.QueryAsync(
            @"SELECT photo_id, file_path FROM photos
              WHERE annotation_status IS NULL
                 OR annotation_status NOT IN ('done', 'processing')
              LIMIT 500", transaction: tx)).ToList();

        if (pending.Count == 0)
            return ActionHandlerResult.Failure("没有待处理的照片");

        var now = DateTime.UtcNow;
        var queued = 0;

        foreach (var photo in pending)
        {
            var exists = await db.QueryFirstOrDefaultAsync<int>(
                @"SELECT COUNT(1) FROM processing_queue
                  WHERE photo_id = @Id AND status IN ('queued', 'processing')",
                new { Id = (string)photo.photo_id }, tx);

            if (exists > 0) continue;

            await db.ExecuteAsync(@"
                INSERT INTO processing_queue (photo_id, file_path, status, provider, priority, retry_count, queued_at)
                VALUES (@PhotoId, @FilePath, 'queued', 'lmstudio', 3, 0, @Now)",
                new { PhotoId = (string)photo.photo_id, FilePath = (string)photo.file_path, Now = now }, tx);

            await db.ExecuteAsync(
                "UPDATE photos SET annotation_status = 'pending', updated_at = @Now WHERE photo_id = @Id",
                new { Now = now, Id = (string)photo.photo_id }, tx);

            queued++;
        }

        return ActionHandlerResult.Success();
    }
}
