// 测试概要：验证"試乗したいです"重复提问的修复
// 确保当用户重复表达试乗意向但未提供具体信息时，系统不会一直问"お名前を教えてください"

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Services.AI;
using Xunit;
using Xunit.Abstractions;

namespace NetYamlForge.Tests.Services.AI;

public class TestDriveRepetitionFixTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<SlotFillingManager>> _logger;
    private readonly Mock<IServiceScopeFactory> _scopeFactory;

    public TestDriveRepetitionFixTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new Mock<ILogger<SlotFillingManager>>();
        _scopeFactory = new Mock<IServiceScopeFactory>();
    }

    [Fact]
    public async Task SlotFilling_WhenUserRepeatsTestDriveIntent_ShouldNotAlwaysAskForName()
    {
        // Arrange
        var slotFilling = new SlotFillingManager(_logger.Object, _scopeFactory.Object);
        var conversationId = "TEST-CONV-001";
        var scenario = "test_drive";

        // 第一次：获取会话（此时所有槽位都是空的）
        var session1 = await slotFilling.GetSessionAsync(conversationId, scenario);
        Assert.False(session1.IsComplete);
        
        var missingSlots1 = session1.GetMissingSlots();
        _output.WriteLine($"初始缺失的槽位数: {missingSlots1.Count}");
        foreach (var slot in missingSlots1)
        {
            _output.WriteLine($"  - {slot.Name}: {slot.Prompt}");
        }
        
        // 验证第一个缺失的槽位是 vehicle_model（按定义顺序）
        Assert.Equal("vehicle_model", missingSlots1[0].Name);
        Assert.Equal("どの車種の試乗をご希望ですか？", missingSlots1[0].Prompt);

        // Act 1: 用户说"試乗したいです"，但没有提供任何具体信息
        // 模拟：不更新任何槽位
        
        // 获取下一个必需的槽位
        var nextSlot1 = await slotFilling.GetNextRequiredSlotAsync(conversationId, scenario);
        
        // Assert 1: 应该请求第一个槽位（vehicle_model）
        Assert.NotNull(nextSlot1);
        Assert.Equal("vehicle_model", nextSlot1.SlotName);
        Assert.Equal("どの車種の試乗をご希望ですか？", nextSlot1.Prompt);
        _output.WriteLine($"第一次提问: {nextSlot1.Prompt}");

        // Act 2: 用户再次说"試乗したいです"，仍然没有提供具体信息
        // 在修复前，这会导致一直问 customer_name
        // 修复后，应该仍然问 vehicle_model（因为还没有填充）
        var nextSlot2 = await slotFilling.GetNextRequiredSlotAsync(conversationId, scenario);
        
        // Assert 2: 应该仍然请求 vehicle_model，而不是 customer_name
        Assert.NotNull(nextSlot2);
        Assert.Equal("vehicle_model", nextSlot2.SlotName);
        Assert.Equal("どの車種の試乗をご希望ですか？", nextSlot2.Prompt);
        _output.WriteLine($"第二次提问: {nextSlot2.Prompt}");

        // 验证：两次提问应该相同，都是 vehicle_model
        Assert.Equal(nextSlot1.Prompt, nextSlot2.Prompt);
    }

    [Fact]
    public async Task SlotFilling_WhenUserProvidesPartialInfo_ShouldAskNextMissingSlot()
    {
        // Arrange
        var slotFilling = new SlotFillingManager(_logger.Object, _scopeFactory.Object);
        var conversationId = "TEST-CONV-002";
        var scenario = "test_drive";

        var session1 = await slotFilling.GetSessionAsync(conversationId, scenario);
        
        // Act: 用户提供了 vehicle_model
        await slotFilling.UpdateSlotAsync(conversationId, "vehicle_model", "プリウス");
        
        var session2 = await slotFilling.GetSessionAsync(conversationId, scenario);
        var nextSlot = await slotFilling.GetNextRequiredSlotAsync(conversationId, scenario);
        
        // Assert: 应该问下一个缺失的槽位（preferred_date）
        Assert.NotNull(nextSlot);
        Assert.Equal("preferred_date", nextSlot.SlotName);
        Assert.Equal("ご希望の日付を教えてください（例：明日、来週月曜日）", nextSlot.Prompt);
        _output.WriteLine($"填充 vehicle_model 后的下一个问题: {nextSlot.Prompt}");
    }

    [Fact]
    public async Task SlotFilling_GetCollectedValuesCount_ShouldReflectFilledSlots()
    {
        // Arrange
        var slotFilling = new SlotFillingManager(_logger.Object, _scopeFactory.Object);
        var conversationId = "TEST-CONV-003";
        var scenario = "test_drive";

        var session1 = await slotFilling.GetSessionAsync(conversationId, scenario);
        var collected1 = session1.GetCollectedValues();
        
        // Assert 1: 初始应该没有收集任何槽位
        Assert.Empty(collected1);
        _output.WriteLine($"初始收集槽位数: {collected1.Count}");

        // Act: 填充一个槽位
        await slotFilling.UpdateSlotAsync(conversationId, "vehicle_model", "プリウス");
        
        var session2 = await slotFilling.GetSessionAsync(conversationId, scenario);
        var collected2 = session2.GetCollectedValues();
        
        // Assert 2: 应该有一个槽位被收集
        Assert.Single(collected2);
        Assert.Equal("プリウス", collected2["vehicle_model"]);
        _output.WriteLine($"填充后收集槽位数: {collected2.Count}");
        _output.WriteLine($"  - vehicle_model: {collected2["vehicle_model"]}");
    }
}
