using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

/// <summary>
/// 顧客の電話番号形式を検証するフック。
/// </summary>
public class ValidateCustomerPhoneHook : IEntityHook
{
    public string Name => "validate_customer_phone";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("phone", out var phone) && phone is string phoneStr)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(phoneStr, @"^[0-9\-]+$"))
            {
                return Task.FromResult(HookResult.Abort("電話番号は数字とハイフンのみ使用できます。"));
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 顧客名を正規化するフック（前後の空白を除去）。
/// </summary>
public class NormalizeCustomerNameHook : IEntityHook
{
    public string Name => "normalize_customer_name";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("name", out var name) && name is string s)
        {
            ctx.Values["name"] = s.Trim();
        }
        if (ctx.Values.TryGetValue("name_kana", out var kana) && kana is string k)
        {
            ctx.Values["name_kana"] = k.Trim();
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 顧客登録時のユーザー名を検証するフック。
/// </summary>
public class ValidateCustomerRegistrationHook : IEntityHook
{
    public string Name => "validate_customer_registration";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("user_name", out var userName) && userName != null)
        {
            var userNameStr = userName.ToString()!;
            if (!string.IsNullOrWhiteSpace(userNameStr))
            {
                var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM customers WHERE user_name = @userName";
                var param = cmd.CreateParameter();
                param.ParameterName = "@userName";
                param.Value = userNameStr;
                cmd.Parameters.Add(param);
                
                var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0L);
                if (count > 0)
                {
                    return Task.FromResult(HookResult.Abort("このユーザー名は既に使用されています。"));
                }
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 顧客登録時に AppUser テーブルにも同期するフック。
/// </summary>
public class SyncCustomerToAuthUserHook : IEntityHook
{
    public string Name => "sync_customer_to_auth_user";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("user_name", out var userNameObj) || userNameObj == null)
        {
            return Task.CompletedTask;
        }

        var userName = userNameObj.ToString()!;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Task.CompletedTask;
        }

        if (!ctx.Values.TryGetValue("customer_id", out var customerIdObj) || customerIdObj == null)
        {
            return Task.CompletedTask;
        }

        var customerId = customerIdObj.ToString()!;
        var customerName = ctx.Values.TryGetValue("name", out var nameObj) ? (nameObj?.ToString() ?? userName) : userName;

        var checkCmd = db.CreateCommand();
        checkCmd.Transaction = tx;
        checkCmd.CommandText = "SELECT COUNT(*) FROM AppUser WHERE UserName = @userName";
        var userNameParam = checkCmd.CreateParameter();
        userNameParam.ParameterName = "@userName";
        userNameParam.Value = userName;
        checkCmd.Parameters.Add(userNameParam);

        var count = Convert.ToInt32(checkCmd.ExecuteScalar() ?? 0L);
        if (count > 0)
        {
            return Task.CompletedTask;
        }

        var insertCmd = db.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = @"
            INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
            VALUES (@userName, @passwordHash, @displayName, @language, @isAdmin, @isActive, @createdAt)";

        var passwordParam = insertCmd.CreateParameter();
        passwordParam.ParameterName = "@passwordHash";
        var passwordHasher = new PasswordHasher<AppUser>();
        passwordParam.Value = passwordHasher.HashPassword(new AppUser(), customerId);
        insertCmd.Parameters.Add(passwordParam);

        var displayNameParam = insertCmd.CreateParameter();
        displayNameParam.ParameterName = "@displayName";
        displayNameParam.Value = customerName;
        insertCmd.Parameters.Add(displayNameParam);

        var langParam = insertCmd.CreateParameter();
        langParam.ParameterName = "@language";
        langParam.Value = "ja-JP";
        insertCmd.Parameters.Add(langParam);

        var adminParam = insertCmd.CreateParameter();
        adminParam.ParameterName = "@isAdmin";
        adminParam.Value = false;
        insertCmd.Parameters.Add(adminParam);

        var activeParam = insertCmd.CreateParameter();
        activeParam.ParameterName = "@isActive";
        activeParam.Value = true;
        insertCmd.Parameters.Add(activeParam);

        var createdAtParam = insertCmd.CreateParameter();
        createdAtParam.ParameterName = "@createdAt";
        createdAtParam.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        insertCmd.Parameters.Add(createdAtParam);

        insertCmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }
}
