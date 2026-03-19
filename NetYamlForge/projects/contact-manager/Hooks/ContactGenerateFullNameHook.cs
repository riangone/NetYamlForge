// 联系人实体钩子 - 自动生成全名
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ContactManager.Hooks;

/// <summary>
/// 联系人全名自动生成钩子
/// </summary>
public class ContactGenerateFullNameHook : IEntityHook
{
    public string Name => "contact_generate_fullname";

    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        // 自动生成全名：姓 + 名
        if (context.Values.TryGetValue("firstName", out var firstName) &&
            context.Values.TryGetValue("lastName", out var lastName))
        {
            var full = $"{lastName} {firstName}";
            context.Values["fullName"] = full;
            context.Data["fullNameGenerated"] = true;
        }
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
