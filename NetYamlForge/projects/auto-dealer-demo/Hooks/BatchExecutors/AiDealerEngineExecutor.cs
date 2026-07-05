// ファイル概要：AI ディーラーエンジン。Antigravity CLI を使用してリードスコアリング・育成タスク・見積生成を自動実行します。

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

/// <summary>
/// AI 全面主導の汽車販売管理エンジン。
/// リードスコアリング・育成タスク生成・見積生成を Antigravity CLI で自動実行し、
/// ai_decisions テーブルに結果を書き込む。
/// </summary>
public class AiDealerEngineExecutor : AiExecutorBase
{
    public override string StepType => "ai_dealer_engine";

    private readonly ILogger<AiDealerEngineExecutor> _logger;

    public AiDealerEngineExecutor(ICliChainService cliChain, ILogger<AiDealerEngineExecutor> logger) : base(cliChain, logger)
    {
        _logger = logger;
    }

    public override async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var execResult = await ExecuteInternalAsync(job, projectName ?? "", db, tx, ct);
        result.Success = execResult.Success;
        result.RowsAffected = execResult.RowsAffected;
        result.ErrorMessage = execResult.ErrorMessage;
        result.ErrorDetail = execResult.ErrorDetail;
    }

    public async Task<BatchJobResult> ExecuteInternalAsync(
        BatchJobDefinition job,
        string projectName,
        IDbConnection db,
        IDbTransaction tx,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult { JobId = job.Id, StartedAt = DateTime.UtcNow };

        try
        {
            var mode = job.Settings.Params?.GetValueOrDefault("mode") ?? "lead_scoring";
            var maxItems = int.TryParse(job.Settings.Params?.GetValueOrDefault("maxItems"), out var m) ? m : 5;
            var autoExecuteThreshold = double.TryParse(job.Settings.Params?.GetValueOrDefault("autoExecuteThreshold"), out var a) ? a : 90.0;

            _logger.LogInformation("[AiDealerEngine] Start: mode={Mode}, project={Project}", mode, projectName);

            var rowsAffected = mode switch
            {
                "lead_scoring"     => await RunLeadScoringAsync(job, projectName, db, tx, maxItems, autoExecuteThreshold, cancellationToken),
                "nurturing"        => await RunNurturingAsync(job, projectName, db, tx, maxItems, autoExecuteThreshold, cancellationToken),
                "quote_generation" => await RunQuoteGenerationAsync(job, projectName, db, tx, maxItems, autoExecuteThreshold, cancellationToken),
                _                  => throw new NotSupportedException($"Unknown mode: {mode}")
            };

            result.Success = true;
            result.RowsAffected = rowsAffected;
            _logger.LogInformation("[AiDealerEngine] Complete: mode={Mode}, rows={Rows}", mode, rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiDealerEngine] Error: {JobId}", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorDetail = ex.ToString();
        }
        finally
        {
            result.EndedAt = DateTime.UtcNow;
        }

        return result;
    }

    // ──────────────────────────────────────────────────────────────
    // モード1: リードスコアリング
    // ──────────────────────────────────────────────────────────────

    private async Task<int> RunLeadScoringAsync(
        BatchJobDefinition job, string projectName, IDbConnection db, IDbTransaction tx,
        int maxItems, double autoExecuteThreshold, CancellationToken cancellationToken)
    {
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

        if (!leads.Any())
        {
            _logger.LogInformation("[AiDealerEngine] lead_scoring: No stale leads found.");
            return 0;
        }

        var rowsAffected = 0;
        foreach (var lead in leads)
        {
            var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                string prompt = BuildLeadScoringPrompt(lead);
                var aiResult = await Cli.PromptJsonAsync<LeadScoringResult>(
                    prompt, projectName: projectName, cancellationToken: cancellationToken);

                if (aiResult == null) continue;

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

                var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                await LogActionAsync(db, tx, "lead_scored", "sales_leads", (string?)lead.lead_id,
                    "antigravity", $"リードスコア更新: {lead.customer_name}",
                    $"スコア {lead.lead_score ?? 0} → {aiResult.NewScore} / 確信度 {aiResult.ConfidenceScore}%",
                    (int)elapsedMs);

                rowsAffected++;
            }
            catch (Exception ex)
            {
                var leadIdStr = (string?)lead.lead_id;
                _logger.LogWarning(ex, "[AiDealerEngine] lead_scoring failed for lead_id={LeadId}", leadIdStr);
            }
        }

        return rowsAffected;
    }

    // ──────────────────────────────────────────────────────────────
    // モード2: 育成タスク生成
    // ──────────────────────────────────────────────────────────────

    private async Task<int> RunNurturingAsync(
        BatchJobDefinition job, string projectName, IDbConnection db, IDbTransaction tx,
        int maxItems, double autoExecuteThreshold, CancellationToken cancellationToken)
    {
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

        if (!leads.Any())
        {
            _logger.LogInformation("[AiDealerEngine] nurturing: No candidates found.");
            return 0;
        }

        var rowsAffected = 0;
        foreach (var lead in leads)
        {
            var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                string prompt = BuildNurturingPrompt(lead);
                var aiResult = await Cli.PromptJsonAsync<NurturingResult>(
                    prompt, projectName: projectName, cancellationToken: cancellationToken);

                if (aiResult == null) continue;

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
                    // lead_nurturing_tasks の実際のスキーマに合わせて挿入
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

                var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                await LogActionAsync(db, tx, "nurturing_created", "sales_leads", (string?)lead.lead_id,
                    "antigravity", $"育成タスク生成: {lead.customer_name}",
                    $"{aiResult.ActionType}: {aiResult.TaskTitle}",
                    (int)elapsedMs);

                rowsAffected++;
            }
            catch (Exception ex)
            {
                var leadIdStr = (string?)lead.lead_id;
                _logger.LogWarning(ex, "[AiDealerEngine] nurturing failed for lead_id={LeadId}", leadIdStr);
            }
        }

        return rowsAffected;
    }

    // ──────────────────────────────────────────────────────────────
    // モード3: AI 見積生成
    // ──────────────────────────────────────────────────────────────

    private async Task<int> RunQuoteGenerationAsync(
        BatchJobDefinition job, string projectName, IDbConnection db, IDbTransaction tx,
        int maxItems, double autoExecuteThreshold, CancellationToken cancellationToken)
    {
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

        if (!leads.Any())
        {
            _logger.LogInformation("[AiDealerEngine] quote_generation: No candidates found.");
            return 0;
        }

        var rowsAffected = 0;
        foreach (var lead in leads)
        {
            var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                string prompt = BuildQuotePrompt(lead);
                var aiResult = await Cli.PromptJsonAsync<QuoteResult>(
                    prompt, projectName: projectName, cancellationToken: cancellationToken);

                if (aiResult == null) continue;

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

                // vehicle_id が NULL の場合はスキップ（FKが NOT NULL のため）
                if (string.IsNullOrEmpty(vehicleId) || string.IsNullOrEmpty(customerId)) continue;

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

                var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startMs;
                await LogActionAsync(db, tx, "quote_generated", "ai_quotes", quoteId,
                    "antigravity", $"AI見積生成: {lead.customer_name}",
                    $"¥{aiResult.FinalPrice:N0} (割引率 {aiResult.DiscountRate}%)",
                    (int)elapsedMs);

                rowsAffected++;
            }
            catch (Exception ex)
            {
                var leadIdStr = (string?)lead.lead_id;
                _logger.LogWarning(ex, "[AiDealerEngine] quote_generation failed for lead_id={LeadId}", leadIdStr);
            }
        }

        return rowsAffected;
    }

    // ──────────────────────────────────────────────────────────────
    // プロンプト構築
    // ──────────────────────────────────────────────────────────────

    private static string BuildLeadScoringPrompt(dynamic lead)
    {
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

    private static string BuildNurturingPrompt(dynamic lead)
    {
        return $@"
あなたは自動車販売のAIコンサルタントです。以下の顧客に最適な育成アクションを提案してください。

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

    private static string BuildQuotePrompt(dynamic lead)
    {
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

    // ──────────────────────────────────────────────────────────────
    // ユーティリティ
    // ──────────────────────────────────────────────────────────────

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

    private static decimal ParseBudget(string? budget)
    {
        if (string.IsNullOrEmpty(budget)) return 0;
        var cleaned = new string(budget.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return decimal.TryParse(cleaned, out var v) ? v : 0;
    }

    // ──────────────────────────────────────────────────────────────
    // AI レスポンス モデル
    // ──────────────────────────────────────────────────────────────

    private class LeadScoringResult
    {
        public int NewScore { get; set; }
        public double ConfidenceScore { get; set; }
        public string Reasoning { get; set; } = "";
        public string RecommendedAction { get; set; } = "";
    }

    private class NurturingResult
    {
        public string ActionType { get; set; } = "";
        public string TaskTitle { get; set; } = "";
        public string MessageDraft { get; set; } = "";
        public double ConfidenceScore { get; set; }
        public string Reasoning { get; set; } = "";
    }

    private class QuoteResult
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
}
