using NetYamlForge.Models.Email;

namespace NetYamlForge.Services.Email;

public interface IEmailService
{
    Task SendEmailAsync(EmailMessage message);
    Task<List<EmailMessage>> FetchUnreadEmailsAsync(int limit = 10);
    Task MarkAsReadAsync(string messageId);
    Task DeleteEmailAsync(string messageId);
}
