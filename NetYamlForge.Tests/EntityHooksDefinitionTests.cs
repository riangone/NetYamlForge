using System;
using System.Collections.Generic;
using System.IO;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class EntityHooksDefinitionTests
{
    private readonly IEntityHooksService _entityHooks = new EntityHooksService();

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

        var expanded = _entityHooks.GetExpandedHookList(hooks, h => h.BeforeCreate);

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

        var expanded = _entityHooks.GetExpandedHookList(hooks, h => h.BeforeUpdate);

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

        var expanded = _entityHooks.GetExpandedHookList(hooks, h => h.BeforeCreate, warnings.Add);

        Assert.NotNull(expanded);
        Assert.Contains("trim:Name", expanded!);
        Assert.Contains("validate_required:Name", expanded);
        Assert.Contains(warnings, w => w.Contains("循環参照", StringComparison.Ordinal) || w.Contains("circular", StringComparison.OrdinalIgnoreCase) || w.Contains("循環", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TaskYaml_DeserializesHooksCorrectly()
    {
        var yaml = File.ReadAllText("../../../../NetYamlForge/projects/todo-app/entities/task.yml");
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var root = deserializer.Deserialize<EntityConfigRoot>(yaml);
        var taskDef = root.Entities["task"];
        Assert.NotNull(taskDef.Hooks);
        var bc = _entityHooks.GetHookList(taskDef.Hooks!, h => h.BeforeCreate);
        Assert.NotNull(bc);
        Assert.Contains("normalize_title", bc!);
        Assert.Contains("validate_due_date", bc!);
    }
}
