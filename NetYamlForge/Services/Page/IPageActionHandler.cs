using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Services.Page;

public interface IPageActionHandler
{
    string ActionName { get; }              // "annotate-photo"
    string? Project { get; }                // null = 全局；否则限定项目
    Task<IActionResult> HandleAsync(PageActionContext ctx);
}

public sealed record PageActionContext(
    string Project,
    string PageName,
    IReadOnlyDictionary<string, string?> Query,
    IServiceProvider Services,
    ClaimsPrincipal User,
    HttpContext HttpContext);
