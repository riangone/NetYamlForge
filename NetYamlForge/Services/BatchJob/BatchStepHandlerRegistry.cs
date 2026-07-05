using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NetYamlForge.Services.BatchJob;

/// <summary>
/// 批处理步骤注册项
/// </summary>
public class BatchStepHandlerRegistration
{
    public string StepType { get; }
    public Type HandlerType { get; }

    public BatchStepHandlerRegistration(string stepType, Type handlerType)
    {
        StepType = stepType;
        HandlerType = handlerType;
    }
}

/// <summary>
/// 批处理步骤路由解析注册表
/// </summary>
public class BatchStepHandlerRegistry
{
    private readonly ConcurrentDictionary<string, Type> _handlers;

    public BatchStepHandlerRegistry(IEnumerable<BatchStepHandlerRegistration> registrations)
    {
        _handlers = new ConcurrentDictionary<string, Type>(
            registrations.ToDictionary(r => r.StepType, r => r.HandlerType, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase
        );
    }

    public void Register(string stepType, Type handlerType)
    {
        _handlers[stepType] = handlerType;
    }

    public bool TryGetHandlerType(string stepType, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Type? handlerType)
    {
        return _handlers.TryGetValue(stepType, out handlerType);
    }

    public IEnumerable<string> RegisteredTypes => _handlers.Keys;
}
