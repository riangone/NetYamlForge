// 联系人实体钩子 - 记录创建日志
using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ContactManager.Hooks;

/// <summary>
/// 联系人创建后记录日志
/// </summary>
public class LogContactCreatedHook : IEntityHook
{
    public string Name => "log_contact_created";

    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        return Task.FromResult(HookResult.Continue());
    }

    public async Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        // 记录到交互日志
        if (context.Values.TryGetValue("fullName", out var fullName) &&
            context.Values.TryGetValue("email", out var email))
        {
            await db.ExecuteAsync(@"
                INSERT INTO interaction (contactId, companyId, type, subject, description, status, createdAt)
                VALUES (@contactId, @companyId, 'note', '联系人已创建', 
                        '新联系人：' || @fullName || ' (' || @email || ')', 'completed', datetime('now'))
            ", new
            {
                contactId = context.Id,
                companyId = context.Values.TryGetValue("companyId", out var cid) ? cid : null,
                fullName = fullName,
                email = email
            }, tx);
        }
    }
}
