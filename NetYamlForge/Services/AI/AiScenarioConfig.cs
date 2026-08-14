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

    /// <summary>providers セクション（AI プロバイダー別の接続設定）。</summary>
    public Dictionary<string, AiProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>既定の标注プロバイダー（lmstudio / ollama / gemini / antigravity など）。</summary>
    public string? DefaultAnnotationProvider { get; set; }

    /// <summary>标注用プロンプト（プロジェクト固有の上書き）。</summary>
    public string? AnnotationPrompt { get; set; }
}

/// <summary>
/// AI プロバイダー別の接続設定（scenarios.yaml の providers セクションに対応）。
/// </summary>
public class AiProviderConfig
{
    public bool Enabled { get; set; } = true;
    public string? DisplayName { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public string? VisionModel { get; set; }
    public string? TextModel { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiKeyEnv { get; set; }
    public int MaxTokens { get; set; } = 1024;
    public double Temperature { get; set; } = 0.2;
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
