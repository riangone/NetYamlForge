using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using Microsoft.Extensions.Options;
using MimeKit;
using NetYamlForge.Models.Config;
using NetYamlForge.Models.Email;

namespace NetYamlForge.Services.Email;

public class MailKitEmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<MailKitEmailService> _logger;

    public MailKitEmailService(IOptions<EmailSettings> settings, ILogger<MailKitEmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(EmailMessage message)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(_settings.Smtp.FromName, _settings.Smtp.FromAddress ?? _settings.Smtp.User));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;

        if (!string.IsNullOrEmpty(message.InReplyTo))
        {
            mimeMessage.InReplyTo = message.InReplyTo;
        }

        if (!string.IsNullOrEmpty(message.References))
        {
            mimeMessage.References.Add(message.References);
        }

        var bodyBuilder = new BodyBuilder();
        if (message.IsHtml)
        {
            bodyBuilder.HtmlBody = message.Body;
        }
        else
        {
            bodyBuilder.TextBody = message.Body;
        }

        foreach (var attachment in message.Attachments)
        {
            bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_settings.Smtp.Server, _settings.Smtp.Port, _settings.Smtp.UseSsl);
            await client.AuthenticateAsync(_settings.Smtp.User, _settings.Smtp.Password);
            await client.SendAsync(mimeMessage);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Email sent successfully to {To}", message.To);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", message.To);
            throw;
        }
    }

    public async Task<List<EmailMessage>> FetchUnreadEmailsAsync(int limit = 10)
    {
        var emails = new List<EmailMessage>();
        using var client = new ImapClient();
        try
        {
            await client.ConnectAsync(_settings.Imap.Server, _settings.Imap.Port, _settings.Imap.UseSsl);
            await client.AuthenticateAsync(_settings.Imap.User, _settings.Imap.Password);

            var inbox = client.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

            var uids = await inbox.SearchAsync(SearchQuery.NotSeen);
            var count = 0;
            foreach (var uid in uids.OrderByDescending(x => x.Id))
            {
                if (count >= limit) break;

                var message = await inbox.GetMessageAsync(uid);
                var email = new EmailMessage
                {
                    Id = uid.ToString(),
                    From = message.From?.ToString() ?? string.Empty,
                    To = message.To?.ToString() ?? string.Empty,
                    Subject = message.Subject ?? string.Empty,
                    Body = (message.HtmlBody ?? message.TextBody) ?? string.Empty,
                    IsHtml = !string.IsNullOrEmpty(message.HtmlBody),
                    Date = message.Date,
                    MessageId = message.MessageId ?? string.Empty,
                    InReplyTo = message.InReplyTo ?? string.Empty,
                    References = string.Join(" ", message.References ?? new MessageIdList())
                };

                foreach (var attachment in message.Attachments)
                {
                    if (attachment is MimePart part)
                    {
                        using var ms = new MemoryStream();
                        await part.Content.DecodeToAsync(ms);
                        email.Attachments.Add(new EmailAttachment
                        {
                            FileName = part.FileName ?? "unnamed",
                            Content = ms.ToArray(),
                            ContentType = part.ContentType?.MimeType ?? "application/octet-stream"
                        });
                    }
                }

                emails.Add(email);
                count++;
            }

            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch unread emails");
            throw;
        }

        return emails;
    }

    public async Task MarkAsReadAsync(string messageId)
    {
        using var client = new ImapClient();
        try
        {
            await client.ConnectAsync(_settings.Imap.Server, _settings.Imap.Port, _settings.Imap.UseSsl);
            await client.AuthenticateAsync(_settings.Imap.User, _settings.Imap.Password);

            var inbox = client.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadWrite);

            if (UniqueId.TryParse(messageId, out var uid))
            {
                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true);
            }

            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark email as read: {MessageId}", messageId);
            throw;
        }
    }

    public async Task DeleteEmailAsync(string messageId)
    {
        using var client = new ImapClient();
        try
        {
            await client.ConnectAsync(_settings.Imap.Server, _settings.Imap.Port, _settings.Imap.UseSsl);
            await client.AuthenticateAsync(_settings.Imap.User, _settings.Imap.Password);

            var inbox = client.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadWrite);

            if (UniqueId.TryParse(messageId, out var uid))
            {
                await inbox.AddFlagsAsync(uid, MessageFlags.Deleted, true);
                await inbox.ExpungeAsync();
            }

            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete email: {MessageId}", messageId);
            throw;
        }
    }
}
