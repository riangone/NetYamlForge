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
/// 従業員登録時に AppUser テーブルにも同期するフック。
/// </summary>
public class SyncEmployeeToAuthUserHook : IEntityHook
{
    public string Name => "sync_employee_to_auth_user";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (!ctx.Values.TryGetValue("user_name", out var userNameObj) || userNameObj == null)
        {
            return Task.FromResult(HookResult.Abort("ユーザー名は必須です。"));
        }

        var userName = userNameObj.ToString()!;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Task.FromResult(HookResult.Abort("ユーザー名は必須です。"));
        }

        if (!ctx.Values.TryGetValue("role", out var roleObj) || roleObj == null)
        {
            return Task.FromResult(HookResult.Abort("ロールは必須です。"));
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var userName = ctx.Values.TryGetValue("user_name", out var uo) ? uo?.ToString() : null;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Task.CompletedTask;
        }

        var employeeId = ctx.Values.TryGetValue("employee_id", out var eo) ? eo?.ToString() : null;
        if (string.IsNullOrWhiteSpace(employeeId))
        {
            return Task.CompletedTask;
        }

        var employeeName = ctx.Values.TryGetValue("name", out var no) ? no?.ToString() : userName;
        var role = ctx.Values.TryGetValue("role", out var ro) ? ro?.ToString() : "operator";

        var checkCmd = db.CreateCommand();
        checkCmd.Transaction = tx;
        checkCmd.CommandText = "SELECT COUNT(*) FROM AppUsers WHERE UserName = @userName";
        var userNameParam = checkCmd.CreateParameter();
        userNameParam.ParameterName = "@userName";
        userNameParam.Value = userName;
        checkCmd.Parameters.Add(userNameParam);

        var count = Convert.ToInt32(checkCmd.ExecuteScalar() ?? 0L);
        if (count > 0)
        {
            var updateCmd = db.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = @"
                UPDATE AppUsers 
                SET DisplayName = @displayName, 
                    PreferredLanguage = @language,
                    IsAdmin = @isAdmin,
                    UpdatedAt = @updatedAt
                WHERE UserName = @userName";

            var displayNameParam = updateCmd.CreateParameter();
            displayNameParam.ParameterName = "@displayName";
            displayNameParam.Value = employeeName;
            updateCmd.Parameters.Add(displayNameParam);

            var langParam = updateCmd.CreateParameter();
            langParam.ParameterName = "@language";
            langParam.Value = "ja-JP";
            updateCmd.Parameters.Add(langParam);

            var adminParam = updateCmd.CreateParameter();
            adminParam.ParameterName = "@isAdmin";
            adminParam.Value = IsAdminRole(role);
            updateCmd.Parameters.Add(adminParam);

            var updatedAtParam = updateCmd.CreateParameter();
            updatedAtParam.ParameterName = "@updatedAt";
            updatedAtParam.Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            updateCmd.Parameters.Add(updatedAtParam);

            var userNameUpdateParam = updateCmd.CreateParameter();
            userNameUpdateParam.ParameterName = "@userName";
            userNameUpdateParam.Value = userName;
            updateCmd.Parameters.Add(userNameUpdateParam);

            updateCmd.ExecuteNonQuery();
        }
        else
        {
            var insertCmd = db.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = @"
                INSERT INTO AppUsers (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
                VALUES (@userName, @passwordHash, @displayName, @language, @isAdmin, @isActive, @createdAt)";

            var passwordParam = insertCmd.CreateParameter();
            passwordParam.ParameterName = "@passwordHash";
            var passwordHasher = new PasswordHasher<AppUser>();
            passwordParam.Value = passwordHasher.HashPassword(new AppUser(), employeeId);
            insertCmd.Parameters.Add(passwordParam);

            var displayNameParam = insertCmd.CreateParameter();
            displayNameParam.ParameterName = "@displayName";
            displayNameParam.Value = employeeName;
            insertCmd.Parameters.Add(displayNameParam);

            var langParam = insertCmd.CreateParameter();
            langParam.ParameterName = "@language";
            langParam.Value = "ja-JP";
            insertCmd.Parameters.Add(langParam);

            var adminParam = insertCmd.CreateParameter();
            adminParam.ParameterName = "@isAdmin";
            adminParam.Value = IsAdminRole(role);
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
        }

        return Task.CompletedTask;
    }

    private static bool IsAdminRole(string? role) => role switch
    {
        "ai_admin" => true,
        "executive" => true,
        "general_manager" => true,
        _ => false
    };
}

/// <summary>
/// 従業員作成をログに記録するフック。
/// </summary>
public class LogEmployeeCreationHook : IEntityHook
{
    public string Name => "log_employee_creation";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var employeeId = ctx.Values.TryGetValue("employee_id", out var eo) ? eo?.ToString() : null;
        var employeeName = ctx.Values.TryGetValue("name", out var no) ? no?.ToString() : null;
        
        if (!string.IsNullOrEmpty(employeeId) && !string.IsNullOrEmpty(employeeName))
        {
            Console.WriteLine($"[EmployeeHook] 従業員が作成されました：{employeeId} - {employeeName}");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 従業員更新をログに記録するフック。
/// </summary>
public class LogEmployeeUpdateHook : IEntityHook
{
    public string Name => "log_employee_update";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var employeeId = ctx.Values.TryGetValue("employee_id", out var eo) ? eo?.ToString() : null;
        var employeeName = ctx.Values.TryGetValue("name", out var no) ? no?.ToString() : null;
        
        if (!string.IsNullOrEmpty(employeeId) && !string.IsNullOrEmpty(employeeName))
        {
            Console.WriteLine($"[EmployeeHook] 従業員が更新されました：{employeeId} - {employeeName}");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 従業員のメールアドレス形式を検証するフック。
/// </summary>
public class ValidateEmployeeEmailHook : IEntityHook
{
    public string Name => "validate_employee_email";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("email", out var emailObj) && emailObj is string email)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                return Task.FromResult(HookResult.Abort("メールアドレスの形式が無効です。"));
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 従業員名を正規化するフック（前後の空白を除去）。
/// </summary>
public class NormalizeEmployeeNameHook : IEntityHook
{
    public string Name => "normalize_employee_name";

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
/// 従業員登録時のユーザー名・メールアドレスの一意性を検証するフック。
/// </summary>
public class ValidateEmployeeRegistrationHook : IEntityHook
{
    public string Name => "validate_employee_registration";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("user_name", out var userName) && userName != null)
        {
            var userNameStr = userName.ToString()!;
            if (!string.IsNullOrWhiteSpace(userNameStr))
            {
                var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM employees WHERE user_name = @userName";
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

        if (ctx.Values.TryGetValue("email", out var email) && email != null)
        {
            var emailStr = email.ToString()!;
            if (!string.IsNullOrWhiteSpace(emailStr))
            {
                var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM employees WHERE email = @email";
                var param = cmd.CreateParameter();
                param.ParameterName = "@email";
                param.Value = emailStr;
                cmd.Parameters.Add(param);

                var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0L);
                if (count > 0)
                {
                    return Task.FromResult(HookResult.Abort("このメールアドレスは既に使用されています。"));
                }
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
