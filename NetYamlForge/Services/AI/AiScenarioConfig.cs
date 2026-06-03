using System;
using System.Collections.Generic;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI 场景场景 YAML 配置结构
/// </summary>
public class AiScenarioConfig
{
    public Dictionary<string, ScenarioConfig> Scenarios { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> AllowedEntities { get; set; } = new();
    public List<string> AllowedActions { get; set; } = new();
}

public class ScenarioConfig
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InitialState { get; set; } = "Init";
    public List<SlotConfig> RequiredSlots { get; set; } = new();
    public List<SlotConfig> OptionalSlots { get; set; } = new();
    public Dictionary<string, List<string>> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TransitionConfig> Transitions { get; set; } = new();
}

public class TransitionConfig
{
    public string From { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class SlotConfig
{
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string? ValidationPattern { get; set; }
    public List<string>? AllowedValues { get; set; }
    public string? Trigger { get; set; }
}
