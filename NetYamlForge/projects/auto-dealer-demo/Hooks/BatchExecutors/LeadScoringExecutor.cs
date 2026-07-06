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

public class LeadScoringItem
{
    public dynamic Lead { get; set; } = null!;
    public long StartMs { get; set; }
}

public class LeadScoringResult
{
    public int NewScore { get; set; }
    public double ConfidenceScore { get; set; }
    public string Reasoning { get; set; } = "";
    public string RecommendedAction { get; set; } = "";
}

public class LeadScoringExecutor : ProjectBatchExecutorBase<LeadScoringItem, LeadScoringResult>
{
    public override string StepType => "lead_scoring";

    public LeadScoringExecutor(ICliChainService cli, ILogger logger) : base(cli, logger)
    {
    }

    protected override Task<LeadScoringItem?> LoadInputAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct) => Task.FromResult<LeadScoringItem?>(null);

    protected override async Task<IReadOnlyList<LeadScoringItem>> LoadItemsAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var maxItems = int.TryParse(job.Settings.Params?.GetValueOrDefault("maxItems"), out var m) ? m : 5;
        var leads = (await db.QueryAsync(@"
            SELECT sl.lead_id, sl.customer_id,
                   c.name AS customer_name,
                   sl.vehicle_interest, sl.budget, sl.status, sl.lead_score,
                   sl.last_contact_at, sl.lead_source AS source
            FROM sales_leads sl
            LEFT JOIN customers c ON sl.customer_id = c.customer_id
            WHERE (sl.last_contact_at IS NULL OR sl.last_contact_at <= datetime('now', '-7 days'))
              AND sl.status NOT IN ('closed_won', 'closed_lost')
            ORDER BY sl.last_contact_at ASC
            LIMIT @Max", new { Max = maxItems }, tx)).ToList();

        return leads.Select(l => new LeadScoringItem
        {
            Lead = l,
            StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }).ToList();
    }

    protected override string BuildPrompt(LeadScoringItem input)
    {
        var lead = input.Lead;
        return $@"
        あなたは自動車販売のAIアナリストです。以下の顧客リード情報を分析し、リードスコアを算出してください。

        【リード情報】
        - 顧客名: {lead.customer_name}
        - 関心車種: {lead.vehicle_interest}
        - 予算: {lead.budget ?? "未記載"}
        - 現在のステータス: {lead.status}
        - 現在のスコア: {lead.lead_score ?? 0}
        - 最終接触日: {lead.last_contact_at ?? "なし"}
        - 購入源: {lead.source ?? "不明"}

        【スコアリング基準】
        - 0-30: 冷却中（連絡が途絶えている、予算不明）
        - 31-60: 育成中（関心はあるが決断に時間がかかる）
        - 61-80: 積極的（商談進行中、比較検討段階）
        - 81-100: 購入直前（商談成立が見込める）

        以下のJSON形式のみで回答してください:
        {{
          ""newScore"": 数値(0-100),
          ""confidenceScore"": 数値(0-100),
          ""reasoning"": ""スコア変更理由（日本語、100文字以内）"",
          ""recommendedAction"": ""次の推奨アクション（日本語、50文字以内）""
        }}";
    }

    protected override LeadScoringResult? ParseResult(string raw)
    {
        if (TryParseJson(raw, out LeadScoringResult? result, out var error))
        {
            return result;
        }
        Logger.LogWarning("Failed to parse LeadScoringResult: {Error}", error);
        return null;
    }

    protected override async Task PersistAsync(
        LeadScoringItem input, LeadScoringResult aiResult,
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var lead = input.Lead;
        var autoExecuteThreshold = double.TryParse(job.Settings.Params?.GetValueOrDefault("autoExecuteThreshold"), out var a) ? a : 90.0;
        var decisionId = $"LS-{lead.lead_id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var requiresHuman = aiResult.ConfidenceScore < autoExecuteThreshold
            || aiResult.NewScore >= 80
            || ParseBudget(lead.budget?.ToString()) >= 5_000_000m;

        var status = requiresHuman ? "pending" : "auto_executed";

        await db.ExecuteAsync(@"
            INSERT OR IGNORE INTO ai_decisions (
                decision_id, decision_type, entity_type, entity_id,
                ai_reasoning, confidence_score, status, requires_human,
                executed_at, created_at
            ) VALUES (
                @DecisionId, 'lead_scoring', 'sales_leads', @LeadId,
                @Reasoning, @Confidence, @Status, @RequiresHuman,
                @ExecutedAt, @Now
            )", new
        {
            DecisionId = decisionId,
            LeadId = (string?)lead.lead_id,
            Reasoning = $"[スコア: {lead.lead_score ?? 0} → {aiResult.NewScore}] {aiResult.Reasoning}",
            Confidence = aiResult.ConfidenceScore,
            Status = status,
            RequiresHuman = requiresHuman ? 1 : 0,
            ExecutedAt = status == "auto_executed" ? (DateTime?)DateTime.UtcNow : null,
            Now = DateTime.UtcNow
        }, tx);

        if (status == "auto_executed")
        {
            await db.ExecuteAsync(@"
                UPDATE sales_leads
                SET lead_score = @NewScore,
                    ai_touch_count = COALESCE(ai_touch_count, 0) + 1,
                    updated_at = @Now
                WHERE lead_id = @LeadId",
                new { NewScore = aiResult.NewScore, Now = DateTime.UtcNow, LeadId = (string?)lead.lead_id }, tx);
        }

        var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - input.StartMs;
        await LogActionAsync(db, tx, "lead_scored", "sales_leads", (string?)lead.lead_id,
            "antigravity", $"リードスコア更新: {lead.customer_name}",
            $"スコア {lead.lead_score ?? 0} → {aiResult.NewScore} / 確信度 {aiResult.ConfidenceScore}%",
            (int)elapsedMs);
    }

    private static decimal ParseBudget(string? budget)
    {
        if (string.IsNullOrEmpty(budget)) return 0;
        var cleaned = new string(budget.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return decimal.TryParse(cleaned, out var v) ? v : 0;
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
