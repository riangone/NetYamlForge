using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Page;
using NetYamlForge.Services.Auth;

namespace NetYamlForge.Services.AI.PageActions;

public class SwitchProviderAction : IPageActionHandler
{
    public string ActionName => "switch-provider";
    public string? Project => null; // Global

    public async Task<IActionResult> HandleAsync(PageActionContext ctx)
    {
        var userIsAdmin = ctx.User?.IsInRole("Admin") ?? false;
        if (!userIsAdmin)
        {
            return new ForbidResult();
        }

        ctx.Query.TryGetValue("type", out var type);
        ctx.Query.TryGetValue("provider", out var provider);

        var allowedAnnotation = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "lmstudio", "ollama", "gemini", "antigravity" };
        var allowedEmbedding = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "lmstudio", "gemini" };

        var typeNorm = (type ?? "").ToLowerInvariant();
        var providerNorm = (provider ?? "").ToLowerInvariant();

        string? settingKey = typeNorm switch
        {
            "annotation" when allowedAnnotation.Contains(providerNorm) => "annotation_provider",
            "embedding"  when allowedEmbedding.Contains(providerNorm)  => "embedding_provider",
            _ => null
        };

        if (settingKey == null)
        {
            return new BadRequestObjectResult("无效的提供商类型或名称");
        }

        var sectionGroup = typeNorm == "annotation" ? "annotation" : "embedding";

        var db = ctx.Services.GetRequiredService<IDbConnection>();
        var audit = ctx.Services.GetRequiredService<IAuditLogService>();
        var logger = ctx.Services.GetRequiredService<ILogger<SwitchProviderAction>>();

        var updated = await db.ExecuteAsync("""
            UPDATE project_settings
            SET value = @V, updated_at = datetime('now')
            WHERE setting_key = @K
            """, new { K = settingKey, V = providerNorm });

        if (updated == 0)
        {
            await db.ExecuteAsync("""
                INSERT OR IGNORE INTO project_settings
                    (section_group, setting_key, label, value, default_value, description, updated_at)
                VALUES (@SG, @K, @K, @V, @V, '', datetime('now'))
                """, new { SG = sectionGroup, K = settingKey, V = providerNorm });
        }

        try
        {
            await audit.WriteAsync("switch_provider", "project_settings", $"Page={ctx.PageName},Type={typeNorm},Provider={providerNorm}", ctx.User.Identity?.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audit write failed for action=switch_provider, entity=project_settings");
        }

        return new RedirectResult($"/{ctx.Project}/Page/{ctx.PageName}");
    }
}
