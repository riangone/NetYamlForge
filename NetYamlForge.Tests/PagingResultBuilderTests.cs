using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class PagingResultBuilderTests
{
    [Fact]
    public void Build_TrimsOneExtraRow_AndSetsHasMore()
    {
        var rows = new List<dynamic>
        {
            new Dictionary<string, object> { ["Id"] = 1 },
            new Dictionary<string, object> { ["Id"] = 2 },
            new Dictionary<string, object> { ["Id"] = 3 }
        };

        var result = PagingResultBuilder.Build(rows, pageSize: 2, expectExtraRow: true, cursorKey: "Id");

        Assert.True(result.HasMore);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("2", result.NextCursor);
    }

    [Fact]
    public void Build_DoesNotTrim_WhenExpectExtraRowIsFalse()
    {
        var rows = new List<dynamic>
        {
            new Dictionary<string, object> { ["Id"] = 1 },
            new Dictionary<string, object> { ["Id"] = 2 },
            new Dictionary<string, object> { ["Id"] = 3 }
        };

        var result = PagingResultBuilder.Build(rows, pageSize: 2, expectExtraRow: false, cursorKey: "Id");

        Assert.False(result.HasMore);
        Assert.Equal(3, result.Items.Count);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public void Build_DoesNotSetNextCursor_WhenKeyMissing()
    {
        var rows = new List<dynamic>
        {
            new Dictionary<string, object> { ["Code"] = "A" },
            new Dictionary<string, object> { ["Code"] = "B" },
            new Dictionary<string, object> { ["Code"] = "C" }
        };

        var result = PagingResultBuilder.Build(rows, pageSize: 2, expectExtraRow: true, cursorKey: "Id");

        Assert.True(result.HasMore);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public void Build_HasMoreFalse_WhenCountEqualsPageSize()
    {
        var rows = new List<dynamic>
        {
            new Dictionary<string, object> { ["Id"] = 1 },
            new Dictionary<string, object> { ["Id"] = 2 }
        };

        var result = PagingResultBuilder.Build(rows, pageSize: 2, expectExtraRow: true, cursorKey: "Id");

        Assert.False(result.HasMore);
        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public void Build_HandlesEmptyRows()
    {
        var result = PagingResultBuilder.Build(new List<dynamic>(), pageSize: 10, expectExtraRow: true, cursorKey: "Id");

        Assert.False(result.HasMore);
        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
    }
}
