using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests.Security;

[Trait("Category", "Security")]
public sealed class PropertyAccessorCacheTests
{
    private class TestTarget
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? ReadOnlyProp { get; } = "readonly";
    }

    [Fact]
    public void SetValue_ShouldSetPropertyCorrectly()
    {
        var target = new TestTarget();

        PropertyAccessorCache.SetValue(target, "Name", "Alice");
        PropertyAccessorCache.SetValue(target, "Age", 30);

        Assert.Equal("Alice", target.Name);
        Assert.Equal(30, target.Age);
    }

    [Fact]
    public void SetValue_IgnoreCase_ShouldWork()
    {
        var target = new TestTarget();

        PropertyAccessorCache.SetValue(target, "name", "Bob");
        PropertyAccessorCache.SetValue(target, "AGE", 25);

        Assert.Equal("Bob", target.Name);
        Assert.Equal(25, target.Age);
    }

    [Fact]
    public void SetValue_NonExistentOrReadOnly_ShouldNoOp()
    {
        var target = new TestTarget();

        PropertyAccessorCache.SetValue(target, "NonExistent", "Value");
        PropertyAccessorCache.SetValue(target, "ReadOnlyProp", "NewValue");

        Assert.Equal("readonly", target.ReadOnlyProp);
    }
}
