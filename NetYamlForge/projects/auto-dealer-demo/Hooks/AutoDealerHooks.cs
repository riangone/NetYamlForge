// auto-dealer-demo プロジェクト固有フック
// 顧客・サービス予約の入力検証・自動補完フックの実装です。
//
// YAML での参照例:
//   customers.yml:
//     hooks:
//       beforeCreate: [validate_customer_phone, normalize_customer_name, validate_customer_registration]
//       beforeUpdate: [validate_customer_phone, normalize_customer_name]
//       afterCreate: [sync_customer_to_auth_user]
//   service_appointments.yml:
//     hooks:
//       beforeCreate: [validate_appointment_date, validate_appointment_time]
//       beforeUpdate: [validate_appointment_date]
//   sales_leads.yml:
//     hooks:
//       beforeCreate: [set_lead_timestamps, calculate_lead_score]
//       beforeUpdate: [update_lead_updated_at, calculate_lead_score]

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.AspNetCore.Identity;
using NetYamlForge.Models.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

/// <summary>
/// 顧客の電話番号形式を検証するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateCustomerPhoneHook : IEntityHook
{
    public string Name => "validate_customer_phone";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("phone", out var phone) && phone is string phoneStr)
        {
            // 簡易検証：数字とハイフンのみ許可
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
/// YAML: beforeCreate / beforeUpdate に指定します。
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
/// サービス予約日の検証を行うフック（過去日付不可）。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateAppointmentDateHook : IEntityHook
{
    public string Name => "validate_appointment_date";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("preferred_date", out var preferredDate) && preferredDate != null)
        {
            var dateStr = preferredDate.ToString();
            if (DateTime.TryParse(dateStr, out var pd) && pd.Date < DateTime.Today)
                return Task.FromResult(HookResult.Abort("過去の日付には予約できません。"));
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// サービス予約時間の検証を行うフック（営業時間：9:00-18:00）。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateAppointmentTimeHook : IEntityHook
{
    public string Name => "validate_appointment_time";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("preferred_date", out var preferredDate) && preferredDate != null)
        {
            var dateStr = preferredDate.ToString();
            if (DateTime.TryParse(dateStr, out var pd))
            {
                if (pd.Hour < 9 || pd.Hour >= 18)
                    return Task.FromResult(HookResult.Abort("予約時間は 9:00-18:00 の間である必要があります。"));
            }
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

// ─────────────────────────────────────────────────────────────────────────────
// 以下: sales_leads エンティティ用フック
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// sales_leads 作成時に created_at / updated_at / status / lead_score を自動補完するフック。
/// YAML: beforeCreate (sales_leads) に指定します。
/// </summary>
public class SetLeadTimestampsHook : IEntityHook
{
    public string Name => "set_lead_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            ctx.Values["created_at"] = now;
        if (!ctx.Values.ContainsKey("updated_at") || ctx.Values["updated_at"] == null)
            ctx.Values["updated_at"] = now;
        if (!ctx.Values.ContainsKey("status") || ctx.Values["status"] == null)
            ctx.Values["status"] = "new";
        if (!ctx.Values.ContainsKey("lead_score") || ctx.Values["lead_score"] == null)
            ctx.Values["lead_score"] = 50;

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// sales_leads 更新時に updated_at を自動更新するフック。
/// YAML: beforeUpdate (sales_leads) に指定します。
/// </summary>
public class UpdateLeadTimestampHook : IEntityHook
{
    public string Name => "update_lead_updated_at";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Values["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// lead_activities 作成時に created_at・created_by を自動補完するフック。
/// YAML: beforeCreate (lead_activities) に指定します。
/// </summary>
public class SetLeadActivityTimestampsHook : IEntityHook
{
    public string Name => "set_lead_activity_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            ctx.Values["created_at"] = now;
        if (string.IsNullOrEmpty(ctx.Values.GetValueOrDefault("created_by")?.ToString()) && ctx.UserName != null)
            ctx.Values["created_by"] = ctx.UserName;
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// sales_leads のリードスコアを 0-100 の範囲にクランプするフック。
/// YAML: beforeCreate / beforeUpdate (sales_leads) に指定します。
/// </summary>
public class CalculateLeadScoreHook : IEntityHook
{
    public string Name => "calculate_lead_score";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("lead_score", out var scoreVal) && scoreVal != null)
        {
            if (int.TryParse(scoreVal.ToString(), out var s))
                ctx.Values["lead_score"] = Math.Clamp(s, 0, 100);
        }
        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

// ─────────────────────────────────────────────────────────────────────────────
// 以下：lead_nurturing_tasks エンティティ用フック
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// lead_nurturing_tasks 作成時に created_at / updated_at / due_date を自動設定するフック。
/// YAML: beforeCreate (lead_nurturing_tasks) に指定します。
/// </summary>
public class SetNurturingTaskTimestampsHook : IEntityHook
{
    public string Name => "set_nurturing_task_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            ctx.Values["created_at"] = now;
        if (!ctx.Values.ContainsKey("updated_at") || ctx.Values["updated_at"] == null)
            ctx.Values["updated_at"] = now;

        // 未設定の場合、デフォルトで 3 日後に設定
        if (!ctx.Values.ContainsKey("due_date") || ctx.Values["due_date"] == null)
        {
            if (ctx.Values.TryGetValue("task_type", out var taskType))
            {
                // タスクタイプに応じて締切を調整
                var days = taskType?.ToString() switch
                {
                    "test_drive_invite" => 1,      // 试驾邀请：1 天内
                    "followup_call" => 1,          // 跟进电话：1 天内
                    "price_alert" => 2,            // 价格提醒：2 天内
                    "special_offer" => 3,          // 特别优惠：3 天内
                    _ => 3                          // 默认：3 天内
                };
                ctx.Values["due_date"] = DateTime.Now.AddDays(days).ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                ctx.Values["due_date"] = DateTime.Now.AddDays(3).ToString("yyyy-MM-dd HH:mm:ss");
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// lead_nurturing_tasks 更新時に updated_at を自動更新し、完了時に completed_at を設定するフック。
/// YAML: beforeUpdate (lead_nurturing_tasks) に指定します。
/// </summary>
public class UpdateNurturingTaskTimestampsHook : IEntityHook
{
    public string Name => "update_nurturing_task_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Values["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // ステータスが completed に変更された場合、完了日時を記録
        if (ctx.Values.TryGetValue("status", out var status) &&
            status?.ToString() == "completed")
        {
            ctx.Values["completed_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// lead_nurturing_tasks の優先度スコアを計算するフック。
/// タスクタイプとリードスコアに基づいて 0-100 の範囲で計算します。
/// YAML: beforeCreate (lead_nurturing_tasks) に指定します。
/// </summary>
public class CalculateNurturingPriorityHook : IEntityHook
{
    public string Name => "calculate_nurturing_priority";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // タスクタイプに基づく基本優先度
        var basePriority = ctx.Values.TryGetValue("task_type", out var taskType)
            ? GetBasePriority(taskType?.ToString())
            : 50;

        // リードスコアがあれば加算
        var leadScore = 0;
        if (ctx.Values.TryGetValue("lead_score", out var scoreVal) && scoreVal != null)
        {
            if (int.TryParse(scoreVal.ToString(), out var s))
            {
                leadScore = s;
            }
        }

        // 最終優先度 = 基本優先度 + (リードスコア / 10)、最大 100
        var finalPriority = Math.Min(100, basePriority + (leadScore / 10));
        ctx.Values["priority_score"] = finalPriority;

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;

    /// <summary>
    /// タスクタイプに基づく基本優先度を取得
    /// </summary>
    private static int GetBasePriority(string? taskType) => taskType switch
    {
        "test_drive_invite" => 70,      // 试驾邀请：高优先级
        "competitor_counter" => 75,     // 竞品应对：高优先级
        "followup_call" => 60,          // 跟进电话：中优先级
        "price_alert" => 65,            // 价格提醒：中优先级
        "special_offer" => 55,          // 特别优惠：低优先级
        "send_info" => 50,              // 发送资料：低优先级
        _ => 50                          // 默认
    };
}

/// <summary>
/// 顧客登録時のユーザー名を検証するフック。
/// YAML: beforeCreate (customers) に指定します。
/// </summary>
public class ValidateCustomerRegistrationHook : IEntityHook
{
    public string Name => "validate_customer_registration";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // user_name が指定されている場合、一意性を検証
        if (ctx.Values.TryGetValue("user_name", out var userName) && userName != null)
        {
            var userNameStr = userName.ToString()!;
            if (!string.IsNullOrWhiteSpace(userNameStr))
            {
                // 既に使用されているユーザー名かチェック
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

// ─────────────────────────────────────────────────────────────────────────────
// 顧客・従業員 Auth 同期フック
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 顧客登録時に AppUser テーブルにも同期するフック。
/// YAML: afterCreate (customers) に指定します。
/// 注意：RegisterCustomerAsync で既に AppUser と customers の両方が作成されるため、
/// このフックは customers テーブルを直接操作する場合にのみ使用されます。
/// </summary>
public class SyncCustomerToAuthUserHook : IEntityHook
{
    public string Name => "sync_customer_to_auth_user";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // user_name が指定されていない場合はスキップ
        if (!ctx.Values.TryGetValue("user_name", out var userNameObj) || userNameObj == null)
        {
            return Task.CompletedTask;
        }

        var userName = userNameObj.ToString()!;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Task.CompletedTask;
        }

        // customer_id を取得
        if (!ctx.Values.TryGetValue("customer_id", out var customerIdObj) || customerIdObj == null)
        {
            return Task.CompletedTask;
        }

        var customerId = customerIdObj.ToString()!;
        var customerName = ctx.Values.TryGetValue("name", out var nameObj) ? (nameObj?.ToString() ?? userName) : userName;

        // AppUser テーブルに存在するかチェック（表名は AppUser）
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
            // 既に存在する場合はスキップ
            return Task.CompletedTask;
        }

        // AppUser に新規登録（customers 表から作成する場合のみ）
        var insertCmd = db.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = @"
            INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
            VALUES (@userName, @passwordHash, @displayName, @language, @isAdmin, @isActive, @createdAt)";

        var passwordParam = insertCmd.CreateParameter();
        passwordParam.ParameterName = "@passwordHash";
        // 初期パスワードは顧客 ID を使用（後で変更を促す）
        // PasswordHasher の既定実装は user 引数を参照しないため、ダミーインスタンスを渡す
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

/// <summary>
/// 従業員登録時に AppUser テーブルにも同期するフック。
/// YAML: beforeCreate (employees) に指定します。
/// </summary>
public class SyncEmployeeToAuthUserHook : IEntityHook
{
    public string Name => "sync_employee_to_auth_user";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // user_name が必須か検証
        if (!ctx.Values.TryGetValue("user_name", out var userNameObj) || userNameObj == null)
        {
            return Task.FromResult(HookResult.Abort("ユーザー名は必須です。"));
        }

        var userName = userNameObj.ToString()!;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Task.FromResult(HookResult.Abort("ユーザー名は必須です。"));
        }

        // role が指定されているか確認
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

        // AppUser テーブルに存在するかチェック
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
            // 既に存在する場合は更新
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
            // 新規登録
            var insertCmd = db.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = @"
                INSERT INTO AppUsers (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
                VALUES (@userName, @passwordHash, @displayName, @language, @isAdmin, @isActive, @createdAt)";

            var passwordParam = insertCmd.CreateParameter();
            passwordParam.ParameterName = "@passwordHash";
            // 初期パスワードは従業員 ID を使用
            // PasswordHasher の既定実装は user 引数を参照しないため、ダミーインスタンスを渡す
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
/// YAML: afterCreate (employees) に指定します。
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
/// YAML: afterUpdate (employees) に指定します。
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
/// YAML: beforeCreate / beforeUpdate (employees) に指定します。
/// </summary>
public class ValidateEmployeeEmailHook : IEntityHook
{
    public string Name => "validate_employee_email";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Values.TryGetValue("email", out var emailObj) && emailObj is string email)
        {
            // 簡易的なメール形式検証
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
/// YAML: beforeCreate / beforeUpdate (employees) に指定します。
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
/// YAML: beforeCreate (employees) に指定します。
/// </summary>
public class ValidateEmployeeRegistrationHook : IEntityHook
{
    public string Name => "validate_employee_registration";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // user_name の一意性を検証
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

        // email の一意性を検証
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

// ===== 追加フック・アクションハンドラー統合 =====

public sealed class LinkGuestSessionsHook : IEntityHook
{
    private readonly ILogger<LinkGuestSessionsHook> _logger;
    public string Name => "link_guest_sessions";
    public LinkGuestSessionsHook(ILogger<LinkGuestSessionsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class SetCommunicationTimestampsHook : IEntityHook
{
    private readonly ILogger<SetCommunicationTimestampsHook> _logger;
    public string Name => "set_communication_timestamps";
    public SetCommunicationTimestampsHook(ILogger<SetCommunicationTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Data["created_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Task.FromResult(HookResult.Continue());
    }
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class UpdateCommunicationTimestampsHook : IEntityHook
{
    private readonly ILogger<UpdateCommunicationTimestampsHook> _logger;
    public string Name => "update_communication_timestamps";
    public UpdateCommunicationTimestampsHook(ILogger<UpdateCommunicationTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class SetDecisionTimestampsHook : IEntityHook
{
    private readonly ILogger<SetDecisionTimestampsHook> _logger;
    public string Name => "set_decision_timestamps";
    public SetDecisionTimestampsHook(ILogger<SetDecisionTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Data["created_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (string.IsNullOrEmpty(ctx.Data.GetValueOrDefault("decision_id")?.ToString()))
            ctx.Data["decision_id"] = "DEC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
        return Task.FromResult(HookResult.Continue());
    }
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class UpdateDecisionTimestampsHook : IEntityHook
{
    private readonly ILogger<UpdateDecisionTimestampsHook> _logger;
    public string Name => "update_decision_timestamps";
    public UpdateDecisionTimestampsHook(ILogger<UpdateDecisionTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class SetQuoteTimestampsHook : IEntityHook
{
    private readonly ILogger<SetQuoteTimestampsHook> _logger;
    public string Name => "set_quote_timestamps";
    public SetQuoteTimestampsHook(ILogger<SetQuoteTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Data["created_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ctx.Data["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Task.FromResult(HookResult.Continue());
    }
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class UpdateQuoteTimestampsHook : IEntityHook
{
    private readonly ILogger<UpdateQuoteTimestampsHook> _logger;
    public string Name => "update_quote_timestamps";
    public UpdateQuoteTimestampsHook(ILogger<UpdateQuoteTimestampsHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Data["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Task.FromResult(HookResult.Continue());
    }
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class SyncThirdPartyToAuthUserHook : IEntityHook
{
    private readonly ILogger<SyncThirdPartyToAuthUserHook> _logger;
    public string Name => "sync_third_party_to_auth_user";
    public SyncThirdPartyToAuthUserHook(ILogger<SyncThirdPartyToAuthUserHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class ValidateThirdPartyEmailHook : IEntityHook
{
    private readonly ILogger<ValidateThirdPartyEmailHook> _logger;
    public string Name => "validate_third_party_email";
    public ValidateThirdPartyEmailHook(ILogger<ValidateThirdPartyEmailHook> logger) => _logger = logger;
    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.FromResult(HookResult.Continue());
    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx) => Task.CompletedTask;
}

public sealed class ApproveAiDecisionHandler : ICustomActionHandler
{
    private readonly ILogger<ApproveAiDecisionHandler> _logger;
    public string Name => "approve_ai_decision";
    public ApproveAiDecisionHandler(ILogger<ApproveAiDecisionHandler> logger) => _logger = logger;
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("決定 ID が指定されていません。");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await db.ExecuteAsync("UPDATE ai_decisions SET status = 'approved', executed_at = @now WHERE decision_id = @id", new { now, id = ctx.RecordId }, tx);
        if (affected <= 0) return ActionHandlerResult.Failure("対象の AI 決定が見つかりません。");
        var decision = await db.QueryFirstOrDefaultAsync("SELECT entity_type, entity_id FROM ai_decisions WHERE decision_id = @id", new { id = ctx.RecordId }, tx) as IDictionary<string, object>;
        if (decision != null)
        {
            var entityType = decision.ContainsKey("entity_type") ? decision["entity_type"]?.ToString() : null;
            var entityId = decision.ContainsKey("entity_id") ? decision["entity_id"]?.ToString() : null;
            if (entityType == "ai_quotes") await db.ExecuteAsync("UPDATE ai_quotes SET status = 'approved' WHERE quote_id = @id", new { id = entityId }, tx);
            else if (entityType == "lead_nurturing_tasks") await db.ExecuteAsync("UPDATE lead_nurturing_tasks SET status = 'in_progress' WHERE task_id = @id", new { id = entityId }, tx);
        }
        return ActionHandlerResult.Success();
    }
}

public sealed class RejectAiDecisionHandler : ICustomActionHandler
{
    private readonly ILogger<RejectAiDecisionHandler> _logger;
    public string Name => "reject_ai_decision";
    public RejectAiDecisionHandler(ILogger<RejectAiDecisionHandler> logger) => _logger = logger;
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("決定 ID が指定されていません。");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var affected = await db.ExecuteAsync("UPDATE ai_decisions SET status = 'rejected', executed_at = @now WHERE decision_id = @id", new { now, id = ctx.RecordId }, tx);
        if (affected <= 0) return ActionHandlerResult.Failure("対象の AI 決定が見つかりません。");
        var decision = await db.QueryFirstOrDefaultAsync("SELECT entity_type, entity_id FROM ai_decisions WHERE decision_id = @id", new { id = ctx.RecordId }, tx) as IDictionary<string, object>;
        if (decision != null)
        {
            var entityType = decision.ContainsKey("entity_type") ? decision["entity_type"]?.ToString() : null;
            var entityId = decision.ContainsKey("entity_id") ? decision["entity_id"]?.ToString() : null;
            if (entityType == "ai_quotes") await db.ExecuteAsync("UPDATE ai_quotes SET status = 'rejected' WHERE quote_id = @id", new { id = entityId }, tx);
            else if (entityType == "lead_nurturing_tasks") await db.ExecuteAsync("UPDATE lead_nurturing_tasks SET status = 'cancelled' WHERE task_id = @id", new { id = entityId }, tx);
        }
        return ActionHandlerResult.Success();
    }
}

public sealed class ApproveQuoteHandler : ICustomActionHandler
{
    private readonly ILogger<ApproveQuoteHandler> _logger;
    public string Name => "approve_quote";
    public ApproveQuoteHandler(ILogger<ApproveQuoteHandler> logger) => _logger = logger;
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("見積 ID が指定されていません。");
        var affected = await db.ExecuteAsync("UPDATE ai_quotes SET status = 'approved' WHERE quote_id = @id", new { id = ctx.RecordId }, tx);
        return affected <= 0 ? ActionHandlerResult.Failure("対象の AI 見積が見つかりません。") : ActionHandlerResult.Success();
    }
}

public sealed class RejectQuoteHandler : ICustomActionHandler
{
    private readonly ILogger<RejectQuoteHandler> _logger;
    public string Name => "reject_quote";
    public RejectQuoteHandler(ILogger<RejectQuoteHandler> logger) => _logger = logger;
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("見積 ID が指定されていません。");
        var affected = await db.ExecuteAsync("UPDATE ai_quotes SET status = 'rejected' WHERE quote_id = @id", new { id = ctx.RecordId }, tx);
        return affected <= 0 ? ActionHandlerResult.Failure("対象の AI 見積が見つかりません。") : ActionHandlerResult.Success();
    }
}

public sealed class CompleteNurturingTaskHandler : ICustomActionHandler
{
    public string Name => "complete_nurturing_task";
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("No ID");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync("UPDATE lead_nurturing_tasks SET status = 'completed', completed_at = @now WHERE task_id = @id", new { now, id = ctx.RecordId }, tx);
        return ActionHandlerResult.Success();
    }
}

public sealed class CancelNurturingTaskHandler : ICustomActionHandler
{
    public string Name => "cancel_nurturing_task";
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("No ID");
        await db.ExecuteAsync("UPDATE lead_nurturing_tasks SET status = 'cancelled' WHERE task_id = @id", new { id = ctx.RecordId }, tx);
        return ActionHandlerResult.Success();
    }
}

public sealed class AssignHandoverToMeHandler : ICustomActionHandler
{
    public string Name => "assign_handover_to_me";
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("No ID");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync("UPDATE ai_handovers SET assigned_to_user_id = @user, status = 'in_progress', assigned_at = @now WHERE handover_id = @id",
            new { user = ctx.UserName ?? "system", now, id = ctx.RecordId }, tx);
        return ActionHandlerResult.Success();
    }
}

public sealed class ResolveHandoverHandler : ICustomActionHandler
{
    public string Name => "resolve_handover";
    public async Task<ActionHandlerResult> ExecuteAsync(CustomActionContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(ctx.RecordId)) return ActionHandlerResult.Failure("No ID");
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await db.ExecuteAsync("UPDATE ai_handovers SET status = 'resolved', resolved_at = @now WHERE handover_id = @id", new { now, id = ctx.RecordId }, tx);
        return ActionHandlerResult.Success();
    }
}
