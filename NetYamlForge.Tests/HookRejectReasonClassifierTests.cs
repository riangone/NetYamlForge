using NetYamlForge.Services.Hooks;
using Xunit;

namespace NetYamlForge.Tests;

public class HookRejectReasonClassifierTests
{
    [Theory]
    [InlineData("ステータス遷移が許可されていません", "STATUS_TRANSITION")]
    [InlineData("Order is Shipped and cannot update", "STATUS_LOCK")]
    [InlineData("在庫が不足しています", "INVENTORY")]
    [InlineData("required field is missing", "REQUIRED_FIELD")]
    [InlineData("duplicate record exists", "DUPLICATE")]
    [InlineData("この行は削除できません", "DELETE_GUARD")]
    [InlineData("some unknown business validation", "BUSINESS_RULE")]
    public void Classify_ReturnsExpectedReasonCode(string reason, string expected)
    {
        var code = HookRejectReasonClassifier.Classify(reason);
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NullOrEmpty_ReturnsBusinessRule(string? reason)
    {
        var code = HookRejectReasonClassifier.Classify(reason);
        Assert.Equal("BUSINESS_RULE", code);
    }
}
