// 联系人实体钩子 - 邮箱验证
using System.Data;
using System.Text.RegularExpressions;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.ContactManager.Hooks;

/// <summary>
/// 联系人邮箱验证钩子
/// </summary>
public class ValidateContactEmailHook : IEntityHook
{
    public string Name => "validate_contact_email";

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<HookResult> BeforeAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        if (context.Values.TryGetValue("email", out var emailObj) &&
            emailObj is string email &&
            !string.IsNullOrWhiteSpace(email))
        {
            if (!EmailRegex.IsMatch(email))
            {
                return Task.FromResult(HookResult.Abort($"无效的邮箱格式：{email}"));
            }
            
            // 转换为小写
            context.Values["email"] = email.ToLowerInvariant();
        }
        
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext context, IDbConnection db, IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
