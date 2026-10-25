using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TodoApp.Hooks;

/// <summary>
/// Todo 实体 CRUD 校验与默认值填充钩子
/// </summary>
public sealed class TodoValidationHook : IEntityHook
{
    public string Name => "todo_validate";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 1. 标题非空校验
        if (!ctx.Values.TryGetValue("title", out var titleObj) || string.IsNullOrWhiteSpace(titleObj?.ToString()))
        {
            return Task.FromResult(HookResult.Abort("待办标题不能为空 (Title is required)"));
        }

        // 2. 优先级校验与默认值
        if (ctx.Values.TryGetValue("priority", out var priorityObj) && priorityObj != null && !string.IsNullOrWhiteSpace(priorityObj.ToString()))
        {
            var p = priorityObj.ToString()!.ToLowerInvariant();
            if (p != "low" && p != "medium" && p != "high")
            {
                return Task.FromResult(HookResult.Abort("优先级必须为 low, medium 或 high"));
            }
            ctx.Values["priority"] = p;
        }
        else
        {
            ctx.Values["priority"] = "medium";
        }

        // 3. 完成状态默认值
        if (ctx.Operation == CrudOperation.Create)
        {
            if (!ctx.Values.ContainsKey("is_completed") || ctx.Values["is_completed"] == null)
            {
                ctx.Values["is_completed"] = 0;
            }
            else if (ctx.Values["is_completed"] is bool b)
            {
                ctx.Values["is_completed"] = b ? 1 : 0;
            }
        }

        // 4. 时间戳自动维护
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
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
/// 切换 Todo 完成状态的自定义行级操作
/// </summary>
public sealed class TodoToggleCompleteHandler : ICustomActionHandler
{
    public string Name => "todo_toggle_complete";

    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!int.TryParse(ctx.RecordId, out int id))
        {
            return ActionHandlerResult.Failure("无效的待办 ID (Invalid RecordId)");
        }

        var row = await db.QueryFirstOrDefaultAsync<(int Id, int IsCompleted)?>(
            "SELECT id AS Id, is_completed AS IsCompleted FROM todo WHERE id = @Id",
            new { Id = id }, tx);

        if (row == null)
        {
            return ActionHandlerResult.Failure("未找到该待办事项 (Todo item not found)");
        }

        var newCompleted = row.Value.IsCompleted == 1 ? 0 : 1;
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await db.ExecuteAsync(
            "UPDATE todo SET is_completed = @IsCompleted, updated_at = @Now WHERE id = @Id",
            new { Id = id, IsCompleted = newCompleted, Now = now }, tx);

        ctx.Data["newState"] = newCompleted;
        return ActionHandlerResult.Success();
    }
}
