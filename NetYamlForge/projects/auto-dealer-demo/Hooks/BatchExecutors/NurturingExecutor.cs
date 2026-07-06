using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

public class NurturingItem
{
    public dynamic Lead { get; set; } = null!;
    public long StartMs { get; set; }
}

public class NurturingResult
{
    public string ActionType { get; set; } = "";
    public string TaskTitle { get; set; } = "";
    public string MessageDraft { get; set; } = "";
    public double ConfidenceScore { get; set; }
    public string Reasoning { get; set; } = "";
}

public class NurturingExecutor : ProjectBatchExecutorBase<NurturingItem, NurturingResult>
{
    public override string StepType => "nurturing";

    public NurturingExecutor(ICliChainService cli, ILogger logger) : base(cli, logger)
    {
    }

    protected override Task<NurturingItem?> LoadInputAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct) => Task.FromResult<NurturingItem?>(null);

    protected override async Task<IReadOnlyList<NurturingItem>> LoadItemsAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var maxItems = int.TryParse(job.Settings.Params?.GetValueOrDefault("maxItems"), out var m) ? m : 5;
        var leads = (await db.QueryAsync(@"
            SELECT sl.lead_id, sl.customer_id,
                   c.name AS customer_name,
                   sl.vehicle_interest, sl.budget, sl.status, sl.lead_score,
                   sl.last_contact_at,
                   c.phone, c.email
            FROM sales_leads sl
            LEFT JOIN customers c ON sl.customer_id = c.customer_id
            WHERE sl.lead_score BETWEEN 30 AND 79
              AND (sl.last_contact_at IS NULL OR sl.last_contact_at <= datetime('now', '-3 days'))
              AND sl.status NOT IN ('closed_won', 'closed_lost')
            ORDER BY sl.lead_score DESC, sl.last_contact_at ASC
            LIMIT @Max", new { Max = maxItems }, tx)).ToList();

        return leads.Select(l => new NurturingItem
        {
            Lead = l,
            StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }).ToList();
    }

    protected override string BuildPrompt(NurturingItem input)
    {
        var lead = input.Lead;
        return $@"
        あなたはお客さま対応のAIコンサルタントです。以下の顧客に最適な育成アクションを提案してください。

        【顧客情報】
        - 顧客名: {lead.customer_name}
        - 関心車種: {lead.vehicle_interest}
        - 予算: {lead.budget ?? "未記載"}
        - リードスコア: {lead.lead_score ?? 0}
        - 最終接触日: {lead.last_contact_at ?? "なし"}

        【利用可能なアクション】
        - phone_call: 電話フォローアップ
        - email: メール送信
        - sms: SMS送信
        - appointment: 来店予約の案内
        - test_drive: 試乗の提案
        - catalog: カタログ・資料送付

        以下のJSON形式のみで回答してください:
        {{
          ""actionType"": ""アクション種別（上記から1つ）"",
          ""taskTitle"": ""タスクタイトル（日本語、30文字以内）"",
          ""messageDraft"": ""顧客へのメッセージ案（日本語、200文字以内）"",
          ""confidenceScore"": 数値(0-100),
          ""reasoning"": ""このアクションを選んだ理由（日本語、100文字以内）""
        }}";
    }

    protected override NurturingResult? ParseResult(string raw)
    {
        if (TryParseJson(raw, out NurturingResult? result, out var error))
        {
            return result;
        }
        Logger.LogWarning("Failed to parse NurturingResult: {Error}", error);
        return null;
    }

    protected override async Task PersistAsync(
        NurturingItem input, NurturingResult aiResult,
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var lead = input.Lead;
        var autoExecuteThreshold = double.TryParse(job.Settings.Params?.GetValueOrDefault("autoExecuteThreshold"), out var a) ? a : 90.0;
        var decisionId = $"NT-{lead.lead_id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var requiresHuman = aiResult.ConfidenceScore < autoExecuteThreshold;
        var status = requiresHuman ? "pending" : "auto_executed";

        await db.ExecuteAsync(@"
            INSERT OR IGNORE INTO ai_decisions (
                decision_id, decision_type, entity_type, entity_id,
                ai_reasoning, confidence_score, status, requires_human,
                executed_at, created_at
            ) VALUES (
                @DecisionId, 'nurturing_task', 'sales_leads', @LeadId,
                @Reasoning, @Confidence, @Status, @RequiresHuman,
                @ExecutedAt, @Now
            )", new
        {
            DecisionId = decisionId,
            LeadId = (string?)lead.lead_id,
            Reasoning = $"[推奨: {aiResult.ActionType}] {aiResult.Reasoning}",
            Confidence = aiResult.ConfidenceScore,
            Status = status,
            RequiresHuman = requiresHuman ? 1 : 0,
            ExecutedAt = status == "auto_executed" ? (DateTime?)DateTime.UtcNow : null,
            Now = DateTime.UtcNow
        }, tx);

        if (status == "auto_executed")
        {
            var taskId = $"LNT-{lead.lead_id}-{DateTime.UtcNow:yyyyMMddHHmm}";
            var priorityScore = lead.lead_score >= 60 ? 80 : 50;

            await db.ExecuteAsync(@"
                INSERT OR IGNORE INTO lead_nurturing_tasks (
                    task_id, lead_id, customer_id,
                    task_type, trigger_reason, ai_recommendation, ai_reasoning,
                    priority_score, status, due_date,
                    created_at, updated_at
                ) VALUES (
                    @TaskId, @LeadId, @CustomerId,
                    @TaskType, @TriggerReason, @AiRecommendation, @AiReasoning,
                    @PriorityScore, 'pending', date('now', '+3 days'),
                    @Now, @Now
                )", new
            {
                TaskId = taskId,
                LeadId = (string?)lead.lead_id,
                CustomerId = (string?)lead.customer_id,
                TaskType = aiResult.ActionType,
                TriggerReason = $"スコア {lead.lead_score}・最終接触: {lead.last_contact_at ?? "なし"}",
                AiRecommendation = aiResult.TaskTitle,
                AiReasoning = aiResult.Reasoning,
                PriorityScore = priorityScore,
                Now = DateTime.UtcNow
            }, tx);

            await db.ExecuteAsync(@"
                UPDATE sales_leads
                SET ai_touch_count = COALESCE(ai_touch_count, 0) + 1,
                    updated_at = @Now
                WHERE lead_id = @LeadId",
                new { Now = DateTime.UtcNow, LeadId = (string?)lead.lead_id }, tx);
        }

        var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - input.StartMs;
        await LogActionAsync(db, tx, "nurturing_created", "sales_leads", (string?)lead.lead_id,
            "antigravity", $"育成タスク生成: {lead.customer_name}",
            $"{aiResult.ActionType}: {aiResult.TaskTitle}",
            (int)elapsedMs);
    }

    private static async Task LogActionAsync(
        IDbConnection db, IDbTransaction tx,
        string actionType, string entityType, string? entityId,
        string aiModel, string promptSummary, string resultSummary, int executionMs)
    {
        await db.ExecuteAsync(@"
            INSERT INTO ai_action_log (
                log_id, action_type, entity_type, entity_id,
                ai_model, prompt_summary, result_summary,
                execution_ms, created_at
            ) VALUES (
                'LOG-' || lower(hex(randomblob(6))),
                @ActionType, @EntityType, @EntityId,
                @AiModel, @PromptSummary, @ResultSummary,
                @ExecutionMs, @Now
            )", new
        {
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId,
            AiModel = aiModel,
            PromptSummary = promptSummary,
            ResultSummary = resultSummary,
            ExecutionMs = executionMs,
            Now = DateTime.UtcNow
        }, tx);
    }
}
