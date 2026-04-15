using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using NetYamlForge.AI.Models;

namespace NetYamlForge.AI.Services;

/// <summary>
/// Email 渠道サービス
/// </summary>
public interface IEmailChannelService
{
    /// <summary>
    /// 受信メールを処理
    /// </summary>
    Task ProcessIncomingEmailAsync(EmailMessage email);

    /// <summary>
    /// 応答メールを送信
    /// </summary>
    Task SendResponseEmailAsync(EmailMessage email);

    /// <summary>
    /// IMAP でメールを受信（バックグラウンドタスク用）
    /// </summary>
    Task<List<EmailMessage>> PollEmailsAsync();
}

/// <summary>
/// Email メッセージ
/// </summary>
public class EmailMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public List<EmailAttachment> Attachments { get; set; } = new();
    public DateTime ReceivedAt { get; set; }
    public string? InReplyTo { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
}

/// <summary>
/// Email 添付ファイル
/// </summary>
public class EmailAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
}

/// <summary>
/// Email 設定
/// </summary>
public class EmailConfig
{
    public bool Enabled { get; set; }
    public string IncomingServer { get; set; } = string.Empty;
    public int IncomingPort { get; set; } = 993;
    public bool IncomingUseSsl { get; set; } = true;
    public string IncomingUsername { get; set; } = string.Empty;
    public string IncomingPassword { get; set; } = string.Empty;
    
    public string OutgoingServer { get; set; } = string.Empty;
    public int OutgoingPort { get; set; } = 587;
    public bool OutgoingUseSsl { get; set; } = true;
    public string OutgoingUsername { get; set; } = string.Empty;
    public string OutgoingPassword { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "自動車ディーラー AI アシスタント";

    public int PollingIntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Email 渠道サービス実装
/// </summary>
public class EmailChannelService : IEmailChannelService
{
    private readonly EmailConfig _config;
    private readonly IConversationManager _conversationManager;
    private readonly IDirectAIProcessor _aiProcessor;
    private readonly IHandoverManager _handoverManager;
    private readonly ILogger<EmailChannelService> _logger;

    // 対話 ID マップ（Email スレッドごと）
    private static readonly ConcurrentDictionary<string, string> EmailConversationMap = new();

    public EmailChannelService(
        IOptions<EmailConfig> configOptions,
        IConversationManager conversationManager,
        IDirectAIProcessor aiProcessor,
        IHandoverManager handoverManager,
        ILogger<EmailChannelService> logger)
    {
        _config = configOptions.Value;
        _conversationManager = conversationManager;
        _aiProcessor = aiProcessor;
        _handoverManager = handoverManager;
        _logger = logger;
    }

    /// <summary>
    /// 受信メールを処理
    /// </summary>
    public async Task ProcessIncomingEmailAsync(EmailMessage email)
    {
        try
        {
            _logger.LogInformation("受信メール処理：{From} - {Subject}", email.From, email.Subject);

            // 顧客を識別
            var customerEmail = ExtractEmailAddress(email.From);
            if (string.IsNullOrEmpty(customerEmail))
            {
                _logger.LogWarning("有効なメールアドレスを抽出できません：{From}", email.From);
                return;
            }

            // 対話 ID を取得または作成
            var conversationId = await GetOrCreateConversationAsync(customerEmail, email.InReplyTo);

            // 直接 AI 処理
            var aiResult = await _aiProcessor.ProcessAsync(email.Body, new ConversationContext { ConversationId = conversationId });

            // エスカレーションが必要な場合
            if (aiResult.NeedsHandover)
            {
                await _handoverManager.CreateHandoverAsync(new HandoverRequest
                {
                    ConversationId = conversationId,
                    Reason = aiResult.HandoverReason ?? "ai_unable",
                    Priority = aiResult.Priority,
                    TargetDepartment = aiResult.TargetDepartment,
                    HandoverNotes = $"Email 受信：{email.Subject}\n感情：{aiResult.SentimentLabel} ({aiResult.SentimentScore:F2})"
                }, null);

                aiResult.Message = _handoverManager.GetHandoverMessage(aiResult.HandoverReason ?? "ai_unable");
            }

            // 応答メール作成
            var responseEmail = new EmailMessage
            {
                To = customerEmail,
                From = _config.FromAddress,
                Subject = GenerateSubject(email.Subject, aiResult.Method),
                Body = GenerateEmailBody(aiResult.Message, aiResult),
                IsHtml = true,
                InReplyTo = email.MessageId
            };

            // メール送信
            await SendResponseEmailAsync(responseEmail);

            _logger.LogInformation("応答メール送信済み：{To}", responseEmail.To);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "受信メール処理に失敗：{From}", email.From);
        }
    }

    /// <summary>
    /// 応答メールを送信
    /// </summary>
    public async Task SendResponseEmailAsync(EmailMessage email)
    {
        try
        {
            using var client = new SmtpClient(_config.OutgoingServer, _config.OutgoingPort)
            {
                Credentials = new NetworkCredential(_config.OutgoingUsername, _config.OutgoingPassword),
                EnableSsl = _config.OutgoingUseSsl
            };

            using var message = new MailMessage
            {
                From = new MailAddress(_config.FromAddress, _config.FromName),
                Subject = email.Subject,
                Body = email.Body,
                IsBodyHtml = email.IsHtml
            };

            message.To.Add(email.To);

            // InReplyTo ヘッダーは Headers ディクショナリに追加
            if (!string.IsNullOrEmpty(email.InReplyTo))
            {
                message.Headers.Add("In-Reply-To", email.InReplyTo);
            }

            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メール送信に失敗：{To}", email.To);
            throw;
        }
    }

    /// <summary>
    /// IMAP でメールを受信（バックグラウンドタスク用）
    /// </summary>
    public async Task<List<EmailMessage>> PollEmailsAsync()
    {
        var emails = new List<EmailMessage>();

        if (!_config.Enabled)
            return emails;

        try
        {
            using var client = new ImapClient();
            
            // IMAP サーバーに接続
            await client.ConnectAsync(
                _config.IncomingServer,
                _config.IncomingPort,
                _config.IncomingUseSsl);

            // 認証
            await client.AuthenticateAsync(
                _config.IncomingUsername,
                _config.IncomingPassword);

            // INBOX フォルダーを開く
            var inbox = client.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

            // 未読メールを検索
            var uids = await inbox.SearchAsync(SearchQuery.NotSeen);

            foreach (var uid in uids)
            {
                var message = await inbox.GetMessageAsync(uid);
                
                var email = new EmailMessage
                {
                    MessageId = message.MessageId ?? Guid.NewGuid().ToString(),
                    From = message.From.ToString(),
                    To = message.To.ToString(),
                    Subject = message.Subject ?? "(無題)",
                    Body = GetEmailBody(message),
                    IsHtml = message.HtmlBody != null,
                    ReceivedAt = message.Date.LocalDateTime,
                    InReplyTo = message.InReplyTo
                };

                // 添付ファイル処理
                foreach (var attachment in message.Attachments)
                {
                    if (attachment is MimePart mimePart)
                    {
                        email.Attachments.Add(new EmailAttachment
                        {
                            FileName = mimePart.FileName,
                            ContentType = mimePart.ContentType?.MimeType ?? "application/octet-stream",
                            Size = mimePart.Content?.Stream?.Length ?? 0
                        });
                    }
                }

                emails.Add(email);

                // 既読マークを付与
                await inbox.SetFlagsAsync(uid, MessageFlags.Seen, true);
            }

            client.Disconnect(true);
            
            _logger.LogInformation("{Count} 件のメールを受信", emails.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メールポーリングに失敗");
        }

        return emails;
    }

    /// <summary>
    /// メール本文を取得
    /// </summary>
    private static string GetEmailBody(MimeMessage message)
    {
        // HTML 版を優先、なければテキスト版
        if (!string.IsNullOrEmpty(message.HtmlBody))
            return message.HtmlBody;
        
        if (!string.IsNullOrEmpty(message.TextBody))
            return message.TextBody;

        // マルチパートの場合
        if (message.Body is Multipart multipart)
        {
            foreach (var part in multipart)
            {
                if (part is MimePart mimePart && mimePart.ContentType.IsMimeType("text", "plain"))
                {
                    using var reader = new StreamReader(mimePart.Content.Stream);
                    return reader.ReadToEnd();
                }
            }
        }

        return string.Empty;
    }

    private async Task<string> GetOrCreateConversationAsync(string customerEmail, string? inReplyTo)
    {
        // スレッド ID で対話 ID を検索
        if (!string.IsNullOrEmpty(inReplyTo) && EmailConversationMap.TryGetValue(inReplyTo, out var existingId))
        {
            return existingId;
        }

        // 新しい対話を開始
        var conversation = await _conversationManager.StartConversationAsync(
            new StartConversationRequest { Channel = "email" });

        EmailConversationMap[customerEmail] = conversation.ConversationId;
        return conversation.ConversationId;
    }

    private static string? ExtractEmailAddress(string fromHeader)
    {
        var match = Regex.Match(fromHeader, @"<([^>]+)>");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
        // <> がない場合はそのまま返す
        return fromHeader.Trim();
    }

    private static string GenerateSubject(string originalSubject, string intent)
    {
        var prefix = intent switch
        {
            "appointment_booking" => "[予約受付]",
            "appointment_change" => "[予約変更]",
            "appointment_cancel" => "[予約キャンセル]",
            "inquiry" => "[お問い合わせ]",
            "complaint" => "[苦情対応]",
            _ => "[自動応答]"
        };

        if (originalSubject.StartsWith("Re:"))
        {
            return originalSubject;
        }

        return $"{prefix} Re: {originalSubject}";
    }

    private static string GenerateEmailBody(string responseMessage, AIProcessingResult aiResult)
    {
        var html = $@"
<html>
<head>
    <style>
        body {{ font-family: 'Hiragino Kaku Gothic ProN', 'メイリオ', sans-serif; line-height: 1.6; }}
        .signature {{ border-top: 1px solid #ccc; padding-top: 10px; margin-top: 20px; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <p>お客様</p>
    <p>お問い合わせありがとうございます。<br>
    自動車ディーラー AI アシスタントです。</p>
    <hr style='border: none; border-top: 1px solid #eee;'>
    <p>{responseMessage.Replace("\n", "<br>")}</p>
    <hr style='border: none; border-top: 1px solid #eee;'>
    <div class='signature'>
        <p>────────────────────────────<br>
        <strong>自動車ディーラー AI アシスタント</strong><br>
        営業時間：平日 9:00-19:00 / 土日祝 9:00-18:00<br>
        定休日：水曜日<br>
        TEL: 03-XXXX-XXXX<br>
        ────────────────────────────</p>
        <p style='font-size: 11px;'>
        ※本メールは AI により自動生成されています。<br>
        緊急のお問い合わせはお電話にてご連絡ください。
        </p>
    </div>
</body>
</html>";

        return html;
    }

    private static string GenerateHandoverEmailBody()
    {
        return @"
<html>
<head>
    <style>
        body {{ font-family: 'Hiragino Kaku Gothic ProN', 'メイリオ', sans-serif; line-height: 1.6; }}
        .notice {{ background: #fff3cd; padding: 15px; border-radius: 5px; margin: 20px 0; }}
    </style>
</head>
<body>
    <p>お客様</p>
    <p>お問い合わせありがとうございます。</p>
    
    <div class='notice'>
        <strong>重要なお知らせ</strong><br>
        お客様のお問い合わせ内容につきまして、<br>
        専門スタッフが確認の上、改めてご連絡させていただきます。<br>
        今しばらくお待ちくださいませ。
    </div>
    
    <p>通常、1-2 営業日以内に回答を差し上げます。<br>
    緊急の場合は、お手数ですが 03-XXXX-XXXX までお電話ください。</p>
    
    <p>何卒よろしくお願い申し上げます。</p>
</body>
</html>";
    }
}

/// <summary>
/// Email ポーリングバックグラウンドサービス
/// </summary>
public class EmailPollingBackgroundService : BackgroundService
{
    private readonly IEmailChannelService _emailService;
    private readonly EmailConfig _config;
    private readonly ILogger<EmailPollingBackgroundService> _logger;

    public EmailPollingBackgroundService(
        IEmailChannelService emailService,
        IOptions<EmailConfig> configOptions,
        ILogger<EmailPollingBackgroundService> logger)
    {
        _emailService = emailService;
        _config = configOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email ポーリングサービス開始");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_config.Enabled)
            {
                try
                {
                    var emails = await _emailService.PollEmailsAsync();
                    
                    foreach (var email in emails)
                    {
                        await _emailService.ProcessIncomingEmailAsync(email);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Email ポーリング処理中にエラーが発生");
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(_config.PollingIntervalMinutes), stoppingToken);
        }

        _logger.LogInformation("Email ポーリングサービス終了");
    }
}
