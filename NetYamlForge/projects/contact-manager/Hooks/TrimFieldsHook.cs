// 联系人实体钩子 - 字段修剪
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ContactManager.Hooks;

/// <summary>
/// 联系人字段修剪钩子
/// </summary>
public class TrimContactFieldsHook : IEntityHook
{
    public string Name => "trim_contact_fields";

    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        // 修剪字符串字段
        TrimField(context, "firstName");
        TrimField(context, "lastName");
        TrimField(context, "title");
        TrimField(context, "department");
        TrimField(context, "phone");
        TrimField(context, "linkedin");
        TrimField(context, "tags");
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }

    private static void TrimField(EntityHookContext context, string fieldName)
    {
        if (context.Values.TryGetValue(fieldName, out var value) &&
            value is string str &&
            !string.IsNullOrEmpty(str))
        {
            context.Values[fieldName] = str.Trim();
        }
    }
}

/// <summary>
/// 公司字段修剪钩子
/// </summary>
public class TrimCompanyFieldsHook : IEntityHook
{
    public string Name => "trim_company_fields";

    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        TrimField(context, "name");
        TrimField(context, "industry");
        TrimField(context, "website");
        TrimField(context, "phone");
        TrimField(context, "email");
        TrimField(context, "address");
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }

    private static void TrimField(EntityHookContext context, string fieldName)
    {
        if (context.Values.TryGetValue(fieldName, out var value) &&
            value is string str &&
            !string.IsNullOrEmpty(str))
        {
            context.Values[fieldName] = str.Trim();
        }
    }
}
