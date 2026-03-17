using NetYamlForge.Models;
using NetYamlForge.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Xunit;

namespace NetYamlForge.Tests;

public class SectionHooksDeserializationTests
{
    private static PageDefinition Deserialize(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new SectionColumnsConverter())
            .WithTypeConverter(new SectionHooksConverter())
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<PageDefinition>(yaml);
    }

    [Fact]
    public void Hooks_CamelCase_BindsCorrectly()
    {
        // camelCase 形式（推奨・entities と同じ記法）
        var yaml = """
            title: Test
            sections:
              - id: sec1
                source_type: table
                source: t
                hooks:
                  beforeCreate: [trim, validate_required]
                  afterCreate: [audit_log]
                  beforeUpdate: [validate_required]
                  afterUpdate:  [audit_log]
                  beforeDelete: [check_ref]
            """;

        var page = Deserialize(yaml);
        var hooks = page.Sections[0].Hooks;

        Assert.NotNull(hooks);
        Assert.Equal(new[] { "trim", "validate_required" }, hooks.BeforeCreate);
        Assert.Equal(new[] { "audit_log" }, hooks.AfterCreate);
        Assert.Equal(new[] { "validate_required" }, hooks.BeforeUpdate);
        Assert.Equal(new[] { "audit_log" }, hooks.AfterUpdate);
        Assert.Equal(new[] { "check_ref" }, hooks.BeforeDelete);
        Assert.Empty(hooks.AfterDelete);
    }

    [Fact]
    public void Hooks_SnakeCase_AlsoBindsCorrectly()
    {
        // snake_case 形式（後方互換）
        var yaml = """
            title: Test
            sections:
              - id: sec1
                source_type: table
                source: t
                hooks:
                  before_create: [trim, validate_required]
                  after_create: [audit_log]
                  before_update: [validate_required]
            """;

        var page = Deserialize(yaml);
        var hooks = page.Sections[0].Hooks;

        Assert.NotNull(hooks);
        Assert.Equal(new[] { "trim", "validate_required" }, hooks.BeforeCreate);
        Assert.Equal(new[] { "audit_log" }, hooks.AfterCreate);
        Assert.Equal(new[] { "validate_required" }, hooks.BeforeUpdate);
    }

    [Fact]
    public void Hooks_SingleString_BindsAsList()
    {
        // 単一文字列形式もリストとして扱われる
        var yaml = """
            title: Test
            sections:
              - id: sec1
                source_type: table
                source: t
                hooks:
                  beforeCreate: trim
            """;

        var page = Deserialize(yaml);
        var hooks = page.Sections[0].Hooks;

        Assert.NotNull(hooks);
        Assert.Equal(new[] { "trim" }, hooks.BeforeCreate);
    }

    [Fact]
    public void Hooks_MultipleHooks_PreserveOrder()
    {
        // フックは定義順に実行される
        var yaml = """
            title: Test
            sections:
              - id: sec1
                source_type: table
                source: t
                hooks:
                  beforeCreate:
                    - normalize_item_name
                    - validate_quantity_positive
                    - validate_price_positive
                    - validate_required
            """;

        var page = Deserialize(yaml);
        var hooks = page.Sections[0].Hooks;

        Assert.NotNull(hooks);
        Assert.Equal(
            new[] { "normalize_item_name", "validate_quantity_positive", "validate_price_positive", "validate_required" },
            hooks.BeforeCreate);
    }

    [Fact]
    public void Hooks_Null_ReturnsNullHooks()
    {
        var yaml = """
            title: Test
            sections:
              - id: sec1
                source_type: table
                source: t
            """;

        var page = Deserialize(yaml);
        Assert.Null(page.Sections[0].Hooks);
    }

    [Fact]
    public void GetFormFields_UpdateAliasesEdit()
    {
        var sec = new SectionDefinition
        {
            TargetPrimaryKey = "id",
            Forms = new Dictionary<string, SectionFormGroupDef>
            {
                ["update"] = new() { Fields = new() { "name", "category" } }
            }
        };

        // "edit" でアクセスしても "update" が返る
        var fields = sec.GetFormFields("edit");
        Assert.Equal(new[] { "name", "category" }, fields);
    }

    [Fact]
    public void GetFormFields_EditAliasesUpdate()
    {
        var sec = new SectionDefinition
        {
            TargetPrimaryKey = "id",
            Forms = new Dictionary<string, SectionFormGroupDef>
            {
                ["edit"] = new() { Fields = new() { "price", "stock" } }
            }
        };

        // "update" でアクセスしても "edit" が返る
        var fields = sec.GetFormFields("update");
        Assert.Equal(new[] { "price", "stock" }, fields);
    }
}
