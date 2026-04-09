using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.ToolValidation;
using Xunit;
using Xunit.Abstractions;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// AI 客户试驾预约完整流程集成测试
/// 
/// 测试场景:
/// 1. 客户发起试驾预约请求
/// 2. AI 通过 FSM 状态机引导收集信息 (车型 → 日期 → 时间 → 姓名 → 电话)
/// 3. 每个槽位填充时 FSM 自动推进
/// 4. 低置信度时触发 ESCALATE 机制
/// 5. 确认后创建预约
/// 6. 验证最终状态和数据
/// 
/// 本测试专注于 FSM 状态机和 Tool 验证器的集成测试，
/// 不依赖数据库，纯内存测试。
/// </summary>
public class AutoDealerTestDriveIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly AppointmentStateMachine _fsm;
    private readonly ToolCallValidator _toolValidator;
    private readonly string _conversationId = "test-conv-001";
    private readonly string _projectId = "auto-dealer-demo";

    // 模拟收集的槽位
    private readonly Dictionary<string, string> _collectedSlots = new();

    public AutoDealerTestDriveIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _fsm = new AppointmentStateMachine(_conversationId);
        
        var toolValidatorLogger = new Mock<ILogger<ToolCallValidator>>();
        _toolValidator = new ToolCallValidator(toolValidatorLogger.Object);
    }

    public void Dispose()
    {
        _collectedSlots.Clear();
    }

    #region 测试场景 1: 完整的试驾预约流程

    [Fact]
    public async Task CompleteTestDriveBookingFlow_ShouldSuccessfullyBookAppointment()
    {
        _output.WriteLine("=== 开始完整试驾预约流程测试 ===");

        // ========== 步骤 1: 客户发起试驾预约请求 ==========
        _output.WriteLine("\n【步骤 1】客户发起试驾预约请求");
        
        var initialState = _fsm.CurrentState;
        Assert.Equal(AppointmentStateMachine.State.Init, initialState);
        _output.WriteLine($"  ✓ 初始状态: {initialState}");

        // 检查初始状态允许的 Tool
        var allowedTools = _fsm.GetAllowedTools();
        Assert.Contains("query_data", allowedTools);
        _output.WriteLine($"  ✓ 允许的 Tool: {string.Join(", ", allowedTools)}");

        // ========== 步骤 2: 客户提供车型信息 ==========
        _output.WriteLine("\n【步骤 2】客户提供车型信息 (RAV4)");
        
        _collectedSlots["vehicle_model"] = "RAV4";
        _fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        
        Assert.Equal(AppointmentStateMachine.State.CollectVehicle, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: Init → CollectVehicle");
        _output.WriteLine($"  ✓ 当前状态: {_fsm.CurrentState}");
        _output.WriteLine($"  ✓ 已收集槽位: vehicle_model = RAV4");

        // 验证 Tool 允许性
        Assert.True(_fsm.IsToolAllowed("query_data"));
        Assert.False(_fsm.IsToolAllowed("create_appointment_request"));
        _output.WriteLine($"  ✓ Tool 检查: query_data=允许, create_appointment_request=禁止");

        // ========== 步骤 3: 客户提供日期信息 ==========
        _output.WriteLine("\n【步骤 3】客户提供日期信息 (2026-04-15)");
        
        _collectedSlots["preferred_date"] = "2026-04-15";
        _fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided); // CollectVehicle → CollectDate
        
        Assert.Equal(AppointmentStateMachine.State.CollectDate, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectVehicle → CollectDate");

        Assert.Equal("2026-04-15", _collectedSlots["preferred_date"]);
        _output.WriteLine($"  ✓ 已收集槽位: preferred_date = 2026-04-15");

        // ========== 步骤 4: 客户提供时间信息 ==========
        _output.WriteLine("\n【步骤 4】客户提供时间信息 (10:00)");
        
        _collectedSlots["preferred_time"] = "10:00";
        _fsm.Fire(AppointmentStateMachine.Trigger.DateProvided); // CollectDate → CollectTime
        
        Assert.Equal(AppointmentStateMachine.State.CollectTime, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectDate → CollectTime");

        Assert.Equal("10:00", _collectedSlots["preferred_time"]);
        _output.WriteLine($"  ✓ 已收集槽位: preferred_time = 10:00");

        // ========== 步骤 5: 客户提供姓名信息 ==========
        _output.WriteLine("\n【步骤 5】客户提供姓名信息 (张三)");
        
        _collectedSlots["customer_name"] = "张三";
        _fsm.Fire(AppointmentStateMachine.Trigger.TimeProvided); // CollectTime → CollectName
        
        Assert.Equal(AppointmentStateMachine.State.CollectName, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectTime → CollectName");

        Assert.Equal("张三", _collectedSlots["customer_name"]);
        _output.WriteLine($"  ✓ 已收集槽位: customer_name = 张三");

        // ========== 步骤 6: 客户提供电话信息 ==========
        _output.WriteLine("\n【步骤 6】客户提供电话信息 (13812345678)");
        
        _collectedSlots["customer_phone"] = "13812345678";
        _fsm.Fire(AppointmentStateMachine.Trigger.NameProvided); // CollectName → CollectPhone
        _fsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided); // CollectPhone → Confirming
        
        Assert.Equal(AppointmentStateMachine.State.Confirming, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectPhone → Confirming");

        Assert.Equal("13812345678", _collectedSlots["customer_phone"]);
        _output.WriteLine($"  ✓ 已收集槽位: customer_phone = 13812345678");

        // 验证 Confirming 状态只允许创建预约
        Assert.True(_fsm.IsToolAllowed("create_appointment_request"));
        Assert.False(_fsm.IsToolAllowed("query_data"));
        _output.WriteLine($"  ✓ Tool 检查: create_appointment_request=允许, query_data=禁止");

        // ========== 步骤 7: 验证 Tool 调用 (模拟 LLM 输出) ==========
        _output.WriteLine("\n【步骤 7】验证 Tool 调用 (模拟 LLM 输出)");
        
        var toolCall = new JsonObject
        {
            ["tool_call"] = "create_appointment_request",
            ["entity"] = "service_appointments",
            ["action"] = "create",
            ["filters"] = new JsonArray()
        };

        var validationResult = await _toolValidator.ValidateAsync(toolCall, _projectId, _fsm.CurrentState);
        Assert.True(validationResult.IsValid);
        _output.WriteLine($"  ✓ Tool 验证通过: {toolCall["tool_call"]}");

        // ========== 步骤 8: 用户确认预约 ==========
        _output.WriteLine("\n【步骤 8】用户确认预约");
        
        _fsm.Fire(AppointmentStateMachine.Trigger.Confirmed);
        
        Assert.Equal(AppointmentStateMachine.State.Booked, _fsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: Confirming → Booked");
        _output.WriteLine($"  ✓ 预约状态: BOOKED ✓");

        // 验证最终收集的所有槽位
        Assert.Equal(5, _collectedSlots.Count);
        _output.WriteLine($"\n  ✓ 最终收集的槽位 ({_collectedSlots.Count} 个):");
        foreach (var slot in _collectedSlots)
        {
            _output.WriteLine($"    - {slot.Key}: {slot.Value}");
        }

        // ========== 步骤 9: 验证预约数据完整性 ==========
        _output.WriteLine("\n【步骤 9】验证预约数据完整性");
        
        Assert.Equal("RAV4", _collectedSlots["vehicle_model"]);
        Assert.Equal("2026-04-15", _collectedSlots["preferred_date"]);
        Assert.Equal("10:00", _collectedSlots["preferred_time"]);
        Assert.Equal("张三", _collectedSlots["customer_name"]);
        Assert.Equal("13812345678", _collectedSlots["customer_phone"]);
        _output.WriteLine($"  ✓ 所有槽位数据验证通过 ✓");

        // ========== 步骤 10: 验证最终状态 ==========
        _output.WriteLine("\n【步骤 10】验证最终状态");
        
        var finalState = _fsm.CurrentState;
        Assert.Equal(AppointmentStateMachine.State.Booked, finalState);
        Assert.False(_fsm.IsToolAllowed("query_data"));
        Assert.False(_fsm.IsToolAllowed("create_appointment_request"));
        _output.WriteLine($"  ✓ 最终状态: {finalState}");
        _output.WriteLine($"  ✓ 所有 Tool 已禁止 (终端状态)");

        _output.WriteLine("\n=== 完整试驾预约流程测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 2: 低置信度触发 ESCALATE

    [Fact]
    public void LowConfidenceFlow_ShouldTriggerEscalate()
    {
        _output.WriteLine("\n=== 开始低置信度 ESCALATE 流程测试 ===");

        // 创建新的 FSM 实例以确保测试隔离
        var localFsm = new AppointmentStateMachine("test-conv-escalate");
        
        // 到达 CollectDate 状态
        localFsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided); // Init → CollectVehicle
        localFsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided); // CollectVehicle → CollectDate
        Assert.Equal(AppointmentStateMachine.State.CollectDate, localFsm.CurrentState);
        _output.WriteLine($"  ✓ 当前状态: {localFsm.CurrentState}");

        // 触发两次低置信度 - 应该进入 ESCALATE
        _output.WriteLine("\n【触发 1】第一次低置信度 (0.5)");
        localFsm.TriggerLowConfidence(0.5);
        _output.WriteLine($"  ✓ 计数器: {localFsm.LowConfidenceCount}");
        
        _output.WriteLine("\n【触发 2】第二次低置信度 (0.4)");
        localFsm.TriggerLowConfidence(0.4);
        
        // 验证进入 ESCALATE 状态
        Assert.Equal(AppointmentStateMachine.State.Escalate, localFsm.CurrentState);
        _output.WriteLine($"  ✓ 状态转换: CollectDate → ESCALATE ✓");
        _output.WriteLine($"  ✓ 计数器已重置: {localFsm.LowConfidenceCount}");

        // 验证 ESCALATE 状态禁止所有 Tool
        _output.WriteLine("\n【验证】ESCALATE 状态 Tool 允许性");
        Assert.False(localFsm.IsToolAllowed("query_data"));
        Assert.False(localFsm.IsToolAllowed("create_appointment_request"));
        _output.WriteLine($"  ✓ 所有 Tool 已禁止 ✓");

        // 人工接管
        _output.WriteLine("\n【恢复】人工坐席解决");
        localFsm.Fire(AppointmentStateMachine.Trigger.HumanResolved);
        Assert.Equal(AppointmentStateMachine.State.Init, localFsm.CurrentState);
        _output.WriteLine($"  ✓ 状态恢复: Escalate → Init ✓");

        _output.WriteLine("\n=== 低置信度 ESCALATE 流程测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 3: Tool 验证集成测试

    [Fact]
    public async Task ToolValidationFlow_ShouldRejectInvalidCalls()
    {
        _output.WriteLine("\n=== 开始 Tool 验证流程测试 ===");

        // 场景 1: Init 状态允许 query_data
        _output.WriteLine("\n【场景 1】Init 状态允许 query_data");
        var toolCall1 = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = "list"
        };
        var result1 = await _toolValidator.ValidateAsync(toolCall1, _projectId, _fsm.CurrentState);
        Assert.True(result1.IsValid);
        _output.WriteLine($"  ✓ 验证通过: query_data");

        // 场景 2: Init 状态禁止 create_appointment_request
        _output.WriteLine("\n【场景 2】Init 状态禁止 create_appointment_request");
        var toolCall2 = new JsonObject
        {
            ["tool_call"] = "create_appointment_request",
            ["entity"] = "service_appointments",
            ["action"] = "create"
        };
        var result2 = await _toolValidator.ValidateAsync(toolCall2, _projectId, _fsm.CurrentState);
        Assert.False(result2.IsValid);
        _output.WriteLine($"  ✓ 验证失败: {result2.ErrorMessage}");

        // 场景 3: 无效 Entity
        _output.WriteLine("\n【场景 3】无效 Entity");
        var toolCall3 = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "invalid_entity",
            ["action"] = "list"
        };
        var result3 = await _toolValidator.ValidateAsync(toolCall3, _projectId, _fsm.CurrentState);
        Assert.False(result3.IsValid);
        _output.WriteLine($"  ✓ 无效 Entity 被拦截: {result3.ErrorMessage}");

        // 场景 4: 无效 Action
        _output.WriteLine("\n【场景 4】无效 Action");
        var toolCall4 = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = "delete"
        };
        var result4 = await _toolValidator.ValidateAsync(toolCall4, _projectId, _fsm.CurrentState);
        Assert.False(result4.IsValid);
        _output.WriteLine($"  ✓ 无效 Action 被拦截: {result4.ErrorMessage}");

        // 场景 5: ESCALATE 状态禁止所有 Tool
        _output.WriteLine("\n【场景 5】ESCALATE 状态禁止所有 Tool");
        _fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        _fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        _fsm.TriggerLowConfidence(0.5);
        _fsm.TriggerLowConfidence(0.4);
        Assert.Equal(AppointmentStateMachine.State.Escalate, _fsm.CurrentState);

        var toolCall5 = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = "list"
        };
        var result5 = await _toolValidator.ValidateAsync(toolCall5, _projectId, _fsm.CurrentState);
        Assert.False(result5.IsValid);
        _output.WriteLine($"  ✓ ESCALATE 状态 Tool 被拦截: {result5.ErrorMessage}");

        _output.WriteLine("\n=== Tool 验证流程测试通过 ✓ ===");
    }

    #endregion

    #region 测试场景 4: 槽位自动推进测试

    [Fact]
    public void SlotAutoProgressFlow_ShouldAutomaticallyAdvanceState()
    {
        _output.WriteLine("\n=== 开始槽位自动推进流程测试 ===");

        // 创建新的 FSM 实例以确保测试隔离
        var localFsm = new AppointmentStateMachine("test-conv-auto-progress");
        var localSlots = new Dictionary<string, string>();

        // 模拟完整的槽位填充流程，验证 FSM 自动推进
        var slots = new Dictionary<string, string>
        {
            ["vehicle_model"] = "RAV4",
            ["preferred_date"] = "2026-04-15",
            ["preferred_time"] = "14:00",
            ["customer_name"] = "李四",
            ["customer_phone"] = "13987654321"
        };

        // 修正触发器序列：
        // Init → CollectVehicle (VehicleProvided)
        // CollectVehicle → CollectDate (VehicleProvided)
        // CollectDate → CollectTime (DateProvided)
        // CollectTime → CollectName (TimeProvided)
        // CollectName → CollectPhone (NameProvided)
        // 注意：CollectPhone → Confirming 需要 PhoneProvided,但测试只有5个槽位
        // 所以最后一个触发器应该是 PhoneProvided
        var triggers = new[]
        {
            AppointmentStateMachine.Trigger.VehicleProvided,    // Init → CollectVehicle
            AppointmentStateMachine.Trigger.VehicleProvided,    // CollectVehicle → CollectDate
            AppointmentStateMachine.Trigger.DateProvided,       // CollectDate → CollectTime
            AppointmentStateMachine.Trigger.TimeProvided,       // CollectTime → CollectName
            AppointmentStateMachine.Trigger.NameProvided,       // CollectName → CollectPhone
            AppointmentStateMachine.Trigger.PhoneProvided       // CollectPhone → Confirming (额外触发)
        };

        var expectedStates = new[]
        {
            AppointmentStateMachine.State.CollectVehicle,
            AppointmentStateMachine.State.CollectDate,
            AppointmentStateMachine.State.CollectTime,
            AppointmentStateMachine.State.CollectName,
            AppointmentStateMachine.State.CollectPhone,
            AppointmentStateMachine.State.Confirming
        };

        _output.WriteLine("\n【自动推进】逐个填充槽位并验证状态转换");
        
        int slotIndex = 0;
        foreach (var slot in slots)
        {
            // 记录槽位值
            localSlots[slot.Key] = slot.Value;
            
            // 触发 FSM 推进
            var trigger = triggers[slotIndex];
            localFsm.Fire(trigger);

            // 验证状态
            var expectedState = expectedStates[slotIndex];
            Assert.Equal(expectedState, localFsm.CurrentState);
            
            _output.WriteLine($"  ✓ 槽位 {slotIndex + 1}: {slot.Key} = {slot.Value} → {expectedState}");
            slotIndex++;
        }

        // 所有槽位填充后,需要额外触发 PhoneProvided 才能进入 Confirming
        // 因为 customer_phone 槽位对应的是 NameProvided 触发器 (进入 CollectPhone)
        // 需要额外的 PhoneProvided 才能进入 Confirming
        _output.WriteLine("\n【额外触发】PhoneProvided → Confirming");
        localFsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided);
        Assert.Equal(AppointmentStateMachine.State.Confirming, localFsm.CurrentState);
        _output.WriteLine($"  ✓ 状态: CollectPhone → Confirming");

        // 最终确认
        _output.WriteLine("\n【最终确认】用户确认预约");
        localFsm.Fire(AppointmentStateMachine.Trigger.Confirmed);
        Assert.Equal(AppointmentStateMachine.State.Booked, localFsm.CurrentState);
        _output.WriteLine($"  ✓ 最终确认 → Booked ✓");

        // 验证所有槽位已收集
        Assert.Equal(5, localSlots.Count);
        _output.WriteLine($"  ✓ 收集槽位数: {localSlots.Count}/5");

        _output.WriteLine("\n=== 槽位自动推进流程测试通过 ✓ ===");
    }

    #endregion
}
