using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NetYamlForge.Services.Diagnostics;

/// <summary>
/// NetYamlForge の統一テレメトリ計測点（OpenTelemetry メトリクスとアクティビティ）。
/// </summary>
public static class ForgeTelemetry
{
    public const string ServiceName = "NetYamlForge";
    private static readonly string AssemblyVersion = typeof(ForgeTelemetry).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    public static readonly ActivitySource Source = new(ServiceName, AssemblyVersion);
    public static readonly Meter Meter = new(ServiceName, AssemblyVersion);

    // DynamicEntity Metrics
    public static readonly Histogram<double> EntityQueryDuration =
        Meter.CreateHistogram<double>("forge.entity.query.duration", unit: "ms");

    public static readonly Histogram<long> EntityQueryRows =
        Meter.CreateHistogram<long>("forge.entity.query.rows");

    // Hook compile Metrics
    public static readonly Histogram<double> HookCompileDuration =
        Meter.CreateHistogram<double>("forge.hook.compile.duration", unit: "ms");

    public static readonly Counter<long> HookCompileErrors =
        Meter.CreateCounter<long>("forge.hook.compile.errors");

    // Batch Metrics
    public static readonly Histogram<double> BatchStepDuration =
        Meter.CreateHistogram<double>("forge.batch.step.duration", unit: "ms");

    public static readonly Counter<long> BatchFailures =
        Meter.CreateCounter<long>("forge.batch.failures");

    // AI Metrics
    public static readonly Histogram<double> AiCliDuration =
        Meter.CreateHistogram<double>("forge.ai.cli.duration", unit: "ms");

    public static readonly Counter<long> AiCliFallback =
        Meter.CreateCounter<long>("forge.ai.cli.fallback");

    public static readonly Counter<long> AiTokens =
        Meter.CreateCounter<long>("forge.ai.tokens");
}
