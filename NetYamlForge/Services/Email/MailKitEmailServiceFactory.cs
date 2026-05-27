using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.Config;

namespace NetYamlForge.Services.Email;

/// <summary>
/// Singleton ファクトリ。プロジェクト名ごとに IEmailService インスタンスをキャッシュして返す。
/// プロジェクト固有の email: 設定はグローバル設定を上書き（未設定項目はグローバルから継承）する。
/// </summary>
public class MailKitEmailServiceFactory : IEmailServiceFactory
{
    private readonly EmailSettings _globalSettings;
    private readonly ProjectManager _projectManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEmailService _globalService;
    private readonly ConcurrentDictionary<string, IEmailService> _cache = new(StringComparer.OrdinalIgnoreCase);

    public MailKitEmailServiceFactory(
        IOptions<EmailSettings> globalSettings,
        ProjectManager projectManager,
        ILoggerFactory loggerFactory,
        IEmailService globalService)
    {
        _globalSettings = globalSettings.Value;
        _projectManager = projectManager;
        _loggerFactory = loggerFactory;
        _globalService = globalService;
    }

    public IEmailService GetForProject(string? projectName)
    {
        if (string.IsNullOrEmpty(projectName))
            return _globalService;

        if (!_projectManager.TryGet(projectName, out var info) || info!.Email == null)
            return _globalService;

        return _cache.GetOrAdd(projectName, _ => BuildService(info.Email!));
    }

    private IEmailService BuildService(NetYamlForge.Models.ProjectEmailConfig projectEmail)
    {
        var settings = new EmailSettings
        {
            Smtp = new SmtpSettings
            {
                Server    = projectEmail.Smtp?.Server    ?? _globalSettings.Smtp.Server,
                Port      = projectEmail.Smtp?.Port      ?? _globalSettings.Smtp.Port,
                UseSsl    = projectEmail.Smtp?.UseSsl    ?? _globalSettings.Smtp.UseSsl,
                User      = projectEmail.Smtp?.User      ?? _globalSettings.Smtp.User,
                Password  = projectEmail.Smtp?.Password  ?? _globalSettings.Smtp.Password,
                FromName  = projectEmail.Smtp?.FromName  ?? _globalSettings.Smtp.FromName,
                FromAddress = projectEmail.Smtp?.FromAddress ?? _globalSettings.Smtp.FromAddress
            },
            Imap = new ImapSettings
            {
                Server   = projectEmail.Imap?.Server   ?? _globalSettings.Imap.Server,
                Port     = projectEmail.Imap?.Port     ?? _globalSettings.Imap.Port,
                UseSsl   = projectEmail.Imap?.UseSsl   ?? _globalSettings.Imap.UseSsl,
                User     = projectEmail.Imap?.User     ?? _globalSettings.Imap.User,
                Password = projectEmail.Imap?.Password ?? _globalSettings.Imap.Password
            }
        };

        var logger = _loggerFactory.CreateLogger<MailKitEmailService>();
        return new MailKitEmailService(Options.Create(settings), logger);
    }
}
