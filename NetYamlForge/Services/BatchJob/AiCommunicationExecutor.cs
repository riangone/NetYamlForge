// ファイル概要：AI コミュニケーション自動化エンジン。
// Antigravity CLI でパーソナライズメッセージを生成し、EmailService で顧客へ自動送信します。
// lead_nurturing_tasks の pending タスクを処理し、ai_communications にログを残します。

using System.Data;
using Dapper;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.Email;
using NetYamlForge.Models.Email;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// AI コミュニケーション自動化エンジン。
/// ・育成タスク(email/sms)を処理 → AI生成メッセージを送信
/// ・見積(approved)を処理 → 見積メール自動送信
/// ・応答トラッキング更新
/// </summary>
public class AiCommunicationExecutor : AiExecutorBase
{
    public override string StepType => "ai_communication_sender";
    private readonly IEmailServiceFactory _emailFactory;
    private readonly ILogger<AiCommunicationExecutor> _logger;

    public AiCommunicationExecutor(
        ICliChainService cliChain,
        IEmailServiceFactory emailFactory,
        ILogger<AiCommunicationExecutor> logger) : base(cliChain, logger)
    {
        _emailFactory = emailFactory;
        _logger = logger;
    }

    public override async Task ExecuteAsync(
        BatchJobDefinition job, string? projectName,
        IDbConnection db, IDbTransaction tx,
        BatchJobResult result, CancellationToken ct)
    {
        var r = await ExecuteAsync(job, projectName ?? "", db, tx, ct);
        result.Success = r.Success;
        result.RowsAffected = r.RowsAffected;
        result.ErrorMessage = r.ErrorMessage;
        result.ErrorDetail = r.ErrorDetail;
    }

    public async Task<BatchJobResult> ExecuteAsync(
        BatchJobDefinition job,
        string projectName,
        IDbConnection db,
        IDbTransaction tx,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult { JobId = job.Id, StartedAt = DateTime.UtcNow };

        try
        {
            var mode = job.Settings.Params?.GetValueOrDefault("mode") ?? "nurturing_email";
            var maxItems = int.TryParse(job.Settings.Params?.GetValueOrDefault("maxItems"), out var m) ? m : 10;
            var autoSendThreshold = double.TryParse(job.Settings.Params?.GetValueOrDefault("autoSendThreshold"), out var a) ? a : 80.0;

            _logger.LogInformation("[AiCommExecutor] Start: mode={Mode}, project={Project}", mode, projectName);

            var rowsAffected = mode switch
            {
                "nurturing_email"  => await RunNurturingEmailAsync(projectName, db, tx, maxItems, autoSendThreshold, cancellationToken),
                "quote_email"      => await RunQuoteEmailAsync(projectName, db, tx, maxItems, autoSendThreshold, cancellationToken),
                "response_check"   => await RunResponseCheckAsync(projectName, db, tx, maxItems, cancellationToken),
                _                  => throw new NotSupportedException($"Unknown communication mode: {mode}")
            };

            result.Success = true;
            result.RowsAffected = rowsAffected;
            _logger.LogInformation("[AiCommExecutor] Complete: mode={Mode}, rows={Rows}", mode, rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiCommExecutor] Error: {JobId}", job.Id);
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
    // モード1: 育成タスク → AI生成メール送信
    // ──────────────────────────────────────────────────────────────

    private async Task<int> RunNurturingEmailAsync(
        string projectName, IDbConnection db, IDbTransaction tx,
        int maxItems, double autoSendThreshold, CancellationToken ct)
    {
        // email/appointment タスクのうち pending かつ due_date が今日以前を取得
        var tasks = (await db.QueryAsync(@"
            SELECT
                lnt.task_id, lnt.lead_id, lnt.customer_id,
                lnt.task_type, lnt.ai_recommendation, lnt.ai_reasoning,
                lnt.priority_score,
                sl.vehicle_interest, sl.budget, sl.lead_score,
                c.name AS customer_name, c.email AS customer_email,
                c.phone AS customer_phone
            FROM lead_nurturing_tasks lnt
            LEFT JOIN sales_leads sl ON lnt.lead_id = sl.lead_id
            LEFT JOIN customers c    ON lnt.customer_id = c.customer_id
            WHERE lnt.status = 'pending'
              AND (lnt.comm_sent_at IS NULL)
              AND lnt.task_type IN ('email', 'appointment', 'test_drive', 'catalog')
              AND (lnt.due_date IS NULL OR lnt.due_date <= date('now', '+1 day'))
            ORDER BY lnt.priority_score DESC, lnt.due_date ASC
            LIMIT @Max", new { Max = maxItems }, tx)).ToList();

        if (!tasks.Any())
        {
            _logger.LogInformation("[AiCommExecutor] nurturing_email: No pending tasks.");
            return 0;
        }

        var emailSvc = _emailFactory.GetForProject(projectName);
        var rowsAffected = 0;

        foreach (var task in tasks)
        {
            // Dapper dynamic → 型付き変数に明示的に取り出す（ILogger 拡張メソッドへの dynamic dispatch 回避）
            string taskId = AiCommunicationEmailHelper.ToStringOrDefault(task.task_id);
            string leadId = AiCommunicationEmailHelper.ToStringOrDefault(task.lead_id);
            string customerId = AiCommunicationEmailHelper.ToStringOrDefault(task.customer_id);
            string? customerEmail = AiCommunicationEmailHelper.ToStringOrNull(task.customer_email);
            string customerName = AiCommunicationEmailHelper.ToStringOrDefault(task.customer_name);
            string? vehicleInterest = AiCommunicationEmailHelper.ToStringOrNull(task.vehicle_interest);
            string? budget = AiCommunicationEmailHelper.ToStringOrNull(task.budget);
            int leadScore = AiCommunicationEmailHelper.ToIntOrDefault(task.lead_score);
            string taskType = AiCommunicationEmailHelper.ToStringOrDefault(task.task_type);
            string aiRecommendation = AiCommunicationEmailHelper.ToStringOrDefault(task.ai_recommendation);
            string aiReasoning = AiCommunicationEmailHelper.ToStringOrDefault(task.ai_reasoning);

            try
            {
                if (string.IsNullOrWhiteSpace(customerEmail))
                {
                    _logger.LogWarning("[AiCommExecutor] No email for customer {CustomerId}", customerId);
                    continue;
                }

                // Antigravity CLI でパーソナライズメッセージ生成
                var prompt = BuildNurturingEmailPrompt(
                    customerName, vehicleInterest, budget, leadScore, taskType, aiRecommendation, aiReasoning);
                var aiResult = await Cli.PromptJsonAsync<EmailMessageResult>(
                    prompt, projectName: projectName, cancellationToken: ct);

                if (aiResult == null)
                {
                    _logger.LogWarning("[AiCommExecutor] Antigravity returned null for task {TaskId}", taskId);
                    continue;
                }

                var commId = AiCommunicationEmailHelper.GenerateCommId("NE", taskId);
                var requiresHuman = aiResult.ConfidenceScore < autoSendThreshold;

                // ai_communications に記録
                await AiCommunicationEmailHelper.InsertCommunicationAsync(
                    db, tx, commId, leadId, customerId,
                    aiResult.Subject, aiResult.Body, aiResult.ConfidenceScore,
                    requiresHuman, taskId: taskId);

                if (!requiresHuman)
                {
                    // 実際にメール送信
                    var sent = await AiCommunicationEmailHelper.SendEmailAndUpdateStatusAsync(
                        emailSvc, db, tx, commId, customerEmail, aiResult.Subject, aiResult.HtmlBody);

                    if (sent)
                    {
                        // 育成タスクに送信記録
                        await AiCommunicationEmailHelper.UpdateNurturingTaskStatusAsync(
                            db, tx, taskId, commId, leadId);

                        _logger.LogInformation("[AiCommExecutor] Email sent to {Email}: {Subject}",
                            customerEmail, aiResult.Subject);
                    }
                }

                await AiCommunicationEmailHelper.LogActionAsync(db, tx, "comm_email_sent",
                    "lead_nurturing_tasks", taskId,
                    "antigravity", $"育成メール: {customerName}",
                    $"件名: {aiResult.Subject} / 確信度: {aiResult.ConfidenceScore}%",
                    0);

                rowsAffected++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiCommExecutor] Failed for task {TaskId}", taskId);
            }
        }

        return rowsAffected;
    }

    // ──────────────────────────────────────────────────────────────
    // モード2: 承認済み見積 → AI生成見積メール送信
    // ──────────────────────────────────────────────────────────────

    private async Task<int> RunQuoteEmailAsync(
        string projectName, IDbConnection db, IDbTransaction tx,
        int maxItems, double autoSendThreshold, CancellationToken ct)
    {
        var quotes = (await db.QueryAsync(@"
            SELECT
                aq.quote_id, aq.lead_id, aq.customer_id,
                aq.base_price, aq.final_price, aq.discount_rate,
                aq.monthly_payment, aq.accessories, aq.notes,
                aq.valid_until, aq.ai_reasoning, aq.ai_confidence,
                v.make, v.model, v.year, v.color,
                sl.vehicle_interest, sl.lead_score,
                c.name AS customer_name, c.email AS customer_email
            FROM ai_quotes aq
            LEFT JOIN vehicles v   ON aq.vehicle_id = v.vehicle_id
            LEFT JOIN sales_leads sl ON aq.lead_id = sl.lead_id
            LEFT JOIN customers c  ON aq.customer_id = c.customer_id
            WHERE aq.status = 'approved'
              AND (aq.quote_sent_at IS NULL)
              AND c.email IS NOT NULL AND c.email != ''
            ORDER BY aq.created_at ASC
            LIMIT @Max", new { Max = maxItems }, tx)).ToList();

        if (!quotes.Any())
        {
            _logger.LogInformation("[AiCommExecutor] quote_email: No approved quotes to send.");
            return 0;
        }

        var emailSvc = _emailFactory.GetForProject(projectName);
        var rowsAffected = 0;

        foreach (var quote in quotes)
        {
            // Dapper dynamic → 型付き変数
            string quoteId = AiCommunicationEmailHelper.ToStringOrDefault(quote.quote_id);
            string leadId = AiCommunicationEmailHelper.ToStringOrDefault(quote.lead_id);
            string customerId = AiCommunicationEmailHelper.ToStringOrDefault(quote.customer_id);
            string customerName = AiCommunicationEmailHelper.ToStringOrDefault(quote.customer_name);
            string? customerEmail = AiCommunicationEmailHelper.ToStringOrNull(quote.customer_email);
            string? vehicleInterest = AiCommunicationEmailHelper.ToStringOrNull(quote.vehicle_interest);
            string? make = AiCommunicationEmailHelper.ToStringOrNull(quote.make);
            string? model = AiCommunicationEmailHelper.ToStringOrNull(quote.model);
            string? yearStr = AiCommunicationEmailHelper.ToStringOrNull(quote.year);
            string? color = AiCommunicationEmailHelper.ToStringOrNull(quote.color);
            double basePrice = AiCommunicationEmailHelper.ToDoubleOrDefault(quote.base_price);
            double finalPrice = AiCommunicationEmailHelper.ToDoubleOrDefault(quote.final_price);
            double discountRate = AiCommunicationEmailHelper.ToDoubleOrDefault(quote.discount_rate);
            double monthlyPayment = AiCommunicationEmailHelper.ToDoubleOrDefault(quote.monthly_payment);
            string? accessories = AiCommunicationEmailHelper.ToStringOrNull(quote.accessories);
            string? notes = AiCommunicationEmailHelper.ToStringOrNull(quote.notes);
            string? validUntil = AiCommunicationEmailHelper.ToStringOrNull(quote.valid_until);
            int leadScore = AiCommunicationEmailHelper.ToIntOrDefault(quote.lead_score);

            try
            {
                var prompt = BuildQuoteEmailPrompt(
                    customerName, vehicleInterest, make, model, yearStr, color,
                    basePrice, finalPrice, discountRate, monthlyPayment,
                    accessories, notes, validUntil, leadScore);
                var aiResult = await Cli.PromptJsonAsync<EmailMessageResult>(
                    prompt, projectName: projectName, cancellationToken: ct);

                if (aiResult == null) continue;

                var commId = AiCommunicationEmailHelper.GenerateCommId("QE", quoteId);
                var requiresHuman = aiResult.ConfidenceScore < autoSendThreshold;

                await AiCommunicationEmailHelper.InsertCommunicationAsync(
                    db, tx, commId, leadId, customerId,
                    aiResult.Subject, aiResult.Body, aiResult.ConfidenceScore,
                    requiresHuman);

                if (!requiresHuman)
                {
                    var sent = await AiCommunicationEmailHelper.SendEmailAndUpdateStatusAsync(
                        emailSvc, db, tx, commId, customerEmail ?? "", aiResult.Subject, aiResult.HtmlBody);

                    if (sent)
                    {
                        await AiCommunicationEmailHelper.UpdateQuoteStatusAsync(
                            db, tx, quoteId, commId);

                        _logger.LogInformation("[AiCommExecutor] Quote email sent: quote={QuoteId}", quoteId);
                    }
                }

                rowsAffected++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiCommExecutor] Quote email failed for {QuoteId}", quoteId);
            }
        }

        return rowsAffected;
    }

    // ──────────────────────────────────────────────────────────────
    // モード3: 無返信メール → Antigravity CLI で感情分析・追跡更新
    // ──────────────────────────────────────────────────────────────

    private async Task<int> RunResponseCheckAsync(
        string projectName, IDbConnection db, IDbTransaction tx,
        int maxItems, CancellationToken ct)
    {
        // 送信済みで3日以上返信なしのコミュニケーション
        var comms = (await db.QueryAsync(@"
            SELECT
                ac.comm_id, ac.lead_id, ac.customer_id,
                ac.subject, ac.body_text, ac.sent_at,
                c.name AS customer_name, sl.lead_score
            FROM ai_communications ac
            LEFT JOIN customers c ON ac.customer_id = c.customer_id
            LEFT JOIN sales_leads sl ON ac.lead_id = sl.lead_id
            WHERE ac.send_status = 'sent'
              AND ac.response_received = 0
              AND ac.sent_at <= datetime('now', '-3 days')
            ORDER BY ac.sent_at ASC
            LIMIT @Max", new { Max = maxItems }, tx)).ToList();

        if (!comms.Any())
        {
            _logger.LogInformation("[AiCommExecutor] response_check: No unresponded comms.");
            return 0;
        }

        var rowsAffected = 0;
        foreach (var comm in comms)
        {
            // Dapper dynamic → 型付き変数
            string commId = AiCommunicationEmailHelper.ToStringOrDefault(comm.comm_id);
            string leadId = AiCommunicationEmailHelper.ToStringOrDefault(comm.lead_id);
            string customerId = AiCommunicationEmailHelper.ToStringOrDefault(comm.customer_id);
            string customerName = AiCommunicationEmailHelper.ToStringOrDefault(comm.customer_name);
            string? subject = AiCommunicationEmailHelper.ToStringOrNull(comm.subject);
            string? sentAt = AiCommunicationEmailHelper.ToStringOrNull(comm.sent_at);
            int leadScore = AiCommunicationEmailHelper.ToIntOrDefault(comm.lead_score);

            try
            {
                // AI で無返信状態を分析し、次のアクションを提案
                var prompt = BuildNoResponsePrompt(customerName, subject, sentAt, leadScore);
                var aiResult = await Cli.PromptJsonAsync<NoResponseAnalysis>(
                    prompt, projectName: projectName, cancellationToken: ct);

                if (aiResult == null) continue;

                // 育成タスクに再フォロー推奨を追加
                var retaskId = $"LNT-RETRY-{leadId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
                await db.ExecuteAsync(@"
                    INSERT OR IGNORE INTO lead_nurturing_tasks (
                        task_id, lead_id, customer_id,
                        task_type, trigger_reason, ai_recommendation, ai_reasoning,
                        priority_score, status, due_date,
                        created_at, updated_at
                    ) VALUES (
                        @TaskId, @LeadId, @CustomerId,
                        @TaskType, @Trigger, @Recommendation, @Reasoning,
                        @Priority, 'pending', date('now', '+2 days'),
                        @Now, @Now
                    )", new
                {
                    TaskId = retaskId,
                    LeadId = leadId,
                    CustomerId = customerId,
                    TaskType = aiResult.SuggestedChannel,
                    Trigger = $"3日間未返信（送信: {sentAt}）",
                    Recommendation = aiResult.FollowUpTitle,
                    Reasoning = aiResult.Reasoning,
                    Priority = 70,
                    Now = DateTime.UtcNow
                }, tx);

                await AiCommunicationEmailHelper.LogActionAsync(db, tx, "no_response_followup",
                    "ai_communications", commId,
                    "antigravity", $"無返信フォロー: {customerName}",
                    $"推奨: {aiResult.SuggestedChannel} - {aiResult.FollowUpTitle}",
                    0);

                rowsAffected++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiCommExecutor] response_check failed for {CommId}", commId);
            }
        }

        return rowsAffected;
    }

    // ──────────────────────────────────────────────────────────────
    // プロンプト構築
    // ──────────────────────────────────────────────────────────────

    private static string BuildNurturingEmailPrompt(
        string customerName, string? vehicleInterest, string? budget,
        int leadScore, string taskType, string aiRecommendation, string aiReasoning)
    {
        return $@"
あなたは自動車ディーラーのAIコミュニケーション担当です。
以下の顧客に対してパーソナライズされた育成メールを作成してください。

【顧客情報】
- 顧客名: {customerName}
- 関心車種: {vehicleInterest ?? "未記載"}
- 予算: {budget ?? "未記載"}
- リードスコア: {leadScore}

【タスク情報】
- タスクタイプ: {taskType}
- AI推奨内容: {aiRecommendation}
- 理由: {aiReasoning}

【メール作成ガイドライン】
- 件名は親しみやすく、開封されやすいものにする
- 本文は自然な日本語で、押し売り感がないよう注意
- 具体的な次のステップを1つ提示する
- 文字数: 200〜400文字
- HTMLメールとしてフォーマットする（シンプルなデザイン）

以下のJSON形式のみで回答してください:
{{
  ""subject"": ""メール件名（50文字以内）"",
  ""body"": ""プレーンテキスト本文（300文字以内）"",
  ""htmlBody"": ""HTML形式の本文（シンプルなフォーマット、スタイルインライン）"",
  ""confidenceScore"": 数値(0-100),
  ""reasoning"": ""このメール内容を選んだ理由（日本語、80文字以内）""
}}";
    }

    private static string BuildQuoteEmailPrompt(
        string customerName, string? vehicleInterest,
        string? make, string? model, string? year, string? color,
        double basePrice, double finalPrice, double discountRate, double monthlyPayment,
        string? accessories, string? notes, string? validUntil, int leadScore)
    {
        return $@"
あなたは自動車ディーラーのAI見積送信担当です。
以下の見積情報を基に、顧客への見積提案メールを作成してください。

【顧客情報】
- 顧客名: {customerName}
- 希望車種: {vehicleInterest ?? "未記載"}

【見積内容】
- 車両: {year ?? ""} {make ?? ""} {model ?? ""} {color ?? ""}
- 定価: ¥{basePrice:N0}
- 割引率: {discountRate}%
- 最終価格: ¥{finalPrice:N0}
- 月額ローン: ¥{monthlyPayment:N0}（60回払い）
- 有効期限: {validUntil ?? "14日間"}
- 推奨オプション: {accessories ?? "なし"}
- AIコメント: {notes ?? ""}

【メール作成ガイドライン】
- 件名は見積内容を明示し、期限感を出す
- 金額は具体的に記載（定価・割引・最終価格）
- ローン月額を強調してお得感を演出
- 期限を明記して行動を促す
- 次のステップ（試乗・来店）を提案

以下のJSON形式のみで回答してください:
{{
  ""subject"": ""メール件名（60文字以内）"",
  ""body"": ""プレーンテキスト本文（400文字以内）"",
  ""htmlBody"": ""HTML形式の本文（見積テーブル含む、スタイルインライン）"",
  ""confidenceScore"": 数値(0-100),
  ""reasoning"": ""このメール内容を選んだ理由（日本語、80文字以内）""
}}";
    }

    private static string BuildNoResponsePrompt(
        string customerName, string? subject, string? sentAt, int leadScore)
    {
        return $@"
あなたは自動車ディーラーのAIフォロー担当です。
3日前に送ったメールに返信がありません。次のフォローアクションを提案してください。

【状況】
- 顧客名: {customerName}
- 送信メール件名: {subject ?? "不明"}
- 送信日: {sentAt ?? "不明"}
- リードスコア: {leadScore}

【利用可能なチャネル】
- phone_call: 電話フォローアップ（高スコア推奨）
- email: 再メール（件名・内容変更）
- sms: SMS送信（短文で反応確認）

以下のJSON形式のみで回答してください:
{{
  ""suggestedChannel"": ""チャネル種別（上記から1つ）"",
  ""followUpTitle"": ""フォローアップタスクタイトル（30文字以内）"",
  ""reasoning"": ""このアクションを選んだ理由（日本語、80文字以内）"",
  ""urgencyLevel"": 数値(1-5),
  ""confidenceScore"": 数値(0-100)
}}";
    }

    // ──────────────────────────────────────────────────────────────
    // AI レスポンスモデル
    // ──────────────────────────────────────────────────────────────

    private class EmailMessageResult
    {
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public string HtmlBody { get; set; } = "";
        public double ConfidenceScore { get; set; }
        public string Reasoning { get; set; } = "";
    }

    private class NoResponseAnalysis
    {
        public string SuggestedChannel { get; set; } = "";
        public string FollowUpTitle { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public int UrgencyLevel { get; set; }
        public double ConfidenceScore { get; set; }
    }
}
