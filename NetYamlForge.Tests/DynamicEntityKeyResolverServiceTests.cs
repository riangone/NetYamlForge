using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityKeyResolverServiceTests
{
    [Fact]
    public void ResolvePrimaryKeyValue_PrefersIdParameter()
    {
        var sut = new DynamicEntityKeyResolverService();
        var meta = new EntityDefinition { Table = "T", Key = "Id", DisplayName = "T" };
        var query = new QueryCollection(new Dictionary<string, StringValues> { ["Id"] = "99" });

        var result = sut.ResolvePrimaryKeyValue(meta, "42", query);

        Assert.Equal("42", result);
    }

    [Fact]
    public void ResolvePrimaryKeyValue_FallsBackToQueryPrimaryKey()
    {
        var sut = new DynamicEntityKeyResolverService();
        var meta = new EntityDefinition { Table = "T", Key = "Id", DisplayName = "T" };
        var query = new QueryCollection(new Dictionary<string, StringValues> { ["Id"] = "99" });

        var result = sut.ResolvePrimaryKeyValue(meta, null, query);

        Assert.Equal("99", result);
    }

    [Fact]
    public void ResolveKeyValues_ParsesCompositeIdJson_WhenQueryMissing()
    {
        var sut = new DynamicEntityKeyResolverService();
        var meta = new EntityDefinition
        {
            Table = "T",
            Key = "OrderId",
            Keys = new List<string> { "OrderId", "LineId" },
            DisplayName = "T"
        };
        var query = new QueryCollection(new Dictionary<string, StringValues>());

        var values = sut.ResolveKeyValues(meta, "{\"OrderId\":10,\"LineId\":20}", query);

        Assert.Equal("10", values["OrderId"]?.ToString());
        Assert.Equal("20", values["LineId"]?.ToString());
    }
}
