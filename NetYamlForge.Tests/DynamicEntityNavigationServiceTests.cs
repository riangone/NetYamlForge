using System.Diagnostics.CodeAnalysis;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityNavigationServiceTests
{
    [Fact]
    public void ExtractEntityFromReturnUrl_ReturnsEntity_WhenQueryContainsEntity()
    {
        var sut = new DynamicEntityNavigationService(new StubEntityMetadataProvider());

        var result = sut.ExtractEntityFromReturnUrl("/p/DynamicEntity/Index?entity=orders&search=abc");

        Assert.Equal("orders", result);
    }

    [Fact]
    public void ExtractEntityFromReturnUrl_ReturnsNull_WhenMissingEntity()
    {
        var sut = new DynamicEntityNavigationService(new StubEntityMetadataProvider());

        var result = sut.ExtractEntityFromReturnUrl("/p/DynamicEntity/Index?search=abc");

        Assert.Null(result);
    }

    [Fact]
    public void BuildBreadcrumbChain_BuildsFromOldestToNewest()
    {
        var metadata = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = new() { Table = "Orders", Key = "Id", DisplayName = "Orders" },
            ["customers"] = new() { Table = "Customers", Key = "Id", DisplayName = "Customers" }
        };
        var sut = new DynamicEntityNavigationService(new StubEntityMetadataProvider(metadata));
        var returnUrl = "/p/DynamicEntity/Index?entity=orders&returnUrl=%2Fp%2FDynamicEntity%2FIndex%3Fentity%3Dcustomers";

        var chain = sut.BuildBreadcrumbChain(returnUrl);

        Assert.Equal(2, chain.Count);
        Assert.Equal("Customers", chain[0].Label);
        Assert.Equal("Orders", chain[1].Label);
    }

    [Fact]
    public void BuildBreadcrumbChain_StopsWhenEntityMissing()
    {
        var sut = new DynamicEntityNavigationService(new StubEntityMetadataProvider());
        const string current = "/p/DynamicEntity/Index?entity=orders&returnUrl=%2Fp%2FDynamicEntity%2FIndex%3Fsearch%3Dabc";

        var chain = sut.BuildBreadcrumbChain(current);

        Assert.Single(chain);
        Assert.Equal("orders", chain[0].Label);
    }

    [Fact]
    public void BuildBreadcrumbChain_RespectsMaxDepth()
    {
        var sut = new DynamicEntityNavigationService(new StubEntityMetadataProvider());
        var returnUrl = "/p/DynamicEntity/Index?entity=e1&returnUrl=%2Fp%2FDynamicEntity%2FIndex%3Fentity%3De2%26returnUrl%3D%252Fp%252FDynamicEntity%252FIndex%253Fentity%253De3";

        var chain = sut.BuildBreadcrumbChain(returnUrl, maxDepth: 2);

        Assert.Equal(2, chain.Count);
        Assert.Equal("e2", chain[0].Label);
        Assert.Equal("e1", chain[1].Label);
    }

    private sealed class StubEntityMetadataProvider : IEntityMetadataProvider
    {
        private readonly IReadOnlyDictionary<string, EntityDefinition> _metadata;

        public StubEntityMetadataProvider(IReadOnlyDictionary<string, EntityDefinition>? metadata = null)
        {
            _metadata = metadata ?? new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        }

        public EntityDefinition Get(string entityName) => _metadata[entityName];

        public IReadOnlyDictionary<string, EntityDefinition> GetAll() => _metadata;

        public bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition)
        {
            return _metadata.TryGetValue(entityName, out definition);
        }
    }
}
