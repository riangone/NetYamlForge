using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Page;
using NetYamlForge.Services;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Services.Auth;

namespace NetYamlForge.Projects.PhotoVocab.Hooks.PageActions;

public class AnnotatePhotoAction : IPageActionHandler
{
    public string ActionName => "annotate-photo";
    public string? Project => "photo-vocab";

    public async Task<IActionResult> HandleAsync(PageActionContext ctx)
    {
        ctx.Query.TryGetValue("photo_id", out var photo_id);
        if (string.IsNullOrWhiteSpace(photo_id))
        {
            return new BadRequestObjectResult("照片 ID 未指定");
        }

        var db = ctx.Services.GetRequiredService<IDbConnection>();
        var scheduler = ctx.Services.GetRequiredService<IBatchJobScheduler>();
        var audit = ctx.Services.GetRequiredService<IAuditLogService>();
        var logger = ctx.Services.GetRequiredService<ILogger<AnnotatePhotoAction>>();

        var photo = await db.QueryFirstOrDefaultAsync(
            "SELECT photo_id, file_path FROM photos WHERE photo_id = @Id AND deleted_at IS NULL",
            new { Id = photo_id });
        if (photo == null)
        {
            return new NotFoundObjectResult("照片不存在");
        }

        await PhotoVocabSettingsHelper.EnsureSeedAsync(db, null);
        var provider = await PhotoVocabSettingsHelper.ReadProviderAsync(db, null, "annotation_provider", "antigravity");

        var now = DateTime.UtcNow;

        var exists = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(1) FROM processing_queue WHERE photo_id = @Id AND status IN ('queued', 'processing')",
            new { Id = photo_id });
        if (exists == 0)
        {
            await db.ExecuteAsync(@"
                INSERT INTO processing_queue (photo_id, file_path, status, provider, priority, retry_count, queued_at)
                VALUES (@PhotoId, @FilePath, 'queued', @Provider, 10, 0, @Now)",
                new { PhotoId = photo_id, FilePath = (string)photo.file_path, Provider = provider, Now = now });
        }

        await db.ExecuteAsync(
            "UPDATE photos SET annotation_status = 'pending', updated_at = @Now WHERE photo_id = @Id",
            new { Now = now, Id = photo_id });

        // 立即触发 Worker
        _ = scheduler.TriggerJobNowAsync(ctx.Project, "antigravity_cli_worker");

        try
        {
            await audit.WriteAsync("annotate_photo", "photos", $"Page={ctx.PageName},PhotoId={photo_id}", ctx.User.Identity?.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audit write failed for action=annotate_photo, entity=photos");
        }

        var isHtmx = ctx.HttpContext.Request.Headers.TryGetValue("HX-Request", out var v) && v == "true";
        if (isHtmx)
        {
            ctx.HttpContext.Response.Headers["HX-Trigger"] = ToAsciiJson("{\"show-toast\":{\"message\":\"已入队后台标注，请稍后刷新页面查看结果\",\"type\":\"success\"}}");
            return new OkResult();
        }

        return new RedirectResult($"/{ctx.Project}/Page/{ctx.PageName}");
    }

    private static string ToAsciiJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        var sb = new System.Text.StringBuilder();
        foreach (var c in json)
        {
            if (c > 127)
            {
                sb.AppendFormat("\\u{0:x4}", (int)c);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

internal static class PhotoVocabSettingsHelper
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
                     '标注 AI 提供商', 'lmstudio', 'lmstudio',
                     '可选值：lmstudio / ollama / gemini / antigravity',
                     datetime('now'))", transaction: tx);
        }
        catch
        {
        }
    }

    internal static async Task<string> ReadProviderAsync(IDbConnection db, IDbTransaction? tx, string settingKey, string fallback)
    {
        try
        {
            return await db.QueryFirstOrDefaultAsync<string>(
                "SELECT COALESCE(value, @Fallback) FROM project_settings WHERE setting_key = @Key",
                new { Key = settingKey, Fallback = fallback },
                transaction: tx) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

