// auto-dealer-demo プロジェクト固有フック
// 顧客・AI 会話・サービス予約の入力検証・自動補完フックの実装です。
//
// YAML での参照例:
//   customers.yml:
//     hooks:
//       beforeCreate: [validate_customer_phone, normalize_customer_name, validate_customer_registration]
//       beforeUpdate: [validate_customer_phone, normalize_customer_name]
//       afterCreate: [sync_customer_to_auth_user]
//   ai_conversations.yml:
//     hooks:
//       beforeCreate: [validate_ai_conversation, set_conversation_timestamps]
//       afterCreate:  [auto_create_lead_from_conversation, sentiment_auto_escalation]
//       beforeUpdate: [validate_ai_conversation, update_conversation_updated_at]
//       afterUpdate:  [auto_create_lead_from_conversation, sentiment_auto_escalation]
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
using System.Threading.Tasks;
using NetYamlForge.Services.Hooks;
using Microsoft.AspNetCore.Identity;
using NetYamlForge.Models.Auth;
using Microsoft.Extensions.Logging;

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

// ─────────────────────────────────────────────────────────────────────────────
// 以下: AI 会話 → セールスリード自動連携フック
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AI 会話が完了ステータスになったとき、販売に関連する意図を持つ会話から
/// sales_leads レコードを自動生成するフック。
/// YAML: afterCreate / afterUpdate (ai_conversations) に指定します。
/// </summary>
public class AutoCreateLeadFromConversationHook : IEntityHook
{
    public string Name => "auto_create_lead_from_conversation";

    // 販売意図として判定するインテント名の集合
    private static readonly HashSet<string> SalesIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "price_inquiry", "vehicle_inquiry", "test_drive_request",
        "financing_inquiry", "trade_in_inquiry", "visit_intent",
        "quote_request", "new_car_inquiry"
    };

    // 意図ごとの基本スコア
    private static int BaseScore(string intent) => intent switch
    {
        "test_drive_request"  => 65,
        "visit_intent"        => 70,
        "quote_request"       => 60,
        "financing_inquiry"   => 58,
        "price_inquiry"       => 55,
        "trade_in_inquiry"    => 50,
        "vehicle_inquiry"     => 45,
        "new_car_inquiry"     => 45,
        _                     => 40,
    };

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 完了ステータス以外は無視
        if (!ctx.Values.TryGetValue("status", out var status) || status?.ToString() != "completed")
            return Task.CompletedTask;

        if (!ctx.Values.TryGetValue("last_intent", out var intentVal) || intentVal == null)
            return Task.CompletedTask;

        var intentStr = intentVal.ToString()!;
        if (!SalesIntents.Contains(intentStr))
            return Task.CompletedTask;

        ctx.Values.TryGetValue("conversation_id", out var convId);
        ctx.Values.TryGetValue("customer_id", out var customerId);
        ctx.Values.TryGetValue("last_confidence", out var confidenceVal);
        ctx.Values.TryGetValue("sentiment_score", out var sentimentVal);
        ctx.Values.TryGetValue("channel", out var channelVal);
        ctx.Values.TryGetValue("contact_info", out var contactInfoVal);

        // 会話 ID 必須
        if (convId == null) return Task.CompletedTask;

        // 同一会話からのリードが既に存在する場合はスキップ
        var checkCmd = db.CreateCommand();
        checkCmd.Transaction = tx;
        checkCmd.CommandText = "SELECT COUNT(*) FROM sales_leads WHERE source_conversation_id = @cid";
        AddParam(checkCmd, "@cid", convId.ToString()!);
        var existing = Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0L);
        if (existing > 0) return Task.CompletedTask;

        // リードスコア計算
        var score = BaseScore(intentStr);
        if (decimal.TryParse(confidenceVal?.ToString(), out var conf))
            score = (int)(score * conf);
        if (decimal.TryParse(sentimentVal?.ToString(), out var sent) && sent > 0)
            score = Math.Min(100, score + (int)(sent * 10));

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var leadId = $"LEAD-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        // 顧客 ID が NULL の場合（ゲスト）、contact_info から連絡先を抽出してリード作成
        // ゲストリードとして customer_id を NULL のまま保存し、後で紐付け可能にする
        object? customerIdValue = customerId?.ToString();
        string leadSource = customerIdValue != null ? "ai_conversation" : "guest_ai";

        var insertCmd = db.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = @"
            INSERT OR IGNORE INTO sales_leads
                (lead_id, customer_id, vehicle_interest, lead_score, status,
                 source_conversation_id, lead_source, created_at, updated_at)
            VALUES (@leadId, @cust, @intent, @score, 'new', @convId, @source, @now, @now)";
        AddParam(insertCmd, "@leadId", leadId);
        AddParam(insertCmd, "@cust",   customerIdValue);
        AddParam(insertCmd, "@intent", intentStr);
        AddParam(insertCmd, "@score",  score);
        AddParam(insertCmd, "@convId", convId.ToString()!);
        AddParam(insertCmd, "@source", leadSource);
        AddParam(insertCmd, "@now",    now);
        insertCmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private static void AddParam(IDbCommand cmd, string name, object? val)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = val ?? (object)DBNull.Value;
        cmd.Parameters.Add(p);
    }
}

/// <summary>
/// 感情スコアが -0.5 以下に下がった場合、自動で ai_handovers にエスカレーション
/// レコードを生成するフック。
/// YAML: afterCreate / afterUpdate (ai_conversations) に指定します。
/// </summary>
public class SentimentEscalationHook : IEntityHook
{
    public string Name => "sentiment_auto_escalation";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 感情スコアが閾値を超えない場合はスキップ
        if (!ctx.Values.TryGetValue("sentiment_score", out var sentimentVal) || sentimentVal == null)
            return Task.CompletedTask;
        if (!decimal.TryParse(sentimentVal.ToString(), out var sentiment) || sentiment >= -0.5m)
            return Task.CompletedTask;

        // 既に escalated / completed の会話はスキップ
        ctx.Values.TryGetValue("status", out var status);
        var statusStr = status?.ToString();
        if (statusStr == "escalated" || statusStr == "completed")
            return Task.CompletedTask;

        ctx.Values.TryGetValue("conversation_id", out var convId);
        if (convId == null) return Task.CompletedTask;

        // 有効なエスカレーションが既に存在する場合はスキップ
        var checkCmd = db.CreateCommand();
        checkCmd.Transaction = tx;
        checkCmd.CommandText = @"
            SELECT COUNT(*) FROM ai_handovers
            WHERE conversation_id = @cid AND status IN ('pending','assigned','in_progress')";
        AddParam(checkCmd, "@cid", convId.ToString()!);
        var existing = Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0L);
        if (existing > 0) return Task.CompletedTask;

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var handoverId = $"ESC-AUTO-{DateTime.Now:yyyyMMddHHmmss}";
        var notes = $"感情スコア {sentiment:F2} を検出。ネガティブ感情による自動エスカレーションです。";

        var insertCmd = db.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = @"
            INSERT OR IGNORE INTO ai_handovers
                (handover_id, conversation_id, reason, priority,
                 target_department, status, handover_notes, escalated_at, created_at)
            VALUES (@id, @cid, 'negative_sentiment', 'high',
                    'general', 'pending', @notes, @now, @now)";
        AddParam(insertCmd, "@id",    handoverId);
        AddParam(insertCmd, "@cid",   convId.ToString()!);
        AddParam(insertCmd, "@notes", notes);
        AddParam(insertCmd, "@now",   now);
        insertCmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private static void AddParam(IDbCommand cmd, string name, object? val)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = val ?? (object)DBNull.Value;
        cmd.Parameters.Add(p);
    }
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

// ─────────────────────────────────────────────────────────────────────────────
// 以下：顧客登録関連フック
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 顧客登録後に、ゲストセッション ID を持つ AI 会話と sales_leads を顧客に紐付けるフック。
/// YAML: afterCreate (customers) に指定します。
/// </summary>
public class LinkGuestSessionsHook : IEntityHook
{
    public string Name => "link_guest_sessions";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 顧客 ID を取得
        if (!ctx.Values.TryGetValue("customer_id", out var customerIdVal) || customerIdVal == null)
            return Task.CompletedTask;

        var customerId = customerIdVal.ToString()!;
        if (string.IsNullOrWhiteSpace(customerId))
            return Task.CompletedTask;

        // user_name または email からゲストセッション ID を特定
        // 方法 1: 同じトランザクションで guest_session_id が渡されている場合
        ctx.Values.TryGetValue("guest_session_id", out var guestSessionIdVal);
        
        // 方法 2: 最近のゲストセッションを検索（過去 30 分以内）
        var fallbackSessionIds = new List<string>();
        
        if (guestSessionIdVal != null && !string.IsNullOrWhiteSpace(guestSessionIdVal.ToString()))
        {
            fallbackSessionIds.Add(guestSessionIdVal.ToString()!);
        }
        else
        {
            // 直近 30 分以内に作成されたゲストセッション（customer_id が NULL）を検索
            var recentCmd = db.CreateCommand();
            recentCmd.Transaction = tx;
            recentCmd.CommandText = @"
                SELECT conversation_id 
                FROM ai_conversations 
                WHERE customer_id IS NULL 
                  AND guest_session_id IS NOT NULL
                  AND created_at >= datetime('now', '-30 minutes')
                ORDER BY created_at DESC";
            
            using var reader = recentCmd.ExecuteReader();
            while (reader.Read())
            {
                fallbackSessionIds.Add(reader.GetString(0));
            }
            reader.Close();
        }

        // 見つかったゲストセッションを顧客に紐付け
        var linkedCount = 0;
        foreach (var sessionId in fallbackSessionIds)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) continue;

            var updateCmd = db.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = @"
                UPDATE ai_conversations 
                SET customer_id = @CustomerId, updated_at = datetime('now', 'localtime')
                WHERE conversation_id = @SessionId AND customer_id IS NULL";
            
            var param1 = updateCmd.CreateParameter();
            param1.ParameterName = "@CustomerId";
            param1.Value = customerId;
            updateCmd.Parameters.Add(param1);
            
            var param2 = updateCmd.CreateParameter();
            param2.ParameterName = "@SessionId";
            param2.Value = sessionId;
            updateCmd.Parameters.Add(param2);
            
            linkedCount += updateCmd.ExecuteNonQuery();
        }

        // 紐付いた会話から sales_leads も更新
        if (linkedCount > 0)
        {
            var updateLeadsCmd = db.CreateCommand();
            updateLeadsCmd.Transaction = tx;
            updateLeadsCmd.CommandText = @"
                UPDATE sales_leads 
                SET customer_id = @CustomerId, 
                    lead_source = CASE WHEN lead_source = 'guest_ai' THEN 'ai_conversation' ELSE lead_source END,
                    updated_at = datetime('now', 'localtime')
                WHERE customer_id IS NULL 
                  AND source_conversation_id IN (
                      SELECT conversation_id 
                      FROM ai_conversations 
                      WHERE customer_id = @CustomerId
                  )";
            
            var param = updateLeadsCmd.CreateParameter();
            param.ParameterName = "@CustomerId";
            param.Value = customerId;
            updateLeadsCmd.Parameters.Add(param);
            
            updateLeadsCmd.ExecuteNonQuery();
        }

        _logger?.LogInformation(
            "顧客 {CustomerId} に {LinkedCount} 件のゲストセッションと関連リードを紐付けました",
            customerId, linkedCount);

        return Task.CompletedTask;
    }

    private ILogger? _logger;
    
    public LinkGuestSessionsHook(ILogger<LinkGuestSessionsHook>? logger = null)
    {
        _logger = logger;
    }
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
        var passwordHasher = new PasswordHasher<AppUser>();
        passwordParam.Value = passwordHasher.HashPassword(null, customerId);
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
            var passwordHasher = new PasswordHasher<AppUser>();
            passwordParam.Value = passwordHasher.HashPassword(null, employeeId);
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
