// auto-dealer-demo プロジェクト固有フック
// 顧客・AI 会話・サービス予約の入力検証・自動補完フックの実装です。
//
// YAML での参照例:
//   customers.yml:
//     hooks:
//       beforeCreate: [validate_customer_phone, normalize_customer_name]
//       beforeUpdate: [validate_customer_phone, normalize_customer_name]
//   ai_conversations.yml:
//     hooks:
//       beforeCreate: [validate_ai_conversation, set_conversation_timestamps]
//       beforeUpdate: [validate_ai_conversation, update_conversation_updated_at]
//   service_appointments.yml:
//     hooks:
//       beforeCreate: [validate_appointment_date, validate_appointment_time]
//       beforeUpdate: [validate_appointment_date]

using System;
using System.Data;
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;

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
/// AI 会話のチャネル・ステータス・置信度を検証するフック。
/// YAML: beforeCreate / beforeUpdate に指定します。
/// </summary>
public class ValidateAiConversationHook : IEntityHook
{
    public string Name => "validate_ai_conversation";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // チャネルの検証
        if (ctx.Values.TryGetValue("channel", out var channel) && channel is string channelStr)
        {
            var validChannels = new[] { "web", "voice", "line", "email", "sms", "tablet" };
            if (!Array.Exists(validChannels, t => t == channelStr))
                return Task.FromResult(HookResult.Abort("チャネルは無効な値です。"));
        }

        // ステータスの検証
        if (ctx.Values.TryGetValue("status", out var status) && status is string statusStr)
        {
            var validStatuses = new[] { "active", "completed", "escalated", "abandoned" };
            if (!Array.Exists(validStatuses, t => t == statusStr))
                return Task.FromResult(HookResult.Abort("ステータスは無効な値です。"));
        }

        // 置信度の範囲チェック (0-1)
        if (ctx.Values.TryGetValue("last_confidence", out var confidence) && confidence != null)
        {
            if (decimal.TryParse(confidence.ToString(), out var conf) && (conf < 0 || conf > 1))
                return Task.FromResult(HookResult.Abort("置信度は 0 から 1 の範囲である必要があります。"));
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// AI 会話作成時に started_at・created_at・updated_at を自動設定するフック。
/// YAML: beforeCreate に指定します。
/// </summary>
public class SetConversationTimestampsHook : IEntityHook
{
    public string Name => "set_conversation_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        if (!ctx.Values.ContainsKey("started_at") || ctx.Values["started_at"] == null)
            ctx.Values["started_at"] = now;
        
        if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            ctx.Values["created_at"] = now;
        
        if (!ctx.Values.ContainsKey("updated_at") || ctx.Values["updated_at"] == null)
            ctx.Values["updated_at"] = now;

        if (!ctx.Values.ContainsKey("status") || ctx.Values["status"] == null)
            ctx.Values["status"] = "active";

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// AI 会話更新時に updated_at を自動更新するフック。
/// YAML: beforeUpdate に指定します。
/// </summary>
public class UpdateConversationUpdatedAtHook : IEntityHook
{
    public string Name => "update_conversation_updated_at";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        ctx.Values["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
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
