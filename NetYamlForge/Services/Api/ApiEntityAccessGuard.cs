using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Models;

namespace NetYamlForge.Services.Api;

public class ApiEntityAccessGuard
{
    public IActionResult? ValidateApiAccess(EntityDefinition meta, bool writeRequired)
    {
        var apiMode = (meta.Api ?? "disabled").ToLowerInvariant();
        if (apiMode == "disabled")
        {
            return new ObjectResult(new ProblemDetails
            {
                Title = "API Access Disabled",
                Detail = $"API access to entity '{meta.Table}' is disabled by configuration.",
                Status = StatusCodes.Status403Forbidden
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
        if (writeRequired && apiMode == "readonly")
        {
            return new ObjectResult(new ProblemDetails
            {
                Title = "API Read-Only",
                Detail = $"API access to entity '{meta.Table}' is read-only.",
                Status = StatusCodes.Status403Forbidden
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
        return null;
    }
}
