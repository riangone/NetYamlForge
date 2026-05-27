using System.Data;
using Microsoft.AspNetCore.Http;
using NetYamlForge.Models.Email;
using NetYamlForge.Services.Email;

namespace NetYamlForge.Services.Hooks;

/// <summary>
/// [汎用通知] データ操作後にメールを送信するフック。
/// プロジェクト固有の email: 設定が project.yaml にあればそちらを使用する。
///
/// entities.yml での使用例:
///   hooks:
///     afterCreate: "send_email:ToField,Subject,Body"
/// </summary>
public class SendEmailHook : IEntityHook
{
    private readonly IEmailServiceFactory _emailFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SendEmailHook> _logger;

    public SendEmailHook(
        IEmailServiceFactory emailFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SendEmailHook> logger)
    {
        _emailFactory = emailFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public string Name => "send_email";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var config = GetConfig(ctx);
        if (config == null) return;

        var (toField, subjectTemplate, bodyTemplate) = config.Value;

        // 宛先の取得
        if (!ctx.Values.TryGetValue(toField, out var toValue) || string.IsNullOrEmpty(toValue?.ToString()))
        {
            _logger.LogWarning("[Hook:send_email] Recipient field '{Field}' is empty", toField);
            return;
        }
        var to = toValue.ToString()!;

        // テンプレート変数の置換
        var subject = ReplaceTemplates(subjectTemplate, ctx.Values);
        var body = ReplaceTemplates(bodyTemplate, ctx.Values);

        // 現在のプロジェクト名を HttpContext のスコープから取得
        var projectName = _httpContextAccessor.HttpContext?.RequestServices
            ?.GetService<ProjectScope>()?.Current?.Name;
        var emailService = _emailFactory.GetForProject(projectName);

        try
        {
            await emailService.SendEmailAsync(new EmailMessage
            {
                To = to,
                Subject = subject,
                Body = body,
                IsHtml = body.Contains("<html", StringComparison.OrdinalIgnoreCase) || body.Contains("<p>", StringComparison.OrdinalIgnoreCase)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Hook:send_email] Failed to send email to {To}", to);
        }
    }

    private (string toField, string subject, string body)? GetConfig(EntityHookContext ctx)
    {
        if (ctx.Data.TryGetValue("__hookConfig", out var config) && config is string s)
        {
            var parts = s.Split(':', 3);
            if (parts.Length == 3)
            {
                return (parts[0], parts[1], parts[2]);
            }
        }
        return null;
    }

    private string ReplaceTemplates(string template, IDictionary<string, object?> values)
    {
        var result = template;
        foreach (var kvp in values)
        {
            var placeholder = "{" + kvp.Key + "}";
            result = result.Replace(placeholder, kvp.Value?.ToString() ?? "");
        }
        return result;
    }
}
