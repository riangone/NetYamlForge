namespace NetYamlForge.Services.AI;

/// <summary>
/// 场景无关的对话状态机抽象。
/// 具体场景（试驾预约、维修预约等）各自实现此接口，
/// AiToolOrchestrator 仅依赖此接口而非具体类型。
/// </summary>
public interface IConversationFsm
{
    string CurrentState { get; }
    bool IsTerminal { get; }
    bool IsEscalated { get; }
    IReadOnlySet<string> AllowedTools { get; }
    int LowConfidenceCount { get; }
    void FireTrigger(string trigger, double confidence = 1.0);
    void TriggerLowConfidence(double confidence);
}
