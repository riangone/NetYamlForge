using Stateless;
using Stateless.Graph;

namespace NetYamlForge.AI.Services;

/// <summary>
/// 试驾预约状态机
/// 
/// 状态流转:
/// Init → CollectVehicle → CollectDate → CollectTime → CollectName → CollectPhone → Confirming → Booked
/// 任意状态 → Escalate(连续 2 次置信度 < 0.6)
/// Escalate → Init(人工坐席接管后)
/// </summary>
public class AppointmentStateMachine
{
    // 状态定义
    public enum State
    {
        Init,
        CollectVehicle,
        CollectDate,
        CollectTime,
        CollectName,
        CollectPhone,
        Confirming,
        Booked,
        Cancelled,
        Escalate  // ⚠️ 新增:脱线状态
    }

    // 触发器定义
    public enum Trigger
    {
        VehicleProvided,
        DateProvided,
        TimeProvided,
        NameProvided,
        PhoneProvided,
        PhoneInvalid,
        Confirmed,
        Cancelled,
        Timeout,
        LowConfidence,      // ⚠️ 新增:低置信度
        HumanResolved       // ⚠️ 新增:人工坐席解决
    }

    private readonly StateMachine<State, Trigger> _machine;
    private readonly string _conversationId;
    private int _lowConfidenceCount = 0; // 连续低置信度计数

    public AppointmentStateMachine(string conversationId)
    {
        _conversationId = conversationId;
        _machine = new StateMachine<State, Trigger>(State.Init);

        ConfigureTransitions();
    }

    private void ConfigureTransitions()
    {
        // Init 状态转换
        _machine.Configure(State.Init)
            .OnEntry(() => _lowConfidenceCount = 0)
            .Permit(Trigger.VehicleProvided, State.CollectVehicle)
            .Permit(Trigger.DateProvided, State.CollectDate)
            .Permit(Trigger.Cancelled, State.Cancelled);

        // 信息收集状态链
        _machine.Configure(State.CollectVehicle)
            .Permit(Trigger.VehicleProvided, State.CollectDate)
            .Permit(Trigger.LowConfidence, HandleLowConfidence())
            .Permit(Trigger.Timeout, State.Init)
            .Permit(Trigger.Cancelled, State.Cancelled);

        _machine.Configure(State.CollectDate)
            .Permit(Trigger.DateProvided, State.CollectTime)
            .Permit(Trigger.LowConfidence, HandleLowConfidence())
            .Permit(Trigger.Timeout, State.Init)
            .Permit(Trigger.Cancelled, State.Cancelled);

        _machine.Configure(State.CollectTime)
            .Permit(Trigger.TimeProvided, State.CollectName)
            .Permit(Trigger.LowConfidence, HandleLowConfidence())
            .Permit(Trigger.Timeout, State.Init)
            .Permit(Trigger.Cancelled, State.Cancelled);

        _machine.Configure(State.CollectName)
            .Permit(Trigger.NameProvided, State.CollectPhone)
            .Permit(Trigger.LowConfidence, HandleLowConfidence())
            .Permit(Trigger.Timeout, State.Init)
            .Permit(Trigger.Cancelled, State.Cancelled);

        _machine.Configure(State.CollectPhone)
            .Permit(Trigger.PhoneProvided, State.Confirming)
            .PermitReentry(Trigger.PhoneInvalid) // 重新收集,保持在同一状态
            .Permit(Trigger.LowConfidence, HandleLowConfidence())
            .Permit(Trigger.Timeout, State.Init)
            .Permit(Trigger.Cancelled, State.Cancelled);

        // 确认状态
        _machine.Configure(State.Confirming)
            .Permit(Trigger.Confirmed, State.Booked)
            .Permit(Trigger.LowConfidence, HandleLowConfidence())
            .Permit(Trigger.Timeout, State.Init)
            .Permit(Trigger.Cancelled, State.Cancelled);

        // 终端状态
        _machine.Configure(State.Booked)
            .OnEntry(() => OnAppointmentBooked());

        _machine.Configure(State.Cancelled)
            .OnEntry(() => OnAppointmentCancelled());

        // ⚠️ ESCALATE 状态
        _machine.Configure(State.Escalate)
            .OnEntryFrom(Trigger.LowConfidence, () => OnEscalate())
            .OnEntry(() => _lowConfidenceCount = 0) // 重置计数器
            .Permit(Trigger.HumanResolved, State.Init);
    }

    /// <summary>
    /// 处理低置信度:连续 2 次后触发 ESCALATE
    /// </summary>
    private State HandleLowConfidence()
    {
        _lowConfidenceCount++;

        if (_lowConfidenceCount >= 2)
        {
            return State.Escalate;
        }

        return _machine.State; // 保持当前状态
    }

    /// <summary>
    /// 触发低置信度
    /// </summary>
    public void TriggerLowConfidence(double confidence)
    {
        if (_machine.State is State.Escalate or State.Booked or State.Cancelled)
        {
            return; // 终端状态不触发
        }

        _machine.Fire(Trigger.LowConfidence);
    }

    /// <summary>
    /// 触发标准状态转换
    /// </summary>
    public void Fire(Trigger trigger)
    {
        if (_machine.CanFire(trigger))
        {
            _machine.Fire(trigger);
        }
    }

    /// <summary>
    /// 检查是否可以触发指定触发器
    /// </summary>
    public bool CanFire(Trigger trigger) => _machine.CanFire(trigger);

    /// <summary>
    /// 获取当前状态
    /// </summary>
    public State CurrentState => _machine.State;

    /// <summary>
    /// 获取当前状态允许的 Tool 列表
    /// </summary>
    public HashSet<string> GetAllowedTools()
    {
        return _machine.State switch
        {
            State.Init => new HashSet<string> { "query_data" },
            State.CollectVehicle => new HashSet<string> { "query_data" }, // 仅车辆查询
            State.CollectDate => new HashSet<string>(),
            State.CollectTime => new HashSet<string>(),
            State.CollectName => new HashSet<string>(),
            State.CollectPhone => new HashSet<string>(),
            State.Confirming => new HashSet<string> { "create_appointment_request" },
            State.Booked => new HashSet<string>(),
            State.Escalate => new HashSet<string>(), // 人工接管,禁止 AI Tool
            _ => new HashSet<string>()
        };
    }

    /// <summary>
    /// 检查 Tool 调用是否被当前状态允许
    /// </summary>
    public bool IsToolAllowed(string toolName)
    {
        var allowed = GetAllowedTools();

        // query_data 在 CollectVehicle 状态仅允许查询 vehicles
        if (_machine.State == State.CollectVehicle && toolName == "query_data")
        {
            return true; // 具体 entity 校验由 ToolCallValidator 处理
        }

        return allowed.Contains(toolName);
    }

    /// <summary>
    /// 获取连续低置信度计数
    /// </summary>
    public int LowConfidenceCount => _lowConfidenceCount;

    /// <summary>
    /// 重置低置信度计数
    /// </summary>
    public void ResetLowConfidenceCount() => _lowConfidenceCount = 0;

    /// <summary>
    /// 生成状态图(PlantUML 格式)
    /// 注: Stateless 5.x 版本可能需要额外包
    /// </summary>
    public string GenerateStateDiagram()
    {
        // TODO: 需要安装 Stateless.Graph 包
        // return UmlDotGraph.Format(_machine.GetGraph());
        return $"Current State: {_machine.State}";
    }

    // ===== 事件处理器 =====

    private void OnAppointmentBooked()
    {
        // 发送确认通知、更新档期等
    }

    private void OnAppointmentCancelled()
    {
        // 释放档期、记录日志等
    }

    private void OnEscalate()
    {
        // 1. 推送至人工坐席队列(通过 SignalR)
        // 2. 更新对话状态为 escalated
        // 3. 发送通知给客服团队
    }
}
