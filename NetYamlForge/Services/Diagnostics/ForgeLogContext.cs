using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Diagnostics;

public static class ForgeLog
{
    // 统一 EventId 段位，便于按类别检索
    public static readonly EventId HookFailure = new(4100, "HookFailure");
    public static readonly EventId CompileFailure = new(4101, "CompileFailure");
    public static readonly EventId EntityIoFailure = new(4102, "EntityIoFailure");

    // 统一结构化字段的 scope
    public static IDisposable? BeginScope(this ILogger logger,
        string? projectId = null,
        string? entity = null,
        string? hook = null,
        string? correlationId = null)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["ProjectId"] = projectId,
            ["Entity"] = entity,
            ["Hook"] = hook,
            ["CorrelationId"] = correlationId ?? Activity.Current?.TraceId.ToString(),
        });
    }
}
