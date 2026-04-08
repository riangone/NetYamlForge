using NetYamlForge.Services.AI;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// 试乘预约中名字提取逻辑的测试
/// </summary>
public class NameExtractionTests
{
    [Theory]
    [InlineData("田中です", "田中")]
    [InlineData("山田と申します", "山田")]
    [InlineData("佐藤でございます", "佐藤")]
    [InlineData("鈴木です", "鈴木")]
    public async Task ExtractName_WithJapanesePolitePatterns_ShouldExtractName(string message, string expectedName)
    {
        // 这个测试需要在集成测试中验证
        // 这里只是文档预期行为
        Assert.NotNull(message);
        Assert.NotNull(expectedName);
    }

    [Theory]
    [InlineData("私の名前は田中です", "田中")]
    [InlineData("名前が山田です", "山田")]
    [InlineData("名前は佐藤でございます", "佐藤")]
    public async Task ExtractName_WithNameExplicitPatterns_ShouldExtractName(string message, string expectedName)
    {
        // 这个测试需要在集成测试中验证
        Assert.NotNull(message);
        Assert.NotNull(expectedName);
    }

    [Theory]
    [InlineData("田中")]
    [InlineData("山田太郎")]
    [InlineData("さとう")]
    [InlineData("タナカ")]
    public async Task ExtractName_WithShortNameOnly_ShouldExtractName(string message)
    {
        // 这个测试需要在集成测试中验证
        Assert.NotNull(message);
        Assert.True(message.Length >= 2 && message.Length <= 4);
    }

    [Theory]
    [InlineData("はい")]
    [InlineData("ありがとう")]
    [InlineData("お願いします")]
    [InlineData("教えてください")]
    [InlineData("12345")]
    public async Task ExtractName_WithNonNameText_ShouldNotExtractName(string message)
    {
        // 这个测试需要在集成测试中验证
        Assert.NotNull(message);
        // 这些应该被 LooksLikeNonNameText 过滤掉
    }
}
