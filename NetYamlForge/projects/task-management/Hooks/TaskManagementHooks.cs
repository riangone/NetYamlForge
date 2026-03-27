// task-management プロジェクト固有フック
using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.TaskManagement.Hooks;

/// <summary>
/// 期限日（DueDate）が過去でないことを検証するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateTaskDueDateHook : IEntityHook
{
    public string Name => "validate_due_date";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("DueDate", out var dueDateVal) || dueDateVal == null)
            return Task.FromResult(HookResult.Continue());

        if (dueDateVal is DateTime dt)
        {
            if (dt.Date < DateTime.Today)
                return Task.FromResult(HookResult.Abort("期限日には本日以降の日付を入力してください。"));
            return Task.FromResult(HookResult.Continue());
        }

        var dueDateStr = dueDateVal.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dueDateStr))
            return Task.FromResult(HookResult.Continue());

        if (DateOnly.TryParse(dueDateStr, out var dueDate) && dueDate < DateOnly.FromDateTime(DateTime.Today))
            return Task.FromResult(HookResult.Abort("期限日には本日以降の日付を入力してください。"));

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
