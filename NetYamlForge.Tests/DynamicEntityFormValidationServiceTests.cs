using NetYamlForge.Models;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityFormValidationServiceTests
{
    [Fact]
    public void ConvertAndValidate_SkipsIdentityColumn()
    {
        var sut = new DynamicEntityFormValidationService(new ValueConverter());
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "Id",
            DisplayName = "Orders",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Id"] = new() { Type = "int", Identity = true },
                ["Name"] = new() { Type = "string" }
            }
        };
        var form = new Dictionary<string, string?> { ["Id"] = "10", ["Name"] = "A" };

        var (values, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Empty(errors);
        Assert.False(values.ContainsKey("Id"));
        Assert.Equal("A", values["Name"]);
    }

    [Fact]
    public void ConvertAndValidate_DefaultsMissingBoolToFalse()
    {
        var sut = new DynamicEntityFormValidationService(new ValueConverter());
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "Id",
            DisplayName = "Orders",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["IsActive"] = new() { Type = "bool" }
            }
        };
        var form = new Dictionary<string, string?>();

        var (values, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Empty(errors);
        Assert.Equal(false, values["IsActive"]);
    }

    [Fact]
    public void ConvertAndValidate_ReturnsError_WhenConversionFails()
    {
        var sut = new DynamicEntityFormValidationService(new ValueConverter());
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "Id",
            DisplayName = "Orders",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Amount"] = new() { Type = "int" }
            }
        };
        var form = new Dictionary<string, string?> { ["Amount"] = "abc" };

        var (_, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Equal("Invalid integer", errors["Amount"]);
    }
}
