using NetYamlForge.Controllers;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityFormViewModelFactoryTests
{
    [Fact]
    public void Build_SetsAllFields_ForCreateErrorCase()
    {
        var sut = new DynamicEntityFormViewModelFactory();
        var meta = new EntityDefinition { Table = "T", Key = "Id", DisplayName = "T" };
        var fkData = new Dictionary<string, IEnumerable<dynamic>>();
        var errors = new Dictionary<string, string> { ["Name"] = "required" };
        var submitted = new Dictionary<string, string?> { ["Name"] = "" };

        var vm = sut.Build("customer", meta, null, fkData, errors, "modal", null, submitted);

        Assert.Equal("customer", vm.Entity);
        Assert.Equal(meta, vm.Meta);
        Assert.Null(vm.Item);
        Assert.Equal("modal", vm.Mode);
        Assert.Equal("required", vm.Errors["Name"]);
        Assert.Equal("", vm.SubmittedValues!["Name"]);
    }

    [Fact]
    public void Build_SetsBreadcrumbs_ForPageMode()
    {
        var sut = new DynamicEntityFormViewModelFactory();
        var meta = new EntityDefinition { Table = "T", Key = "Id", DisplayName = "T" };
        var fkData = new Dictionary<string, IEnumerable<dynamic>>();
        var crumbs = new List<BreadcrumbItem> { new("List", "/x") };

        var vm = sut.Build("customer", meta, new { Id = 1 }, fkData, mode: "page", breadcrumbChain: crumbs);

        Assert.Equal("page", vm.Mode);
        Assert.NotNull(vm.Item);
        Assert.Equal(crumbs, vm.BreadcrumbChain);
    }
}

