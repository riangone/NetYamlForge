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

public class EmbedPhotoAction : IPageActionHandler
{
    public string ActionName => "embed-photo";
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
        var logger = ctx.Services.GetRequiredService<ILogger<EmbedPhotoAction>>();

        var photo = await db.QueryFirstOrDefaultAsync(
            "SELECT photo_id, annotation_status FROM photos WHERE photo_id = @Id AND deleted_at IS NULL",
            new { Id = photo_id });
        if (photo == null)
        {
            return new NotFoundObjectResult("照片不存在");
        }

        string annotationStatus = photo.annotation_status?.ToString() ?? "";
        if (annotationStatus != "done")
        {
            var isHtmxRequest = ctx.HttpContext.Request.Headers.TryGetValue("HX-Request", out var hxVal) && hxVal == "true";
            if (isHtmxRequest)
            {
                ctx.HttpContext.Response.Headers["HX-Trigger"] = ToAsciiJson("{\"show-toast\":{\"message\":\"请先等待标注完成再生成嵌入\",\"type\":\"warning\"}}");
                return new BadRequestObjectResult("请先等待标注完成");
            }
            return new BadRequestObjectResult("请先等待标注完成");
        }

        // 立即触发嵌入 Generator Job
        _ = scheduler.TriggerJobNowAsync(ctx.Project, "embedding_generator");

        try
        {
            await audit.WriteAsync("embed_photo", "photos", $"Page={ctx.PageName},PhotoId={photo_id}", ctx.User.Identity?.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audit write failed for action=embed_photo, entity=photos");
        }

        var isHtmx = ctx.HttpContext.Request.Headers.TryGetValue("HX-Request", out var v) && v == "true";
        if (isHtmx)
        {
            ctx.HttpContext.Response.Headers["HX-Trigger"] = ToAsciiJson("{\"show-toast\":{\"message\":\"已开始生成嵌入向量\",\"type\":\"success\"}}");
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
