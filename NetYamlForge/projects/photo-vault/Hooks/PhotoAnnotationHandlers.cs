using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.PhotoVault.Hooks;

/// <summary>
/// 共享工具：读取 project_settings 并在首次调用时自动 seed 默认配置。
/// </summary>
internal static class PhotoVaultSettingsHelper
{
    internal static async Task EnsureSeedAsync(IDbConnection db, IDbTransaction? tx)
    {
        try
        {
            await db.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS project_settings (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    section_group TEXT    NOT NULL,
                    setting_key   TEXT    NOT NULL UNIQUE,
                    label         TEXT    NOT NULL,
                    value         TEXT,
                    default_value TEXT,
                    description   TEXT,
                    updated_at    TEXT
                )", transaction: tx);

            await db.ExecuteAsync(@"
                INSERT OR IGNORE INTO project_settings
                    (section_group, setting_key, label, value, default_value, description, updated_at)
                VALUES
                    ('annotation', 'annotation_provider',
                     '标注 AI 提供商', 'antigravity', 'antigravity',
                     '可选值：lmstudio / ollama / gemini / antigravity',
                     datetime('now')),
                    ('annotation', 'lmstudio_base_url',
                     'LM Studio 接口地址', 'http://localhost:1234/v1', 'http://localhost:1234/v1',
                     'LM Studio 本地服务的 OpenAI 兼容接口地址',
                     datetime('now')),
                    ('annotation', 'lmstudio_annotation_model',
                     'LM Studio 视觉模型', 'google/gemma-4-e4b', 'google/gemma-4-e4b',
                     'LM Studio 中加载的视觉语言模型 ID',
                     datetime('now')),
                    ('annotation', 'ollama_base_url',
                     'Ollama 接口地址', 'http://localhost:11434', 'http://localhost:11434',
                     'Ollama 本地服务地址',
                     datetime('now')),
                    ('annotation', 'ollama_vision_model',
                     'Ollama 视觉模型', 'llava:13b', 'llava:13b',
                     'Ollama 视觉模型名称（需先 ollama pull llava:13b）',
                     datetime('now')),
                    ('annotation', 'gemini_api_key',
                     'Gemini API 密钥', '', '',
                     '留空则从 GEMINI_API_KEY 环境变量读取',
                     datetime('now')),
                    ('embedding', 'embedding_provider',
                     '嵌入 AI 提供商', 'lmstudio', 'lmstudio',
                     '可选值：lmstudio / gemini',
                     datetime('now')),
                    ('embedding', 'embedding_lmstudio_base_url',
                     'LM Studio 嵌入接口地址', 'http://localhost:1234/v1', 'http://localhost:1234/v1',
                     'LM Studio 嵌入服务地址',
                     datetime('now')),
                    ('embedding', 'embedding_lmstudio_model',
                     'LM Studio 嵌入模型', 'text-embedding-nomic-embed-text-v1.5', 'text-embedding-nomic-embed-text-v1.5',
                     'LM Studio 嵌入模型名称',
                     datetime('now'))",
                transaction: tx);
        }
        catch
        {
            // 初始化失败不阻断主流程，调用方将使用硬编码默认值
        }
    }

    internal static async Task<string> ReadProviderAsync(IDbConnection db, IDbTransaction? tx, string settingKey, string fallback)
    {
        try
        {
            return await db.QueryFirstOrDefaultAsync<string>(
                $"SELECT COALESCE(value, @Fallback) FROM project_settings WHERE setting_key = @Key",
                new { Key = settingKey, Fallback = fallback },
                transaction: tx) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

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

        await PhotoVaultSettingsHelper.EnsureSeedAsync(db, tx);

        var provider = await PhotoVaultSettingsHelper.ReadProviderAsync(db, tx, "annotation_provider", "antigravity");

        var now = DateTime.UtcNow;

        await db.ExecuteAsync(@"
            INSERT INTO processing_queue (photo_id, file_path, status, provider, priority, retry_count, queued_at)
            VALUES (@PhotoId, @FilePath, 'queued', @Provider, 5, 0, @Now)",
            new { PhotoId = ctx.RecordId, FilePath = (string)photo.file_path, Provider = provider, Now = now }, tx);

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

        await PhotoVaultSettingsHelper.EnsureSeedAsync(db, tx);

        var provider = await PhotoVaultSettingsHelper.ReadProviderAsync(db, tx, "annotation_provider", "antigravity");

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
                VALUES (@PhotoId, @FilePath, 'queued', @Provider, 3, 0, @Now)",
                new { PhotoId = (string)photo.photo_id, FilePath = (string)photo.file_path, Provider = provider, Now = now }, tx);

            await db.ExecuteAsync(
                "UPDATE photos SET annotation_status = 'pending', updated_at = @Now WHERE photo_id = @Id",
                new { Now = now, Id = (string)photo.photo_id }, tx);

            queued++;
        }

        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// 初始化/重置默认 AI 配置项（INSERT OR IGNORE，不覆盖已有设置）。
/// </summary>
public class InitAiSettingsHandler : ICustomActionHandler
{
    public string Name => "init_ai_settings";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        await PhotoVaultSettingsHelper.EnsureSeedAsync(db, tx);
        return ActionHandlerResult.Success();
    }
}

/// <summary>
/// 将照片以高优先级入队并立即触发标注 Worker，响应立即返回，标注在后台完成。
/// </summary>
public class AnnotateNowHandler : ICustomActionHandler
{
    public string Name => "annotate_now";

    private readonly NetYamlForge.Services.BatchJob.IBatchJobScheduler _scheduler;

    public AnnotateNowHandler(NetYamlForge.Services.BatchJob.IBatchJobScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId))
            return ActionHandlerResult.Failure("照片 ID 未指定");

        var photo = await db.QueryFirstOrDefaultAsync(
            "SELECT photo_id, file_path FROM photos WHERE photo_id = @Id",
            new { Id = ctx.RecordId }, tx);
        if (photo == null)
            return ActionHandlerResult.Failure("照片不存在");

        await PhotoVaultSettingsHelper.EnsureSeedAsync(db, tx);
        var provider = await PhotoVaultSettingsHelper.ReadProviderAsync(db, tx, "annotation_provider", "antigravity");

        var now = DateTime.UtcNow;

        // 已有 queued/processing 则跳过重复入队
        var exists = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(1) FROM processing_queue WHERE photo_id = @Id AND status IN ('queued', 'processing')",
            new { Id = ctx.RecordId }, tx);
        if (exists == 0)
        {
            await db.ExecuteAsync(@"
                INSERT INTO processing_queue (photo_id, file_path, status, provider, priority, retry_count, queued_at)
                VALUES (@PhotoId, @FilePath, 'queued', @Provider, 10, 0, @Now)",
                new { PhotoId = ctx.RecordId, FilePath = (string)photo.file_path, Provider = provider, Now = now }, tx);
        }

        await db.ExecuteAsync(
            "UPDATE photos SET annotation_status = 'pending', updated_at = @Now WHERE photo_id = @Id",
            new { Now = now, Id = ctx.RecordId }, tx);

        // 立即触发 Worker（fire-and-forget，不等待完成）
        _ = _scheduler.TriggerJobNowAsync(ctx.Project, "antigravity_cli_worker");

        return ActionHandlerResult.Success();
    }
}
