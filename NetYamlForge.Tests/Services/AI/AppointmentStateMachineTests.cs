using NetYamlForge.Services.AI;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// AppointmentStateMachine 单元测试
/// </summary>
public class AppointmentStateMachineTests
{
    [Fact]
    public void Constructor_ShouldInitializeToInitState()
    {
        // Arrange & Act
        var fsm = new AppointmentStateMachine("test-conv-1");

        // Assert
        Assert.Equal(AppointmentStateMachine.State.Init, fsm.CurrentState);
    }

    [Fact]
    public void Fire_VehicleProvided_ShouldTransitionToCollectVehicle()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");

        // Act
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);

        // Assert
        Assert.Equal(AppointmentStateMachine.State.CollectVehicle, fsm.CurrentState);
    }

    [Fact]
    public void Fire_CompleteFlow_ShouldTransitionToConfirming()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");

        // Act - 完整流程
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided); // Init → CollectVehicle
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided); // CollectVehicle → CollectDate
        fsm.Fire(AppointmentStateMachine.Trigger.DateProvided);    // CollectDate → CollectTime
        fsm.Fire(AppointmentStateMachine.Trigger.TimeProvided);    // CollectTime → CollectName
        fsm.Fire(AppointmentStateMachine.Trigger.NameProvided);    // CollectName → CollectPhone
        fsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided);   // CollectPhone → Confirming

        // Assert
        Assert.Equal(AppointmentStateMachine.State.Confirming, fsm.CurrentState);
    }

    [Fact]
    public void Fire_ConfirmedFromConfirming_ShouldTransitionToBooked()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");
        
        // 到达 Confirming 状态
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.DateProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.TimeProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.NameProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided);

        // Act
        fsm.Fire(AppointmentStateMachine.Trigger.Confirmed);

        // Assert
        Assert.Equal(AppointmentStateMachine.State.Booked, fsm.CurrentState);
    }

    [Fact]
    public void Fire_CancelledFromAnyState_ShouldTransitionToCancelled()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");

        // 到达 CollectDate 状态
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);

        // Act
        fsm.Fire(AppointmentStateMachine.Trigger.Cancelled);

        // Assert
        Assert.Equal(AppointmentStateMachine.State.Cancelled, fsm.CurrentState);
    }

    [Fact]
    public void TriggerLowConfidence_Twice_ShouldTransitionToEscalate()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);

        // Act - 连续两次低置信度
        fsm.TriggerLowConfidence(0.5); // 第一次
        fsm.TriggerLowConfidence(0.4); // 第二次

        // Assert
        Assert.Equal(AppointmentStateMachine.State.Escalate, fsm.CurrentState);
    }

    [Fact]
    public void Fire_HumanResolved_FromEscalate_ShouldReturnToInit()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.TriggerLowConfidence(0.5);
        fsm.TriggerLowConfidence(0.4);

        // Act
        fsm.Fire(AppointmentStateMachine.Trigger.HumanResolved);

        // Assert
        Assert.Equal(AppointmentStateMachine.State.Init, fsm.CurrentState);
    }

    [Theory]
    [InlineData(AppointmentStateMachine.State.Init, "query_data", true)]
    [InlineData(AppointmentStateMachine.State.CollectVehicle, "query_data", true)]
    [InlineData(AppointmentStateMachine.State.CollectDate, "query_data", false)]
    [InlineData(AppointmentStateMachine.State.Confirming, "create_appointment_request", true)]
    [InlineData(AppointmentStateMachine.State.Confirming, "query_data", false)]
    [InlineData(AppointmentStateMachine.State.Booked, "query_data", false)]
    [InlineData(AppointmentStateMachine.State.Escalate, "query_data", false)]
    public void IsToolAllowed_ShouldRespectStateWhitelist(
        AppointmentStateMachine.State state,
        string toolName,
        bool expected)
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");
        
        // 通过触发器到达目标状态
        NavigateToState(fsm, state);

        // Act
        var isAllowed = fsm.IsToolAllowed(toolName);

        // Assert
        Assert.Equal(expected, isAllowed);
    }

    /// <summary>
    /// 导航到指定状态(测试辅助)
    /// </summary>
    private void NavigateToState(AppointmentStateMachine fsm, AppointmentStateMachine.State targetState)
    {
        switch (targetState)
        {
            case AppointmentStateMachine.State.Init:
                // 已经是 Init 状态
                break;
            case AppointmentStateMachine.State.CollectVehicle:
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                break;
            case AppointmentStateMachine.State.CollectDate:
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                break;
            case AppointmentStateMachine.State.Confirming:
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.DateProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.TimeProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.NameProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided);
                break;
            case AppointmentStateMachine.State.Booked:
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.DateProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.TimeProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.NameProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.PhoneProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.Confirmed);
                break;
            case AppointmentStateMachine.State.Escalate:
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
                fsm.TriggerLowConfidence(0.5);
                fsm.TriggerLowConfidence(0.4);
                break;
        }
    }

    [Fact]
    public void GetAllowedTools_EscalateState_ShouldReturnEmptySet()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.TriggerLowConfidence(0.5);
        fsm.TriggerLowConfidence(0.4);

        // Act
        var allowedTools = fsm.GetAllowedTools();

        // Assert
        Assert.Empty(allowedTools);
    }

    [Fact]
    public void GenerateStateDiagram_ShouldReturnNonEmptyString()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");

        // Act
        var diagram = fsm.GenerateStateDiagram();

        // Assert
        Assert.NotNull(diagram);
        Assert.NotEmpty(diagram);
        // 由于 Stateless.Graph 未安装,返回简化版本
        Assert.Contains("Current State", diagram);
    }

    [Fact]
    public void LowConfidenceCount_ShouldResetAfterEscalate()
    {
        // Arrange
        var fsm = new AppointmentStateMachine("test-conv-1");
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.Fire(AppointmentStateMachine.Trigger.VehicleProvided);
        fsm.TriggerLowConfidence(0.5);
        fsm.TriggerLowConfidence(0.4);

        // Assert - Escalate 后计数器重置
        Assert.Equal(0, fsm.LowConfidenceCount);
    }
}
