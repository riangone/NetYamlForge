public class AiWindowConfig
{
    public LlmConfig Llm { get; set; } = new();
    public IntentConfig Intent { get; set; } = new();
    public HandoverConfig Handover { get; set; } = new();
}

/// <summary>
/// LLM 設定
/// </summary>
public class LlmConfig
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "qwen";
    public string Model { get; set; } = "qwen2.5-coder:7b";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public int TimeoutSeconds { get; set; } = 3600;
}

/// <summary>
/// 意図分類設定
/// </summary>
public class IntentConfig
{
    public bool RuleBasedEnabled { get; set; } = true;
    public bool LlmEnabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.6;
}

/// <summary>
/// エスカレーション設定
/// </summary>
public class HandoverConfig
{
    public bool AutoEnabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.6;
    public double NegativeSentimentThreshold { get; set; } = -0.5;
    public bool VipAutoHandover { get; set; } = true;
}
