using System;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

/// <summary>
/// AI 对话数据 PII 自动脱敏钩子
/// 
/// 功能:
/// 1. 手机号中间 4 位掩码(138****5678)
/// 2. 邮箱地址部分掩码(abc***@example.com)
/// 3. 身份证号部分掩码(110105****1234****)
/// 4. 姓名仅保留首字符(张**)
/// 
/// YAML 配置示例:
///   hooks:
///     beforeCreate: "ai_pii_mask"
///     beforeUpdate: "ai_pii_mask"
/// </summary>
public class AiPiiMaskHook : IEntityHook
{
    public string Name => "ai_pii_mask";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 手机号脱敏
        if (ctx.Values.TryGetValue("phone", out var phone) && phone is string phoneStr)
        {
            ctx.Values["phone"] = MaskPhone(phoneStr);
        }

        // 邮箱脱敏
        if (ctx.Values.TryGetValue("email", out var email) && email is string emailStr)
        {
            ctx.Values["email"] = MaskEmail(emailStr);
        }

        // 姓名脱敏
        if (ctx.Values.TryGetValue("customer_name", out var name) && name is string nameStr)
        {
            ctx.Values["customer_name"] = MaskName(nameStr);
        }

        // 标记已脱敏
        ctx.Data["pii_masked"] = true;
        ctx.Data["masked_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    /// <summary>
    /// 手机号掩码:13812345678 → 138****5678
    /// </summary>
    private static string MaskPhone(string phone)
    {
        // 移除所有非数字字符
        var digits = Regex.Replace(phone, @"[^\d]", "");

        if (digits.Length == 11)
        {
            // 中国大陆手机号:保留前 3 位和后 4 位
            return $"{digits[..3]}****{digits[^4..]}";
        }
        else if (digits.Length >= 7)
        {
            // 其他格式:保留前 3 位和后 2 位
            var maskLen = Math.Max(2, digits.Length - 5);
            return $"{digits[..3]}{new string('*', maskLen)}{digits[^2..]}";
        }

        // 过短号码:全部掩码
        return new string('*', Math.Max(3, digits.Length));
    }

    /// <summary>
    /// 邮箱掩码:zhang@example.com → zha***@example.com
    /// </summary>
    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return email; // 无效邮箱,原样返回

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        if (localPart.Length <= 2)
        {
            // 短用户名:保留首字符
            return $"{localPart[0]}{new string('*', localPart.Length - 1)}{domain}";
        }

        // 长用户名:保留前 3 字符
        var keepLen = Math.Min(3, localPart.Length);
        return $"{localPart[..keepLen]}***{domain}";
    }

    /// <summary>
    /// 姓名掩码:张三丰 → 张**
    /// </summary>
    private static string MaskName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        if (name.Length == 1)
        {
            return name; // 单字姓名不掩码
        }
        else if (name.Length == 2)
        {
            return $"{name[0]}*"; // 二字姓名掩码最后一位
        }
        else
        {
            // 三字及以上:保留首字符,其余掩码
            return $"{name[0]}{new string('*', name.Length - 1)}";
        }
    }
}
