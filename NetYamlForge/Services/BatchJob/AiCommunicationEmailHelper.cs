using System.Data;
using Dapper;
using NetYamlForge.Services.Email;
using NetYamlForge.Models.Email;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// AiCommunicationExecutor の共通メール処理ロジックを分離したヘルパークラス。
/// 育成メールと見積メールの両方で使用される共通処理を担当します。
/// </summary>
public static class AiCommunicationEmailHelper
{
    /// <summary>
    /// AIコミュニケーション記録をデータベースに挿入します。
    /// </summary>
    public static async Task<string> InsertCommunicationAsync(
        IDbConnection db, IDbTransaction tx,
        string commId, string leadId, string customerId,
        string subject, string body, double confidence,
        bool requiresHuman, string? taskId = null, string? quoteId = null)
    {
        await db.ExecuteAsync(@"
            INSERT OR IGNORE INTO ai_communications (
                comm_id, lead_id, customer_id, nurturing_task_id,
                comm_channel, subject, body_text,
                ai_personalized, ai_model, ai_confidence,
                send_status, requires_human, created_at, updated_at
            ) VALUES (
                @CommId, @LeadId, @CustomerId, @TaskId,
                'email', @Subject, @Body,
                1, 'antigravity', @Confidence,
                @Status, @RequiresHuman, @Now, @Now
            )", new
        {
            CommId = commId,
            LeadId = leadId,
            CustomerId = customerId,
            TaskId = taskId,
            Subject = subject,
            Body = body,
            Confidence = confidence,
            Status = requiresHuman ? "pending" : "sent",
            RequiresHuman = requiresHuman ? 1 : 0,
            Now = DateTime.UtcNow
        }, tx);

        return commId;
    }

    /// <summary>
    /// メール送信を実行し、送信状況を更新します。
    /// </summary>
    public static async Task<bool> SendEmailAndUpdateStatusAsync(
        IEmailService emailSvc, IDbConnection db, IDbTransaction tx,
        string commId, string customerEmail, string subject, string htmlBody)
    {
        try
        {
            await emailSvc.SendEmailAsync(new EmailMessage
            {
                To = customerEmail,
                Subject = subject,
                Body = htmlBody,
                IsHtml = true
            });

            await db.ExecuteAsync(@"
                UPDATE ai_communications SET sent_at = @Now, updated_at = @Now
                WHERE comm_id = @CommId",
                new { Now = DateTime.UtcNow, CommId = commId }, tx);

            return true;
        }
        catch (Exception ex)
        {
            await db.ExecuteAsync(@"
                UPDATE ai_communications
                SET send_status = 'failed', error_message = @Err, updated_at = @Now
                WHERE comm_id = @CommId",
                new { Err = ex.Message, Now = DateTime.UtcNow, CommId = commId }, tx);

            return false;
        }
    }

    /// <summary>
    /// 育成タスクの送信記録を更新します。
    /// </summary>
    public static async Task UpdateNurturingTaskStatusAsync(
        IDbConnection db, IDbTransaction tx,
        string taskId, string commId, string leadId)
    {
        await db.ExecuteAsync(@"
            UPDATE lead_nurturing_tasks
            SET comm_sent_at = @Now, comm_id = @CommId, status = 'in_progress', updated_at = @Now
            WHERE task_id = @TaskId",
            new { Now = DateTime.UtcNow, CommId = commId, TaskId = taskId }, tx);

        await db.ExecuteAsync(@"
            UPDATE sales_leads
            SET ai_touch_count = COALESCE(ai_touch_count, 0) + 1, updated_at = @Now
            WHERE lead_id = @LeadId",
            new { Now = DateTime.UtcNow, LeadId = leadId }, tx);
    }

    /// <summary>
    /// 見積の送信記録を更新します。
    /// </summary>
    public static async Task UpdateQuoteStatusAsync(
        IDbConnection db, IDbTransaction tx,
        string quoteId, string commId)
    {
        await db.ExecuteAsync(@"
            UPDATE ai_quotes
            SET quote_sent_at = @Now, quote_comm_id = @CommId, status = 'sent', updated_at = @Now
            WHERE quote_id = @QuoteId",
            new { Now = DateTime.UtcNow, CommId = commId, QuoteId = quoteId }, tx);
    }

    /// <summary>
    /// アクションログを記録します。
    /// </summary>
    public static async Task LogActionAsync(
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

    /// <summary>
    /// コミュニケーションIDを生成します。
    /// </summary>
    public static string GenerateCommId(string prefix, string entityId)
    {
        return $"COMM-{prefix}-{entityId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
    }

    /// <summary>
    /// Dapper dynamic結果から型付き変数に変換するユーティリティメソッド。
    /// </summary>
    public static string ToStringOrDefault(dynamic value, string defaultValue = "")
    {
        return Convert.ToString(value) ?? defaultValue;
    }

    public static string? ToStringOrNull(dynamic value)
    {
        var result = Convert.ToString(value);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    public static int ToIntOrDefault(dynamic value, int defaultValue = 0)
    {
        return value == null ? defaultValue : (int)value;
    }

    public static double ToDoubleOrDefault(dynamic value, double defaultValue = 0.0)
    {
        return value == null ? defaultValue : (double)value;
    }
}
