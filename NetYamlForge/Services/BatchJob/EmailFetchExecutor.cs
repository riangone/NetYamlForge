using System.Data;
using Dapper;
using NetYamlForge.Services.Email;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// メール受信タスク実行器
/// </summary>
public class EmailFetchExecutor
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailFetchExecutor> _logger;

    public EmailFetchExecutor(IEmailService emailService, ILogger<EmailFetchExecutor> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<BatchJobResult> ExecuteAsync(
        BatchJobDefinition job,
        IDbConnection db,
        IDbTransaction? tx,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult
        {
            JobId = job.Id,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("メール受信タスク開始：{JobId}", job.Id);

            var limit = job.Settings.BatchSize > 0 ? job.Settings.BatchSize : 10;
            var emails = await _emailService.FetchUnreadEmailsAsync(limit);

            if (emails.Count == 0)
            {
                _logger.LogInformation("新着メールはありませんでした。");
                result.Success = true;
                result.RowsAffected = 0;
                result.EndedAt = DateTime.UtcNow;
                return result;
            }

            var rowsAffected = 0;
            var tableName = job.Settings.TargetTable ?? "received_emails";

            foreach (var email in emails)
            {
                // テーブルが存在するか確認し、なければ作成（簡易版）
                if (!System.Text.RegularExpressions.Regex.IsMatch(tableName, @"^[A-Za-z0-9_]+$"))
                {
                    throw new ArgumentException("Invalid table name: " + tableName);
                }
                
                await EnsureTableExistsAsync(db, tx, tableName);

                var insertCmd = "INSERT INTO " + tableName + " (" +
                    "message_id, sender, recipient, subject, body, is_html, received_at, created_at" +
                    ") VALUES (" +
                    "@MessageId, @Sender, @Recipient, @Subject, @Body, @IsHtml, @ReceivedAt, @CreatedAt" +
                    ")";

                await db.ExecuteAsync(insertCmd, new
                {
                    MessageId = email.MessageId,
                    Sender = email.From,
                    Recipient = email.To,
                    Subject = email.Subject,
                    Body = email.Body,
                    IsHtml = email.IsHtml ? 1 : 0,
                    ReceivedAt = email.Date.UtcDateTime,
                    CreatedAt = DateTime.UtcNow
                }, transaction: tx);

                if (job.Settings.AutoMarkRead)
                {
                    await _emailService.MarkAsReadAsync(email.Id);
                }

                rowsAffected++;
            }

            result.Success = true;
            result.RowsAffected = rowsAffected;
            result.EndedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "メール受信完了：{JobId}, 受信数: {Rows}, Duration: {Duration}ms",
                job.Id, rowsAffected, result.DurationMs);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メール受信エラー：{JobId}", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndedAt = DateTime.UtcNow;
            return result;
        }
    }

    private async Task EnsureTableExistsAsync(IDbConnection db, IDbTransaction? tx, string tableName)
    {
        // SQLite を想定した簡易的なテーブル作成
        var createCmd = "CREATE TABLE IF NOT EXISTS " + tableName + " (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "message_id TEXT, " +
                "sender TEXT, " +
                "recipient TEXT, " +
                "subject TEXT, " +
                "body TEXT, " +
                "is_html INTEGER, " +
                "received_at DATETIME, " +
                "created_at DATETIME" +
            ")";
        await db.ExecuteAsync(createCmd, transaction: tx);
    }
}

// JobSettings に TargetTable と AutoMarkRead を追加するために BatchJobDefinition を拡張する必要があるかもしれません。
// すでに Settings は Dictionary 的な扱いになっているか、プロパティがあるか確認します。
