using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Json.Schema;
using Xunit;
using System.Globalization;

namespace NetYamlForge.Tests;

public class JsonSchemaExportTests
{
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "NetYamlForge.slnx")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not locate repo root.");
    }

    private static readonly string RepoRoot = GetRepoRoot();
    private static readonly string ProjectsDir = Path.Combine(RepoRoot, "NetYamlForge", "projects");
    private static readonly string SchemasDir = Path.Combine(RepoRoot, "docs", "schemas");

    private JsonSchema LoadSchema(string schemaName)
    {
        var path = Path.Combine(SchemasDir, schemaName);
        var json = File.ReadAllText(path);
        return JsonSchema.FromText(json);
    }

    private JsonElement ConvertYamlToJsonElement(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var raw = deserializer.Deserialize<object>(yaml);
        var converted = ConvertYamlValue(raw);
        var json = JsonSerializer.Serialize(converted);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private object? ConvertYamlValue(object? value) => value switch
    {
        Dictionary<object, object> dict => dict.ToDictionary(
            kv => kv.Key?.ToString() ?? "",
            kv => ConvertYamlValue(kv.Value)),
        List<object> list => list.Select(ConvertYamlValue).ToList(),
        string s when s.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
        string s when s.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
        string s when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => value
    };

    [Fact]
    public void ValidateAllProjectYamlsAgainstExportedSchemas()
    {
        // 确保导出目录及 schema 存在
        if (!Directory.Exists(SchemasDir))
        {
            Directory.CreateDirectory(SchemasDir);
        }

        // 手動在測試運行時執行一次導出以備測試
        var toolingAssembly = System.Reflection.Assembly.Load("NetYamlForge.Tooling");
        var exporterType = toolingAssembly.GetType("NetYamlForge.Services.Cli.JsonSchemaExporter");
        var exportMethod = exporterType.GetMethod("Export", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        exportMethod.Invoke(null, new object[] { SchemasDir });

        var projectSchema = LoadSchema("project.schema.json");
        var entitiesSchema = LoadSchema("entities.schema.json");
        var pagesSchema = LoadSchema("pages.schema.json");
        var dashboardSchema = LoadSchema("dashboard.schema.json");

        var directories = Directory.GetDirectories(ProjectsDir);
        foreach (var dir in directories)
        {
            var projectYamlPath = Path.Combine(dir, "project.yaml");
            if (File.Exists(projectYamlPath))
            {
                ValidateFile(projectSchema, projectYamlPath, "project.yaml");
            }

            var entitiesDir = Path.Combine(dir, "entities");
            if (Directory.Exists(entitiesDir))
            {
                var files = Directory.GetFiles(entitiesDir, "*.yml")
                    .Concat(Directory.GetFiles(entitiesDir, "*.yaml"));
                foreach (var file in files)
                {
                    ValidateFile(entitiesSchema, file, "entity");
                }
            }

            var pagesDir = Path.Combine(dir, "pages");
            if (Directory.Exists(pagesDir))
            {
                var files = Directory.GetFiles(pagesDir, "*.yml")
                    .Concat(Directory.GetFiles(pagesDir, "*.yaml"));
                foreach (var file in files)
                {
                    ValidateFile(pagesSchema, file, "ui-page");
                }
            }

            var dashboardYamlPath = Path.Combine(dir, "dashboard.yml");
            if (File.Exists(dashboardYamlPath))
            {
                ValidateFile(dashboardSchema, dashboardYamlPath, "dashboard");
            }
        }
    }

    private void ValidateFile(JsonSchema schema, string filePath, string schemaName)
    {
        var yaml = File.ReadAllText(filePath);
        var jsonElement = ConvertYamlToJsonElement(yaml);
        var result = schema.Evaluate(jsonElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        if (!result.IsValid)
        {
            var errors = (result.Details ?? Enumerable.Empty<EvaluationResults>())
                .Where(d => !d.IsValid && d.Errors != null)
                .SelectMany(d => d.Errors!.Select(e => $"  [{d.InstanceLocation}] {e.Key}: {e.Value}"))
                .ToList();

            Assert.Fail($"{schemaName} schema validation failed for: {filePath}\n{string.Join("\n", errors)}");
        }
    }
}
