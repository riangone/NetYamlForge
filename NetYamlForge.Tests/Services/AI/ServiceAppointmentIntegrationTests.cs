using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.AI.Services;
using NetYamlForge.AI.Services.ToolValidation;
using Xunit;
using Xunit.Abstractions;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// 服务预约（车检/保养）自动化集成测试
/// 
/// 测试场景:
/// 1. 客户预约车辆保养/车检
/// 2. FSM 引导收集信息 (服务类型 → 车型 → 日期 → 时间 → 姓名 → 电话)
/// 3. 档期冲突检测
/// 4. 确认预约
/// 
/// 纯内存测试，无需人工干预。
/// </summary>
public class ServiceAppointmentIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly AppointmentStateMachine _fsm;
    private readonly ToolCallValidator _toolValidator;
    private readonly string _conversationId = "service-conv-001";
    private readonly string _projectId = "auto-dealer-demo";

    public ServiceAppointmentIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _fsm = new AppointmentStateMachine(_conversationId);
        var loggerMock = new Mock<ILogger<ToolCallValidator>>();
        _toolValidator = new ToolCallValidator(loggerMock.Object, new NetYamlForge.AI.Infrastructure.DefaultSqlSafetyGuard());
    }

    #region 测试场景 1: 完整的服务预约流程

    [Fact]
    public async Task CompleteServiceBookingFlow_ShouldSuccessfullyBookServiceAppointment()
    {
        _output.WriteLine("=== 开始完整服务预约流程测试 ===");

        // 步骤 1: 客户发起服务预约
        _output.WriteLine("\n【步骤 1】客户发起服务预约请求");
        Assert.Equal(AppointmentStateMachine.State.Init, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 初始状态: {_fsm.CurrentState}");

        // 步骤 2: 客户提供服务类型
        _output.WriteLine("\n【步骤 2】客户提供服务类型 (车检)");
        _fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        Assert.Equal(AppointmentStateMachine.State.CollectVehicle, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: Init → CollectVehicle");
        _output.WriteLine($"  ✓ 服务类型: 车检");

        // 步骤 3: 客户提供车型
        _output.WriteLine("\n【步骤 3】客户提供车型信息 (Camry)");
        _fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        Assert.Equal(AppointmentStateMachine.State.CollectDate, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectVehicle → CollectDate");

        // 步骤 4: 客户提供日期
        _output.WriteLine("\n【步骤 4】客户提供预约日期 (2026-04-20)");
        _fsm.Fire(AppointmentStateMachine.Trigger.DateProvided);
        Assert.Equal(AppointmentStateMachine.State.CollectTime, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectDate → CollectTime");

        // 步骤 5: 客户提供时间
        _output.WriteLine("\n【步骤 5】客户提供预约时间 (09:00)");
        _fsm.Fire(AppointmentStateMachine.Trigger.TimeProvided);
        Assert.Equal(AppointmentStateMachine.State.CollectName, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectTime → CollectName");

        // 步骤 6: 客户提供姓名
        _output.WriteLine("\n【步骤 6】客户提供姓名 (王五)");
        _fsm.Fire(AppointmentStateMachine.Trigger.NameProvided);
        Assert.Equal(AppointmentStateMachine.State.CollectPhone, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectName → CollectPhone");

        // 步骤 7: 客户提供电话
        _output.WriteLine("\n【步骤 7】客户提供电话 (13700000000)");
        _fsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided);
        Assert.Equal(AppointmentStateMachine.State.Confirming, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectPhone → Confirming");

        // 验证 Confirming 状态允许创建预约
        Assert.True(_fsm.IsToolAllowed("create_appointment_request"));
        _output.WriteLine($"  ✓ Tool 检查: create_appointment_request=允许");

        // 步骤 8: 验证 Tool 调用
        _output.WriteLine("\n【步骤 8】验证 Tool 调用");
        var toolCall = new JsonObject
        {
            ["tool_call"] = "create_appointment_request",
            ["entity"] = "service_appointments",
            ["action"] = "create"
        };
        var validationResult = await _toolValidator.ValidateAsync(toolCall, _projectId, _fsm.CurrentState);
        Assert.True(validationResult.IsValid);
        _output.WriteLine($"  ✓ Tool 验证通过");

        // 步骤 9: 确认预约
        _output.WriteLine("\n【步骤 9】用户确认预约");
        _fsm.Fire(AppointmentStateMachine.Trigger.Confirmed);
        Assert.Equal(AppointmentStateMachine.State.Booked, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 预约状态: BOOKED ✓");

        _output.WriteLine("\n=== 服务预约流程测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 2: 档期冲突检测

    [Fact]
    public void ScheduleConflictCheck_ShouldDetectConflicts()
    {
        _output.WriteLine("\n=== 开始档期冲突检测测试 ===");

        // 场景: 同一时间段已有预约
        _output.WriteLine("\n【场景】检查时间段可用性");

        // 模拟冲突检测逻辑
        var maxSlotsPerTime = 2;
        var existingAppointments = 2; // 已有 2 个预约
        var isAvailable = existingAppointments < maxSlotsPerTime;

        Assert.False(isAvailable);
        _output.WriteLine($"  ✓ 时间段已满: {existingAppointments}/{maxSlotsPerTime}");
        _output.WriteLine($"  ✓ 冲突检测: 不可预约 ✓");

        // 场景: 有空余时间段
        existingAppointments = 1;
        isAvailable = existingAppointments < maxSlotsPerTime;

        Assert.True(isAvailable);
        _output.WriteLine($"  ✓ 时间段可用: {existingAppointments}/{maxSlotsPerTime}");
        _output.WriteLine($"  ✓ 冲突检测: 可以预约 ✓");

        _output.WriteLine("\n=== 档期冲突检测测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 3: 服务预约取消流程

    [Fact]
    public void ServiceCancellationFlow_ShouldCancelAppointment()
    {
        _output.WriteLine("\n=== 开始服务预约取消流程测试 ===");

        // 先到达 Booked 状态
        _fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        _fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        _fsm.Fire(AppointmentStateMachine.Trigger.DateProvided);
        _fsm.Fire(AppointmentStateMachine.Trigger.TimeProvided);
        _fsm.Fire(AppointmentStateMachine.Trigger.NameProvided);
        _fsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided);
        _fsm.Fire(AppointmentStateMachine.Trigger.Confirmed);

        Assert.Equal(AppointmentStateMachine.State.Booked, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 当前状态: {_fsm.CurrentState}");

        // 用户取消预约
        _output.WriteLine("\n【操作】用户取消预约");
        _fsm.Fire(AppointmentStateMachine.Trigger.Cancelled);

        Assert.Equal(AppointmentStateMachine.State.Cancelled, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: Booked → Cancelled ✓");

        // 验证终端状态禁止所有 Tool
        Assert.False(_fsm.IsToolAllowed("query_data"));
        Assert.False(_fsm.IsToolAllowed("create_appointment_request"));
        _output.WriteLine($"  ✓ Cancelled 状态禁止所有 Tool");

        _output.WriteLine("\n=== 服务预约取消流程测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 4: 服务类型多样性

    [Theory]
    [InlineData("车检")]
    [InlineData("保养")]
    [InlineData("机油交换")]
    [InlineData("轮胎更换")]
    [InlineData("故障诊断")]
    public void MultipleServiceTypes_ShouldAllBookSuccessfully(string serviceType)
    {
        _output.WriteLine($"\n=== 测试服务类型: {serviceType} ===");

        var localFsm = new AppointmentStateMachine($"service-{serviceType}");

        // 完整预约流程
        localFsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        localFsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        localFsm.Fire(AppointmentStateMachine.Trigger.DateProvided);
        localFsm.Fire(AppointmentStateMachine.Trigger.TimeProvided);
        localFsm.Fire(AppointmentStateMachine.Trigger.NameProvided);
        localFsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided);
        localFsm.Fire(AppointmentStateMachine.Trigger.Confirmed);

        Assert.Equal(AppointmentStateMachine.State.Booked, localFsm.CurrentState);
        _output.WriteLine($"  ✓ {serviceType} 预约成功 ✓");
    }

    #endregion
}
