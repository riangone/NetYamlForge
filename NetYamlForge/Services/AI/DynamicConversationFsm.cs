using System;
using System.Collections.Generic;
using System.Linq;
using Stateless;
using Stateless.Graph;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 通用的、由 YAML 配置驱动的对话状态机。
/// 动态构建状态流转关系，解除硬编码。
/// </summary>
public class DynamicConversationFsm : IConversationFsm
{
    private readonly StateMachine<string, string> _machine;
    private readonly string _conversationId;
    private readonly ScenarioConfig _config;
    private int _lowConfidenceCount = 0;

    public DynamicConversationFsm(string conversationId, ScenarioConfig config, string? initialState = null)
    {
        _conversationId = conversationId;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        
        var resolvedInitialState = !string.IsNullOrEmpty(initialState) ? initialState : (!string.IsNullOrEmpty(config.InitialState) ? config.InitialState : "Init");
        _machine = new StateMachine<string, string>(resolvedInitialState);

        ConfigureTransitions();
    }

    private void ConfigureTransitions()
    {
        // 动态注册转换关系
        foreach (var transition in _config.Transitions)
        {
            if (string.IsNullOrEmpty(transition.From) || string.IsNullOrEmpty(transition.Trigger) || string.IsNullOrEmpty(transition.To))
                continue;

            _machine.Configure(transition.From)
                .Permit(transition.Trigger, transition.To);
        }

        // 配置默认的 LowConfidence 逃生逻辑：若状态支持 LowConfidence 的配置或默认配置，转移至 Escalate 状态。
        // 为确保多租户通用，我们支持通过 "LowConfidence" 触发器转移。
        // 如果当前状态未配置 LowConfidence 的显式转移，我们仍可在状态中隐式添加转到 Escalate 的逻辑。
        var allStates = _config.Transitions.Select(t => t.From).Concat(_config.Transitions.Select(t => t.To)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        
        foreach (var state in allStates)
        {
            // 如果这个状态还没注册 LowConfidence 的转移规则，且它不是 Escalate/Booked/Cancelled 终端状态
            if (!IsTerminalState(state) && !IsEscalateState(state))
            {
                var configState = _machine.Configure(state);
                // 仅在未配置 LowConfidence 的情况下添加默认规则
                var hasLowConfidenceTransition = _config.Transitions.Any(t => string.Equals(t.From, state, StringComparison.OrdinalIgnoreCase) && 
                    (string.Equals(t.Trigger, "LowConfidence", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Trigger, "low_confidence", StringComparison.OrdinalIgnoreCase)));
                
                if (!hasLowConfidenceTransition)
                {
                    configState.Permit("LowConfidence", "Escalate");
                }
            }
        }

        // 配置 Escalate 状态回到 Init 状态的规则（人工解决后）
        _machine.Configure("Escalate")
            .Permit("HumanResolved", "Init")
            .OnEntry(() => _lowConfidenceCount = 0);
    }

    private bool IsTerminalState(string state)
    {
        return string.Equals(state, "Booked", StringComparison.OrdinalIgnoreCase) || 
               string.Equals(state, "Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsEscalateState(string state)
    {
        return string.Equals(state, "Escalate", StringComparison.OrdinalIgnoreCase);
    }

    // IConversationFsm 接口实现
    public string CurrentState => _machine.State;

    public bool IsTerminal => IsTerminalState(_machine.State);

    public bool IsEscalated => IsEscalateState(_machine.State);

    public IReadOnlySet<string> AllowedTools
    {
        get
        {
            if (_config.Tools.TryGetValue(_machine.State, out var toolsList) && toolsList != null)
            {
                return new HashSet<string>(toolsList, StringComparer.OrdinalIgnoreCase);
            }
            return new HashSet<string>();
        }
    }

    public void FireTrigger(string trigger, double confidence = 1.0)
    {
        if (confidence < 0.6)
        {
            TriggerLowConfidence(confidence);
            return;
        }

        if (_machine.CanFire(trigger))
        {
            _machine.Fire(trigger);
        }
    }

    public void TriggerLowConfidence(double confidence)
    {
        if (IsTerminal || IsEscalated)
        {
            return;
        }

        _lowConfidenceCount++;
        if (_lowConfidenceCount >= 2)
        {
            if (_machine.CanFire("LowConfidence"))
            {
                _machine.Fire("LowConfidence");
            }
        }
    }

    public int LowConfidenceCount => _lowConfidenceCount;

    public void ResetLowConfidenceCount()
    {
        _lowConfidenceCount = 0;
    }

    public bool CanFire(string trigger)
    {
        return _machine.CanFire(trigger);
    }

    public void Fire(string trigger)
    {
        if (_machine.CanFire(trigger))
        {
            _machine.Fire(trigger);
        }
    }

    public string GenerateStateDiagram()
    {
        return UmlDotGraph.Format(_machine.GetInfo());
    }
}
