using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class ListQueryOptionResolverTests
{
    [Fact]
    public void ResolveCountEnabled_ReturnsTrue_WhenNullOrEmpty()
    {
        Assert.True(ListQueryOptionResolver.ResolveCountEnabled(null));
        Assert.True(ListQueryOptionResolver.ResolveCountEnabled(""));
        Assert.True(ListQueryOptionResolver.ResolveCountEnabled("   "));
    }

    [Fact]
    public void ResolveCountEnabled_ReturnsFalse_ForFalseAndZero()
    {
        Assert.False(ListQueryOptionResolver.ResolveCountEnabled("false"));
        Assert.False(ListQueryOptionResolver.ResolveCountEnabled("FALSE"));
        Assert.False(ListQueryOptionResolver.ResolveCountEnabled("0"));
    }

    [Fact]
    public void ResolveCountEnabled_ReturnsTrue_ForOtherValues()
    {
        Assert.True(ListQueryOptionResolver.ResolveCountEnabled("true"));
        Assert.True(ListQueryOptionResolver.ResolveCountEnabled("1"));
        Assert.True(ListQueryOptionResolver.ResolveCountEnabled("yes"));
    }

    [Fact]
    public void ResolveClearRequested_ParsesFlagValues()
    {
        Assert.False(ListQueryOptionResolver.ResolveClearRequested(null));
        Assert.False(ListQueryOptionResolver.ResolveClearRequested(""));
        Assert.False(ListQueryOptionResolver.ResolveClearRequested("0"));
        Assert.True(ListQueryOptionResolver.ResolveClearRequested("1"));
        Assert.True(ListQueryOptionResolver.ResolveClearRequested("true"));
        Assert.True(ListQueryOptionResolver.ResolveClearRequested("TRUE"));
    }
}
