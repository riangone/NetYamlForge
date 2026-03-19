// 联系人实体钩子 - 自动生成全名
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ContactManager.Hooks;

/// <summary>
/// 联系人全名自动生成钩子
/// </summary>
public sealed class GenerateFullNameHook : IEntityHook
{
    public string Name => "generate_fullname";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create && ctx.Operation != CrudOperation.Update)
            return Task.FromResult(HookResult.Continue());

        if (ctx.Values.TryGetValue("firstName", out var firstName) &&
            ctx.Values.TryGetValue("lastName", out var lastName))
        {
            var full = $"{lastName} {firstName}";
            ctx.Values["fullName"] = full;
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
