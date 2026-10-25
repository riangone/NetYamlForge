using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.BlogApp.Hooks;

/// <summary>
/// 文章实体 CRUD 校验与自动化属性维护钩子
/// </summary>
public sealed class BlogPostValidationHook : IEntityHook
{
    public string Name => "blog_post_validate";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 1. 标题与正文非空校验
        if (!ctx.Values.TryGetValue("title", out var titleObj) || string.IsNullOrWhiteSpace(titleObj?.ToString()))
        {
            return Task.FromResult(HookResult.Abort("文章标题不能为空 (Title is required)"));
        }

        if (ctx.Operation == CrudOperation.Create)
        {
            if (!ctx.Values.TryGetValue("content", out var contentObj) || string.IsNullOrWhiteSpace(contentObj?.ToString()))
            {
                return Task.FromResult(HookResult.Abort("文章正文不能为空 (Content is required)"));
            }
        }

        // 2. Slug 别名自动生成或清洗
        var title = titleObj.ToString()!.Trim();
        if (!ctx.Values.TryGetValue("slug", out var slugObj) || string.IsNullOrWhiteSpace(slugObj?.ToString()))
        {
            var autoSlug = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9\u4e00-\u9fa5]+", "-").Trim('-');
            if (string.IsNullOrEmpty(autoSlug))
            {
                autoSlug = "post-" + Guid.NewGuid().ToString("N")[..8];
            }
            ctx.Values["slug"] = autoSlug;
        }

        // 3. 状态合法性校验与默认值
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        if (ctx.Values.TryGetValue("status", out var statusObj) && statusObj != null && !string.IsNullOrWhiteSpace(statusObj.ToString()))
        {
            var status = statusObj.ToString()!.ToLowerInvariant();
            if (status != "draft" && status != "published" && status != "archived")
            {
                return Task.FromResult(HookResult.Abort("文章状态必须为 draft, published 或 archived"));
            }
            ctx.Values["status"] = status;

            // 发布时间自动维护
            if (status == "published")
            {
                if (!ctx.Values.TryGetValue("published_at", out var pubObj) || pubObj == null || string.IsNullOrWhiteSpace(pubObj.ToString()))
                {
                    ctx.Values["published_at"] = now;
                }
            }
        }
        else if (ctx.Operation == CrudOperation.Create)
        {
            ctx.Values["status"] = "draft";
        }

        // 4. 时间戳自动维护
        if (ctx.Operation == CrudOperation.Create)
        {
            if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            {
                ctx.Values["created_at"] = now;
            }
        }
        ctx.Values["updated_at"] = now;

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 快速切换文章发布与草稿状态的行级动作处理器
/// </summary>
public sealed class BlogPostTogglePublishHandler : ICustomActionHandler
{
    public string Name => "blog_post_toggle_publish";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!int.TryParse(ctx.RecordId, out int id))
        {
            return ActionHandlerResult.Failure("无效的文章 ID (Invalid RecordId)");
        }

        var row = await db.QueryFirstOrDefaultAsync<(int Id, string Status)?>(
            "SELECT id AS Id, status AS Status FROM blog_post WHERE id = @Id",
            new { Id = id }, tx);

        if (row == null)
        {
            return ActionHandlerResult.Failure("未找到该文章记录 (Blog post not found)");
        }

        var currentStatus = row.Value.Status?.ToLowerInvariant() ?? "draft";
        var newStatus = currentStatus == "published" ? "draft" : "published";
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        if (newStatus == "published")
        {
            await db.ExecuteAsync(
                "UPDATE blog_post SET status = @Status, published_at = COALESCE(published_at, @Now), updated_at = @Now WHERE id = @Id",
                new { Id = id, Status = newStatus, Now = now }, tx);
        }
        else
        {
            await db.ExecuteAsync(
                "UPDATE blog_post SET status = @Status, updated_at = @Now WHERE id = @Id",
                new { Id = id, Status = newStatus, Now = now }, tx);
        }

        ctx.Data["newStatus"] = newStatus;
        return ActionHandlerResult.Success();
    }
}
