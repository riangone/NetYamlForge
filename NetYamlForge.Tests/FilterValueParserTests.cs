using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace NetYamlForge.Tests;

public class FilterValueParserTests
{
    [Fact]
    public void Build_ParsesRangeAndDateRangeValues()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["price"] = new() { Type = "range" },
            ["created_at"] = new() { Type = "date-range" }
        });

        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["price_min"] = "10",
            ["price_max"] = "99.5",
            ["created_at_from"] = "2026-01-01",
            ["created_at_to"] = "2026-01-31"
        });

        var result = FilterValueParser.Build(meta, query);

        Assert.Equal("10", result["price_min"]);
        Assert.Equal("99.5", result["price_max"]);
        Assert.Equal("2026-01-01", result["created_at_from"]);
        Assert.Equal("2026-01-31", result["created_at_to"]);
    }

    [Fact]
    public void Build_NormalizesMultiSelectValues_WithDistinctAndCommaJoin()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["category"] = new() { Type = "multi-select" }
        });

        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["category"] = new StringValues(new[] { "A", "A", "B", "" })
        });

        var result = FilterValueParser.Build(meta, query);

        Assert.Equal("A,B", result["category"]);
    }

    [Fact]
    public void Build_KeepsSingleValueForDropdown()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["status"] = new() { Type = "dropdown" }
        });

        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["status"] = "Open"
        });

        var result = FilterValueParser.Build(meta, query);

        Assert.Equal("Open", result["status"]);
    }

    [Fact]
    public void Build_NormalizesCheckboxValues_LikeMultiSelect()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["tag"] = new() { Type = "checkbox" }
        });

        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["tag"] = new StringValues(new[] { "x", "y", "x" })
        });

        var result = FilterValueParser.Build(meta, query);

        Assert.Equal("x,y", result["tag"]);
    }

    [Fact]
    public void Build_SetsNullForEmptyMultiSelect()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["category"] = new() { Type = "multi-select" }
        });

        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["category"] = new StringValues(new[] { "", "   " })
        });

        var result = FilterValueParser.Build(meta, query);

        Assert.Null(result["category"]);
    }

    [Fact]
    public void Build_SetsRangeKeysEvenWhenMissing()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["price"] = new() { Type = "range" }
        });

        var query = new QueryCollection(new Dictionary<string, StringValues>());

        var result = FilterValueParser.Build(meta, query);

        Assert.True(result.ContainsKey("price_min"));
        Assert.True(result.ContainsKey("price_max"));
        Assert.Null(result["price_min"]);
        Assert.Null(result["price_max"]);
    }

    [Fact]
    public void BuildCleared_ResetsAllFilterKeysToNull()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["status"] = new() { Type = "dropdown" },
            ["price"] = new() { Type = "range" },
            ["created_at"] = new() { Type = "date-range" },
            ["category"] = new() { Type = "multi-select" }
        });

        var result = FilterValueParser.BuildCleared(meta);

        Assert.Null(result["status"]);
        Assert.Null(result["price_min"]);
        Assert.Null(result["price_max"]);
        Assert.Null(result["created_at_from"]);
        Assert.Null(result["created_at_to"]);
        Assert.Null(result["category"]);
    }

    [Fact]
    public void Build_ParsesPartialDateRange()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["created_at"] = new() { Type = "date-range" }
        });

        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["created_at_from"] = "2026-02-01"
        });

        var result = FilterValueParser.Build(meta, query);

        Assert.Equal("2026-02-01", result["created_at_from"]);
        Assert.Null(result["created_at_to"]);
    }

    [Fact]
    public void Build_FallsBackToSingleValueForUnknownType()
    {
        var meta = BuildMeta(new Dictionary<string, FilterDefinition>
        {
            ["state"] = new() { Type = "custom-type" }
        });

        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["state"] = "active"
        });

        var result = FilterValueParser.Build(meta, query);

        Assert.Equal("active", result["state"]);
    }

    private static EntityDefinition BuildMeta(Dictionary<string, FilterDefinition> filters)
    {
        return new EntityDefinition
        {
            Table = "Dummy",
            Key = "Id",
            DisplayName = "Dummy",
            Filters = filters
        };
    }
}
