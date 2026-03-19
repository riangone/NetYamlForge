// 联系人实体钩子 - 邮箱格式验证
using System.Data;
using System.Text.RegularExpressions;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ContactManager.Hooks;

/// <summary>
/// 联系人邮箱验证钩子
/// </summary>
public sealed class ValidateContactEmailHook : IEntityHook
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Name => "validate_contact_email";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create && ctx.Operation != CrudOperation.Update)
            return Task.FromResult(HookResult.Continue());

        if (ctx.Values.TryGetValue("email", out var emailObj) && emailObj is string email)
        {
            if (!string.IsNullOrWhiteSpace(email) && !EmailRegex.IsMatch(email))
            {
                return Task.FromResult(HookResult.Abort($"无效的邮箱格式：{email}"));
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                ctx.Values["email"] = email.ToLowerInvariant();
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
