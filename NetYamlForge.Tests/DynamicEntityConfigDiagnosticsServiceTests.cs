using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityConfigDiagnosticsServiceTests
{
    [Fact]
    public void Build_FallsBackToFirstEntity_WhenRequestedMissing()
    {
        var all = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer"] = new() { Table = "Customer", Key = "Id", DisplayName = "Customer" }
        };
        var sut = new DynamicEntityConfigDiagnosticsService(
            new StubBaseProvider(),
            new DynamicEntityConfigDiffService(),
            NullLogger<DynamicEntityConfigDiagnosticsService>.Instance);

        var result = sut.Build("missing", all, onlyChanged: true);

        Assert.Equal("customer", result.SelectedEntity);
        Assert.Single(result.Entities);
        Assert.Equal("customer", result.Entities[0]);
    }

    [Fact]
    public void Build_UsesEmptyJson_WhenMetadataMissing()
    {
        var all = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
        var sut = new DynamicEntityConfigDiagnosticsService(
            new StubBaseProvider(),
            new DynamicEntityConfigDiffService(),
            NullLogger<DynamicEntityConfigDiagnosticsService>.Instance);

        var result = sut.Build("customer", all, onlyChanged: true);

        Assert.Equal("{}", result.BaseJson);
        Assert.Equal("{}", result.EffectiveJson);
    }

    [Fact]
    public void Build_ComputesDiffLines_WhenBaseAndEffectiveDiffer()
    {
        var all = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders"] = new() { Table = "Orders", Key = "Id", DisplayName = "Orders Runtime" }
        };
        var baseMeta = new EntityDefinition { Table = "Orders", Key = "Id", DisplayName = "Orders Base" };
        var sut = new DynamicEntityConfigDiagnosticsService(
            new StubBaseProvider(baseMeta),
            new DynamicEntityConfigDiffService(),
            NullLogger<DynamicEntityConfigDiagnosticsService>.Instance);

        var result = sut.Build("orders", all, onlyChanged: true);

        Assert.True(result.ChangedCount >= 1);
        Assert.Contains(result.DiffLines, l => l.Contains("$.DisplayName", StringComparison.Ordinal));
    }

    private sealed class StubBaseProvider : IBaseEntityMetadataProvider
    {
        private readonly EntityDefinition? _meta;

        public StubBaseProvider(EntityDefinition? meta = null)
        {
            _meta = meta;
        }

        public bool TryGet(string entityName, out EntityDefinition? definition)
        {
            definition = _meta;
            return definition != null;
        }
    }
}
