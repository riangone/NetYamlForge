// 交互记录钩子
using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ContactManager.Hooks;

/// <summary>
/// 交互记录默认值设置
/// </summary>
public class InteractionSetDefaultsHook : IEntityHook
{
    public string Name => "interaction_set_defaults";

    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        // 如果没有指定公司，从联系人获取
        if ((!context.Values.TryGetValue("companyId", out var companyId) || companyId == null) &&
            context.Values.TryGetValue("contactId", out var contactId) && contactId != null)
        {
            var company = db.ExecuteScalar<object>(@"
                SELECT companyId FROM contact WHERE id = @contactId
            ", new { contactId }, tx);
            
            if (company != null && company != DBNull.Value)
            {
                context.Values["companyId"] = company;
            }
        }
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// 交互记录更新时间戳
/// </summary>
public class InteractionUpdateTimestampHook : IEntityHook
{
    public string Name => "interaction_update_timestamp";

    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        // 如果状态改为 completed 且没有完成时间，自动设置
        if (context.Values.TryGetValue("status", out var status) &&
            status?.ToString() == "completed" &&
            (!context.Values.TryGetValue("completedAt", out var completedAt) || completedAt == null))
        {
            context.Values["completedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
