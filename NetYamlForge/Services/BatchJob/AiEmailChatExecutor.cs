using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MimeKit;
using NetYamlForge.Services.Email;
using NetYamlForge.Services.Ai;

namespace NetYamlForge.Services.BatchJob;

public class AiEmailChatExecutor
{
    private readonly IGeminiCliService _geminiCli;
    private readonly ILogger<AiEmailChatExecutor> _logger;

    public AiEmailChatExecutor(IGeminiCliService geminiCli, ILogger<AiEmailChatExecutor> logger)
    {
        _geminiCli = geminiCli;
        _logger = logger;
    }

    public async Task<BatchJobResult> ExecuteAsync(
        BatchJobDefinition job,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchJobResult
        {
            JobId = job.Id,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("AI Email Chat タスク開始：{JobId} (Project: {Project})", job.Id, projectName);

            // 1. Load .env
            var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "projects", projectName);
            var envPath = Path.Combine(projectDir, ".env");
            
            if (!File.Exists(envPath))
            {
                throw new FileNotFoundException($".env file not found at {envPath}");
            }

            var env = LoadEnv(envPath);

            var imapServer = env.GetValueOrDefault("IMAP_SERVER");
            var imapPort = int.Parse(env.GetValueOrDefault("IMAP_PORT", "993"));
            var imapSsl = bool.Parse(env.GetValueOrDefault("IMAP_SSL", "true"));
            var imapUser = env.GetValueOrDefault("IMAP_USER");
            var imapPass = env.GetValueOrDefault("IMAP_PASSWORD");

            var smtpServer = env.GetValueOrDefault("SMTP_SERVER");
            var smtpPort = int.Parse(env.GetValueOrDefault("SMTP_PORT", "587"));
            var smtpSsl = bool.Parse(env.GetValueOrDefault("SMTP_SSL", "false"));
            var smtpUser = env.GetValueOrDefault("SMTP_USER");
            var smtpPass = env.GetValueOrDefault("SMTP_PASSWORD");
            var fromName = env.GetValueOrDefault("SMTP_FROM_NAME", "AI Assistant");
            var fromAddress = env.GetValueOrDefault("SMTP_FROM_ADDRESS", smtpUser);

            var aiEndpoint = env.GetValueOrDefault("AI_API_ENDPOINT", "https://api.openai.com/v1/chat/completions");
            var aiKey = env.GetValueOrDefault("AI_API_KEY");
            var aiModel = env.GetValueOrDefault("AI_MODEL", "gpt-3.5-turbo");
            var aiSystemPrompt = env.GetValueOrDefault("AI_SYSTEM_PROMPT", "You are a helpful AI assistant. Reply to the user's email.");
            var targetSender = env.GetValueOrDefault("TARGET_SENDER_EMAIL");
            var useGeminiCli = bool.Parse(env.GetValueOrDefault("USE_GEMINI_CLI", "false"));
            var invoiceKeywordsRaw = env.GetValueOrDefault("INVOICE_SUBJECT_KEYWORDS", "");
            var invoiceKeywords = invoiceKeywordsRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            if (!useGeminiCli && (string.IsNullOrEmpty(imapServer) || string.IsNullOrEmpty(smtpServer) || string.IsNullOrEmpty(aiKey)))
            {
                throw new InvalidOperationException("Missing required .env configuration (IMAP/SMTP/AI_API_KEY).");
            }

            // 2. Fetch Emails
            using var imapClient = new ImapClient();
            await imapClient.ConnectAsync(imapServer, imapPort, imapSsl, cancellationToken);
            await imapClient.AuthenticateAsync(imapUser, imapPass, cancellationToken);
            
            var inbox = imapClient.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadWrite, cancellationToken);
            
            var uids = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
            var rowsAffected = 0;

            foreach (var uid in uids.OrderBy(x => x.Id).Take(10)) // Max 10 per batch
            {
                var message = await inbox.GetMessageAsync(uid, cancellationToken);
                var senderEmail = message.From.Mailboxes.FirstOrDefault()?.Address ?? "";
                
                // If TARGET_SENDER_EMAIL is specified, only reply to that address (supports comma-separated list)
                if (!string.IsNullOrEmpty(targetSender))
                {
                    var allowedSenders = targetSender.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!allowedSenders.Any(s => s.Equals(senderEmail, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation("Skipping email from {Sender} (does not match TARGET_SENDER_EMAIL)", senderEmail);
                        continue;
                    }
                }
                
                // Skip emails that should be handled by invoice_email_processor
                if (invoiceKeywords.Length > 0 && invoiceKeywords.Any(kw =>
                    message.Subject.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Skipping invoice email (subject matched INVOICE_SUBJECT_KEYWORDS): {Subject}", message.Subject);
                    continue;
                }

                var body = message.TextBody ?? message.HtmlBody ?? "";
                _logger.LogInformation("Processing email from {Sender}: {Subject}", senderEmail, message.Subject);

                // 3. Call AI
                string? replyContent = null;
                if (useGeminiCli)
                {
                    var prompt = $"System: {aiSystemPrompt}\n\nUser Email Content:\n{body}";
                    replyContent = await _geminiCli.PromptAsync(prompt, aiModel, projectName, cancellationToken);
                }
                else if (!string.IsNullOrEmpty(aiKey))
                {
                    replyContent = await GenerateAiReplyAsync(aiEndpoint, aiKey, aiModel, aiSystemPrompt, body, cancellationToken);
                }
                
                if (string.IsNullOrEmpty(replyContent))
                {
                    _logger.LogWarning("AI returned empty reply for email {MessageId}", message.MessageId);
                    continue;
                }

                // 4. Send Reply
                var replyMessage = new MimeMessage();
                replyMessage.From.Add(new MailboxAddress(fromName, fromAddress ?? smtpUser ?? "ai@assistant.local"));
                replyMessage.To.Add(message.From.Mailboxes.First());
                replyMessage.Subject = message.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) 
                    ? message.Subject 
                    : $"Re: {message.Subject}";
                
                if (!string.IsNullOrEmpty(message.MessageId))
                {
                    replyMessage.InReplyTo = message.MessageId;
                    replyMessage.References.Add(message.MessageId);
                }

                var builder = new BodyBuilder { TextBody = replyContent };
                replyMessage.Body = builder.ToMessageBody();

                using var smtpClient = new SmtpClient();
                await smtpClient.ConnectAsync(smtpServer, smtpPort, smtpSsl, cancellationToken);
                await smtpClient.AuthenticateAsync(smtpUser, smtpPass, cancellationToken);
                await smtpClient.SendAsync(replyMessage, cancellationToken);
                await smtpClient.DisconnectAsync(true, cancellationToken);
                
                _logger.LogInformation("Sent AI reply to {Sender}", senderEmail);

                // 5. Mark as read
                await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
                rowsAffected++;
            }

            await imapClient.DisconnectAsync(true, cancellationToken);

            result.Success = true;
            result.RowsAffected = rowsAffected;
            result.EndedAt = DateTime.UtcNow;
            
            _logger.LogInformation("AI Email Chat タスク完了：{JobId}, 処理数: {Rows}", job.Id, rowsAffected);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Email Chat エラー：{JobId}", job.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndedAt = DateTime.UtcNow;
            return result;
        }
    }

    private Dictionary<string, string> LoadEnv(string filePath)
    {
        var env = new Dictionary<string, string>();
        if (!File.Exists(filePath)) return env;

        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
            
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = trimmed.Substring(0, eqIndex).Trim();
                var value = trimmed.Substring(eqIndex + 1).Trim();
                
                // Handle inline comments
                var commentIndex = value.IndexOf('#');
                if (commentIndex >= 0)
                {
                    value = value.Substring(0, commentIndex).Trim();
                }

                // Remove quotes if present
                if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                {
                    value = value.Substring(1, value.Length - 2);
                }
                env[key] = value;
            }
        }
        return env;
    }

    private async Task<string> GenerateAiReplyAsync(
        string endpoint, 
        string apiKey, 
        string model, 
        string systemPrompt,
        string userMessage, 
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var payload = new
        {
            model = model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(endpoint, content, cancellationToken);
        
        response.EnsureSuccessStatusCode();
        
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseJson);
        
        var replyText = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return replyText ?? "";
    }
}
