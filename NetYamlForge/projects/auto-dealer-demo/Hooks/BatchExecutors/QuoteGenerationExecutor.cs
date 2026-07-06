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

public class QuoteItem
{
    public dynamic Lead { get; set; } = null!;
    public long StartMs { get; set; }
}

public class QuoteResult
{
    public decimal BasePrice { get; set; }
    public double DiscountRate { get; set; }
    public decimal FinalPrice { get; set; }
    public string RecommendedAccessories { get; set; } = "";
    public string SalesPitch { get; set; } = "";
    public string VehicleSummary { get; set; } = "";
    public double ConfidenceScore { get; set; }
    public string Reasoning { get; set; } = "";
}

public class QuoteGenerationExecutor : ProjectBatchExecutorBase<QuoteItem, QuoteResult>
{
    public override string StepType => "quote_generation";

    public QuoteGenerationExecutor(ICliChainService cli, ILogger logger) : base(cli, logger)
    {
    }

    protected override Task<QuoteItem?> LoadInputAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct) => Task.FromResult<QuoteItem?>(null);

    protected override async Task<IReadOnlyList<QuoteItem>> LoadItemsAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var maxItems = int.TryParse(job.Settings.Params?.GetValueOrDefault("maxItems"), out var m) ? m : 5;
        var leads = (await db.QueryAsync(@"
            SELECT sl.lead_id, sl.customer_id,
                   c.name AS customer_name,
                   sl.vehicle_interest, sl.budget, sl.status, sl.lead_score,
                   sl.last_contact_at,
                   v.vehicle_id, v.make, v.model, v.year, v.price, v.color, v.mileage
            FROM sales_leads sl
            LEFT JOIN customers c ON sl.customer_id = c.customer_id
            LEFT JOIN vehicles v ON LOWER(sl.vehicle_interest) LIKE '%' || LOWER(COALESCE(v.model,'')) || '%'
                                 AND v.model IS NOT NULL AND v.model != ''
            WHERE sl.lead_score >= 80
              AND sl.status NOT IN ('closed_won', 'closed_lost')
              AND NOT EXISTS (
                  SELECT 1 FROM ai_quotes aq
                  WHERE aq.lead_id = sl.lead_id
                    AND aq.status IN ('draft','approved','sent')
              )
            GROUP BY sl.lead_id
            ORDER BY sl.lead_score DESC
            LIMIT @Max", new { Max = maxItems }, tx)).ToList();

        return leads.Select(l => new QuoteItem
        {
            Lead = l,
            StartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }).ToList();
    }

    protected override string BuildPrompt(QuoteItem input)
    {
        var lead = input.Lead;
        return $@"
        あなたは自動車販売の AI 見積エンジンです。以下の顧客情報と在庫車両に基づき、最適な見積を作成してください。

        【顧客情報】
        - 顧客名: {lead.customer_name}
        - 希望車種: {lead.vehicle_interest}
        - 予算: {lead.budget ?? "未記載"}
        - リードスコア: {lead.lead_score ?? 0}

        【在庫車両情報】
        - メーカー: {lead.make ?? "未確定"}
        - モデル: {lead.model ?? "未確定"}
        - 年式: {lead.year ?? ""}
        - 定価: ¥{lead.price ?? 0}
        - 色: {lead.color ?? "未確定"}
        - 走行距離: {lead.mileage ?? 0}km

        【見積ポリシー】
        - 割引率は通常 3-8%。スコア 90 以上は最大 10% まで可
        - 月額ローンは 60 回払いを基準に計算
        - おすすめオプションを 2-3 点提案

        以下のJSON形式のみで回答してください（数値フィールドには数値のみ、文字列フィールドには文字列のみ）:
        {{
          ""basePrice"": 数値（定価、円）,
          ""discountRate"": 数値（割引率 %、小数点1桁）,
          ""finalPrice"": 数値（最終価格、円）,
          ""recommendedAccessories"": ""推奨オプション（カンマ区切り）"",
          ""salesPitch"": ""顧客への提案文（日本語、150文字以内）"",
          ""vehicleSummary"": ""車両サマリー（30文字以内）"",
          ""confidenceScore"": 数値(0-100),
          ""reasoning"": ""この見積の根拠（日本語、100文字以内）""
        }}";
    }

    protected override QuoteResult? ParseResult(string raw)
    {
        if (TryParseJson(raw, out QuoteResult? result, out var error))
        {
            return result;
        }
        Logger.LogWarning("Failed to parse QuoteResult: {Error}", error);
        return null;
    }

    protected override async Task PersistAsync(
        QuoteItem input, QuoteResult aiResult,
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx, CancellationToken ct)
    {
        var lead = input.Lead;
        var autoExecuteThreshold = double.TryParse(job.Settings.Params?.GetValueOrDefault("autoExecuteThreshold"), out var a) ? a : 90.0;
        var quoteId = $"QT-{lead.lead_id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var decisionId = $"QG-{lead.lead_id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var requiresHuman = aiResult.ConfidenceScore < autoExecuteThreshold
            || aiResult.DiscountRate > 10.0;
        var status = requiresHuman ? "pending" : "auto_executed";

        await db.ExecuteAsync(@"
            INSERT OR IGNORE INTO ai_decisions (
                decision_id, decision_type, entity_type, entity_id,
                ai_reasoning, confidence_score, status, requires_human,
                executed_at, created_at
            ) VALUES (
                @DecisionId, 'quote_generation', 'ai_quotes', @QuoteId,
                @Reasoning, @Confidence, @Status, @RequiresHuman,
                @ExecutedAt, @Now
            )", new
        {
            DecisionId = decisionId,
            QuoteId = quoteId,
            Reasoning = $"[車両: {aiResult.VehicleSummary}] [最終価格: ¥{aiResult.FinalPrice:N0}] {aiResult.Reasoning}",
            Confidence = aiResult.ConfidenceScore,
            Status = status,
            RequiresHuman = requiresHuman ? 1 : 0,
            ExecutedAt = status == "auto_executed" ? (DateTime?)DateTime.UtcNow : null,
            Now = DateTime.UtcNow
        }, tx);

        var vehicleId = (string?)lead.vehicle_id;
        var customerId = (string?)lead.customer_id;

        if (string.IsNullOrEmpty(vehicleId) || string.IsNullOrEmpty(customerId)) return;

        var discountAmount = (double)aiResult.BasePrice * aiResult.DiscountRate / 100.0;
        var downPayment = (double)aiResult.FinalPrice * 0.2;
        var monthlyPayment = (double)aiResult.FinalPrice * 0.8 / 60.0;

        await db.ExecuteAsync(@"
            INSERT OR IGNORE INTO ai_quotes (
                quote_id, lead_id, vehicle_id, customer_id,
                base_price, discount_rate, discount_amount, final_price,
                trade_in_value, down_payment, monthly_payment, loan_months,
                accessories, notes, ai_reasoning, ai_confidence,
                status, valid_until, created_at, updated_at
            ) VALUES (
                @QuoteId, @LeadId, @VehicleId, @CustomerId,
                @BasePrice, @DiscountRate, @DiscountAmount, @FinalPrice,
                0, @DownPayment, @MonthlyPayment, 60,
                @Accessories, @Notes, @Reasoning, @Confidence,
                @QuoteStatus, date('now', '+14 days'), @Now, @Now
            )", new
        {
            QuoteId = quoteId,
            LeadId = (string?)lead.lead_id,
            VehicleId = vehicleId,
            CustomerId = customerId,
            BasePrice = (double)aiResult.BasePrice,
            DiscountRate = aiResult.DiscountRate,
            DiscountAmount = discountAmount,
            FinalPrice = (double)aiResult.FinalPrice,
            DownPayment = downPayment,
            MonthlyPayment = monthlyPayment,
            Accessories = aiResult.RecommendedAccessories,
            Notes = aiResult.SalesPitch,
            Reasoning = aiResult.Reasoning,
            Confidence = aiResult.ConfidenceScore,
            QuoteStatus = requiresHuman ? "draft" : "approved",
            Now = DateTime.UtcNow
        }, tx);

        var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - input.StartMs;
        await LogActionAsync(db, tx, "quote_generated", "ai_quotes", quoteId,
            "antigravity", $"AI見積生成: {lead.customer_name}",
            $"¥{aiResult.FinalPrice:N0} (割引率 {aiResult.DiscountRate}%)",
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
