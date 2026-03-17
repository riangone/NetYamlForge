using NetYamlForge.Models;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityConfigDiffServiceTests
{
    [Fact]
    public void BuildJsonDiffLines_ReturnsNoMetadata_WhenBothNull()
    {
        var sut = new DynamicEntityConfigDiffService();

        var (lines, changedCount) = sut.BuildJsonDiffLines(null, null, includeUnchanged: false);

        Assert.Single(lines);
        Assert.Equal("No metadata available.", lines[0]);
        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void BuildJsonDiffLines_ReturnsAddedMessage_WhenBaseMissing()
    {
        var sut = new DynamicEntityConfigDiffService();
        var effectiveMeta = new EntityDefinition { Table = "Orders", Key = "Id", DisplayName = "Orders" };

        var (lines, changedCount) = sut.BuildJsonDiffLines(null, effectiveMeta, includeUnchanged: false);

        Assert.Single(lines);
        Assert.Contains("only in effective/project metadata", lines[0], StringComparison.Ordinal);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void BuildJsonDiffLines_ReturnsChangedField_WhenDisplayNameChanged()
    {
        var sut = new DynamicEntityConfigDiffService();
        var baseMeta = new EntityDefinition { Table = "Orders", Key = "Id", DisplayName = "Orders" };
        var effectiveMeta = new EntityDefinition { Table = "Orders", Key = "Id", DisplayName = "Order Master" };

        var (lines, changedCount) = sut.BuildJsonDiffLines(baseMeta, effectiveMeta, includeUnchanged: false);

        Assert.True(changedCount >= 1);
        Assert.Contains(lines, l => l.Contains("$.DisplayName", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildJsonDiffLines_IncludesEqualLine_WhenIncludeUnchangedTrue()
    {
        var sut = new DynamicEntityConfigDiffService();
        var baseMeta = new EntityDefinition { Table = "Orders", Key = "Id", DisplayName = "Orders" };
        var effectiveMeta = new EntityDefinition { Table = "Orders", Key = "Id", DisplayName = "Orders" };

        var (lines, changedCount) = sut.BuildJsonDiffLines(baseMeta, effectiveMeta, includeUnchanged: true);

        Assert.Equal(0, changedCount);
        Assert.Contains(lines, l => l.StartsWith("= $.", StringComparison.Ordinal));
    }
}
