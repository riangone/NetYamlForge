using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.Diagnostics;

namespace NetYamlForge.Services.BatchJob.Sdk;

/// <summary>
/// バッチジョブ実行中の一連の処理における可観測性（Logging Scope + Tracing Span + Metrics）を
/// using ブロックで一括管理するスコープ。
/// </summary>
public sealed class BatchTelemetryScope : IDisposable
{
    private readonly Activity? _activity;
    private readonly IDisposable? _logScope;
    private readonly Stopwatch _stopwatch;
    private readonly string _stepType;
    private readonly string? _jobName;
    private bool _failed;

    public BatchTelemetryScope(
        ILogger logger,
        string stepType,
        string? projectName,
        string? jobName)
    {
        _stepType = stepType;
        _jobName = jobName;
        _stopwatch = Stopwatch.StartNew();

        _activity = ForgeTelemetry.Source.StartActivity($"forge.batch.{stepType}");
        if (_activity != null)
        {
            _activity.SetTag("project", projectName);
            _activity.SetTag("job", jobName);
            _activity.SetTag("step", stepType);
        }

        _logScope = logger.BeginScope(
            projectId: projectName,
            entity: null,
            hook: stepType,
            correlationId: _activity?.TraceId.ToString());
    }

    public void RecordError(string error)
    {
        _failed = true;
        if (_activity != null)
        {
            _activity.SetStatus(ActivityStatusCode.Error, error);
            _activity.SetTag("error", error);
        }
    }

    public void Dispose()
    {
        _stopwatch.Stop();
        var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;

        if (_activity != null)
        {
            _activity.SetTag("duration_ms", elapsedMs);
            _activity.Dispose();
        }

        ForgeTelemetry.BatchStepDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("step", _stepType),
            new KeyValuePair<string, object?>("job", _jobName));

        if (_failed)
        {
            ForgeTelemetry.BatchFailures.Add(1,
                new KeyValuePair<string, object?>("step", _stepType),
                new KeyValuePair<string, object?>("job", _jobName));
        }

        _logScope?.Dispose();
    }
}
