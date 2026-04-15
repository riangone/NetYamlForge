// ファイル概要：jpiere-cs AI 関連フック処理
// AI 会話・メッセージ・引継ぎの前後処理を実装

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.ProjectHooks.JpiereCs;

/// <summary>
/// AI 会話データ検証フック
/// </summary>
public class ValidateAiConversationHook
{
public async Task ExecuteAsync(string hookType, IDictionary<string, object?> data, IDbConnection db)
{
// conversation_id フォーマット検証
if (data.TryGetValue("conversation_id", out var convId) && convId != null)
{
var convIdStr = convId.ToString() ?? "";
if (!convIdStr.StartsWith("CONV-"))
{
throw new ArgumentException("会話 ID は CONV- 形式で始まる必要があります。");
}
}

// sentiment_score 範囲検証
if (data.TryGetValue("sentiment_score", out var sentiment) && sentiment != null)
{
var score = Convert.ToDouble(sentiment);
if (score < -1.0 || score > 1.0)
{
throw new ArgumentException("感情スコアは -1.0 から 1.0 の範囲である必要があります。");
}
}

// last_confidence 範囲検証
if (data.TryGetValue("last_confidence", out var confidence) && confidence != null)
{
var confValue = Convert.ToDouble(confidence);
if (confValue < 0.0 || confValue > 1.0)
{
throw new ArgumentException("信頼度は 0.0 から 1.0 の範囲である必要があります。");
}
}

await Task.CompletedTask;
}
}

/// <summary>
/// AI 会話時間戳自動設定フック
/// </summary>
public class SetConversationTimestampsHook
{
public async Task ExecuteAsync(string hookType, IDictionary<string, object?> data, IDbConnection db)
{
var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

if (hookType == "beforeCreate")
{
data["started_at"] = now;
data["created_at"] = now;
data["updated_at"] = now;

if (!data.ContainsKey("status"))
{
data["status"] = "active";
}
if (!data.ContainsKey("message_count"))
{
data["message_count"] = 0;
}
if (!data.ContainsKey("escalation_count"))
{
data["escalation_count"] = 0;
}
}
else if (hookType == "beforeUpdate")
{
data["updated_at"] = now;
}

await Task.CompletedTask;
}
}

/// <summary>
/// AI メッセージ保存後処理 - エスカレーション自動検出
/// </summary>
public class AutoEscalationHook
{
private readonly double _sentimentThreshold = -0.5;

public async Task ExecuteAsync(string hookType, IDictionary<string, object?> data, IDbConnection db)
{
if (hookType != "afterCreate") return;

// メッセージの感情スコアを取得
if (!data.TryGetValue("sentiment_score", out var sentimentObj) || sentimentObj == null)
{
await Task.CompletedTask;
return;
}

var sentimentScore = Convert.ToDouble(sentimentObj);

// 感情スコアが閾値未満の場合、エスカレーションを提案
if (sentimentScore < _sentimentThreshold)
{
data.TryGetValue("conversation_id", out var convIdObj);
var conversationId = convIdObj?.ToString();
if (string.IsNullOrEmpty(conversationId))
{
await Task.CompletedTask;
return;
}

// 会話のステータスを escalated に更新
await db.ExecuteAsync(@"
UPDATE ai_conversations 
SET status = 'escalated', updated_at = @Now 
WHERE conversation_id = @Id",
new { Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), Id = conversationId });

// TODO: ai_handovers レコード作成は別の処理で実行
}

await Task.CompletedTask;
}
}

/// <summary>
/// AI 提案から自動 TODO 作成フック
/// </summary>
public class AutoCreateTodoFromAiHook
{
public async Task ExecuteAsync(string hookType, IDictionary<string, object?> data, IDbConnection db)
{
if (hookType != "afterCreate") return;

// AI 会話から TODO を自動生成するロジック
// 例：有効期限切れ契約の更新TODO、未請求の請求書作成TODO など

data.TryGetValue("conversation_id", out var convIdObj);
data.TryGetValue("user_id", out var userIdObj);
data.TryGetValue("user_role", out var userRoleObj);
data.TryGetValue("last_intent", out var lastIntentObj);

var conversationId = convIdObj?.ToString();
var userId = userIdObj?.ToString();
var userRole = userRoleObj?.ToString();
var lastIntent = lastIntentObj?.ToString();

if (string.IsNullOrEmpty(conversationId) || string.IsNullOrEmpty(userId))
{
await Task.CompletedTask;
return;
}

// 特定の意図に応じて TODO を自動作成
if (lastIntent == "contract_expiry_alert" || lastIntent == "unbilled_contract")
{
var todoId = $"TODO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
var title = lastIntent == "contract_expiry_alert" ? "契約更新確認" : "請求書作成";

await db.ExecuteAsync(@"
INSERT INTO todos (todo_id, title, status, priority, assigned_to, created_at, updated_at)
VALUES (@TodoId, @Title, 'OPEN', 'MEDIUM', @UserId, @Now, @Now)",
new { TodoId = todoId, Title = title, UserId = userId, Now = now });
}

await Task.CompletedTask;
}
}

/// <summary>
/// AI 会話を業務エンティティに関連付けフック
/// </summary>
public class LinkAiToBusinessEntityHook
{
public async Task ExecuteAsync(string hookType, IDictionary<string, object?> data, IDbConnection db)
{
if (hookType != "afterCreate" && hookType != "afterUpdate") return;

// 会話内容から業務エンティティを自動検出・関連付け
// 例：メッセージ内に契約番号が含まれていれば linked_contract_id を設定

data.TryGetValue("conversation_id", out var convIdObj);
var conversationId = convIdObj?.ToString();
if (string.IsNullOrEmpty(conversationId))
{
await Task.CompletedTask;
return;
}

// メッセージ内容を取得
var messages = await db.QueryAsync<string>(@"
SELECT content FROM ai_messages 
WHERE conversation_id = @Id 
ORDER BY sent_at DESC 
LIMIT 5",
new { Id = conversationId });

foreach (var content in messages)
{
// 契約番号パターン (CON-YYYYMM-XXXX)
var contractMatch = System.Text.RegularExpressions.Regex.Match(content, @"CON-\d{6}-\d+");
if (contractMatch.Success && data.ContainsKey("linked_contract_id"))
{
var contractNo = contractMatch.Value;
var contract = await db.QueryFirstOrDefaultAsync<IDictionary<string, object?>>(@"
SELECT id FROM contracts WHERE contract_no = @ContractNo",
new { ContractNo = contractNo });

if (contract != null && contract.TryGetValue("id", out var cid))
{
data["linked_contract_id"] = cid;
break;
}
}

// 請求番号パターン (BILL-YYYYMM-XXXX)
var billMatch = System.Text.RegularExpressions.Regex.Match(content, @"BILL-\d{6}-\d+");
if (billMatch.Success && data.ContainsKey("linked_bill_id"))
{
var billNo = billMatch.Value;
var bill = await db.QueryFirstOrDefaultAsync<IDictionary<string, object?>>(@"
SELECT id FROM bills WHERE bill_no = @BillNo",
new { BillNo = billNo });

if (bill != null && bill.TryGetValue("id", out var bid))
{
data["linked_bill_id"] = bid;
break;
}
}
}

await Task.CompletedTask;
}
}

/// <summary>
/// 感情トレンド更新フック
/// </summary>
public class UpdateSentimentTrendHook
{
public async Task ExecuteAsync(string hookType, IDictionary<string, object?> data, IDbConnection db)
{
if (hookType != "afterUpdate") return;

data.TryGetValue("conversation_id", out var convIdObj);
var conversationId = convIdObj?.ToString();
if (string.IsNullOrEmpty(conversationId))
{
await Task.CompletedTask;
return;
}

// 会話の平均感情スコアを計算
var avgSentiment = await db.QueryFirstOrDefaultAsync<double>(@"
SELECT AVG(sentiment_score) FROM ai_messages 
WHERE conversation_id = @Id",
new { Id = conversationId });

// 会話レコードの感情スコアを更新
await db.ExecuteAsync(@"
UPDATE ai_conversations 
SET sentiment_score = @AvgSentiment, updated_at = @Now 
WHERE conversation_id = @Id",
new { AvgSentiment = avgSentiment, Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), Id = conversationId });

await Task.CompletedTask;
}
}

/// <summary>
/// AI 引継ぎ自動割り当てフック
/// </summary>
public class AutoAssignHandoverHook
{
public async Task ExecuteAsync(string hookType, IDictionary<string, object?> data, IDbConnection db)
{
if (hookType != "afterCreate") return;

data.TryGetValue("handover_id", out var handoverIdObj);
data.TryGetValue("target_department", out var targetDeptObj);
data.TryGetValue("priority", out var priorityObj);

var handoverId = handoverIdObj?.ToString();
var targetDept = targetDeptObj?.ToString();
var priority = priorityObj?.ToString();

if (string.IsNullOrEmpty(handoverId) || string.IsNullOrEmpty(targetDept))
{
await Task.CompletedTask;
return;
}

// 部門に応じて担当者を自動割り当て（簡易版）
var assignedTo = targetDept switch
{
"contract" => "contract_manager_01",
"accounting" => "accountant_01",
"purchasing" => "purchaser_01",
"management" => "admin_01",
"support" => "employee_01",
_ => "admin_01"
};

var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

await db.ExecuteAsync(@"
UPDATE ai_handovers 
SET assigned_to = @AssignedTo, assigned_at = @Now, status = 'assigned'
WHERE handover_id = @HId AND status = 'pending'",
new { AssignedTo = assignedTo, Now = now, HId = handoverId });

await Task.CompletedTask;
}
}

/// <summary>
/// 引継ぎ解決指標更新フック
/// </summary>
public class UpdateResolutionMetricsHook
{
public async Task ExecuteAsync(string hookType, IDictionary<string, object?> data, IDbConnection db)
{
if (hookType != "afterUpdate") return;

data.TryGetValue("handover_id", out var handoverIdObj);
data.TryGetValue("status", out var statusObj);

var handoverId = handoverIdObj?.ToString();
var status = statusObj?.ToString();

if (string.IsNullOrEmpty(handoverId) || status != "completed")
{
await Task.CompletedTask;
return;
}

var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

// 解決時間を計算
var handover = await db.QueryFirstOrDefaultAsync<IDictionary<string, object?>>(@"
SELECT created_at FROM ai_handovers WHERE handover_id = @HId",
new { HId = handoverId });

if (handover != null)
{
var createdAtVal = handover.TryGetValue("created_at", out var cat) ? cat?.ToString() : null;
if (!string.IsNullOrEmpty(createdAtVal) && DateTime.TryParse(createdAtVal, out var createdAt))
{
var resolvedAt = DateTime.Parse(now);
var resolutionMinutes = (int)(resolvedAt - createdAt).TotalMinutes;

await db.ExecuteAsync(@"
UPDATE ai_handovers
SET completed_at = @Now, resolution_time_minutes = @Minutes
WHERE handover_id = @HId",
new { Now = now, Minutes = resolutionMinutes, HId = handoverId });
}
}

await Task.CompletedTask;
}
}



/// <summary>
/// AI 会話検証フック（EntityHook 互換ラッパー）。
/// entities/ai_conversations.yml の hooks.beforeCreate / beforeUpdate で利用します。
/// </summary>
public class ValidateAiConversationEntityHook : IEntityHook
{
    private readonly ValidateAiConversationHook _inner = new();

    public string Name => "validate_ai_conversation";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => _inner.ExecuteAsync(ctx.Operation == CrudOperation.Create ? "beforeCreate" : "beforeUpdate", ctx.Values, db)
            .ContinueWith(t => t.IsFaulted ? HookResult.Abort(t.Exception?.GetBaseException().Message ?? "validation failed") : HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// AI 会話タイムスタンプ設定フック（EntityHook 互換ラッパー）。
/// </summary>
public class SetConversationTimestampsEntityHook : IEntityHook
{
    private readonly SetConversationTimestampsHook _inner = new();

    public string Name => "set_conversation_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => _inner.ExecuteAsync(ctx.Operation == CrudOperation.Create ? "beforeCreate" : "beforeUpdate", ctx.Values, db)
            .ContinueWith(t => t.IsFaulted ? HookResult.Abort(t.Exception?.GetBaseException().Message ?? "timestamp update failed") : HookResult.Continue());

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// AI 会話更新日時更新フック（EntityHook 互換ラッパー）。
/// </summary>
public class UpdateConversationUpdatedAtEntityHook : IEntityHook
{
    private readonly SetConversationTimestampsHook _inner = new();

    public string Name => "update_conversation_updated_at";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Update)
        {
            return Task.FromResult(HookResult.Continue());
        }

        return _inner.ExecuteAsync("beforeUpdate", ctx.Values, db)
            .ContinueWith(t => t.IsFaulted ? HookResult.Abort(t.Exception?.GetBaseException().Message ?? "updated_at refresh failed") : HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

