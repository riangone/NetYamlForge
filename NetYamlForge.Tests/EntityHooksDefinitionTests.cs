using NetYamlForge.Models;
using Xunit;

namespace NetYamlForge.Tests;

public class EntityHooksDefinitionTests
{
    [Fact]
    public void GetExpandedHookList_ExpandsSinglePreset()
    {
        var hooks = new EntityHooksDefinition
        {
            Presets = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["commonValidation"] = new List<object>
                {
                    "validate_required:Title,Slug",
                    "trim:Title,Slug"
                }
            },
            BeforeCreate = new List<object> { "@commonValidation", "blog_post_slug_generator" }
        };

        var expanded = hooks.GetExpandedHookList(h => h.BeforeCreate);

        Assert.NotNull(expanded);
        Assert.Equal(
            new[]
            {
                "validate_required:Title,Slug",
                "trim:Title,Slug",
                "blog_post_slug_generator"
            },
            expanded);
    }

    [Fact]
    public void GetExpandedHookList_ExpandsNestedPreset()
    {
        var hooks = new EntityHooksDefinition
        {
            Presets = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["base"] = new List<object> { "trim:FirstName,LastName" },
                ["withEmail"] = new List<object> { "@base", "validate_email:Email" }
            },
            BeforeUpdate = new List<object> { "@withEmail", "audit_log" }
        };

        var expanded = hooks.GetExpandedHookList(h => h.BeforeUpdate);

        Assert.NotNull(expanded);
        Assert.Equal(
            new[]
            {
                "trim:FirstName,LastName",
                "validate_email:Email",
                "audit_log"
            },
            expanded);
    }

    [Fact]
    public void GetExpandedHookList_DetectsCircularReference_AndSkipsCycle()
    {
        var warnings = new List<string>();
        var hooks = new EntityHooksDefinition
        {
            Presets = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["a"] = new List<object> { "@b", "trim:Name" },
                ["b"] = new List<object> { "@a", "validate_required:Name" }
            },
            BeforeCreate = new List<object> { "@a" }
        };

        var expanded = hooks.GetExpandedHookList(h => h.BeforeCreate, warnings.Add);

        Assert.NotNull(expanded);
        Assert.Contains("trim:Name", expanded!);
        Assert.Contains("validate_required:Name", expanded);
        Assert.Contains(warnings, w => w.Contains("循環参照", StringComparison.Ordinal));
    }
}
