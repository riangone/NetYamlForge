// ファイル概要: YAML ファイルを JSON Schema で検証するユーティリティです。
// YamlDotNet でデシリアライズ後に System.Text.Json で JSON 変換し、JsonSchema.Net で評価します。

using System.Text.Json;
using System.Globalization;
using Json.Schema;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public static class YamlSchemaValidator
{
    private static JsonSchema? _projectSchema;
    private static JsonSchema? _uiPageSchema;
    private static JsonSchema? _entitySchema;
    private static JsonSchema? _dashboardSchema;
    private static readonly object _lock = new();

    /// <summary>
    /// project.yaml の内容を組み込みスキーマで検証します。
    /// 違反がある場合は InvalidOperationException をスローします。
    /// </summary>
    public static void ValidateProjectYaml(string yamlContent, string filePath)
    {
        var schema = GetProjectSchema();
        ValidateBySchema(schema, yamlContent, filePath, "project.yaml");
    }

    /// <summary>
    /// pages/*.yaml の内容を UI ページスキーマで検証します。
    /// 違反がある場合は InvalidOperationException をスローします。
    /// </summary>
    public static void ValidateUiPageYaml(string yamlContent, string filePath)
    {
        var schema = GetUiPageSchema();
        ValidateBySchema(schema, yamlContent, filePath, "ui-page");
    }

    /// <summary>
    /// entities/*.yml の内容を Entity スキーマで検証します。
    /// 違反がある場合は InvalidOperationException をスローします。
    /// </summary>
    public static void ValidateEntityYaml(string yamlContent, string filePath)
    {
        var schema = GetEntitySchema();
        ValidateBySchema(schema, yamlContent, filePath, "entity");
    }

    /// <summary>
    /// dashboard.yml の内容を Dashboard スキーマで検証します。
    /// 違反がある場合は InvalidOperationException をスローします。
    /// </summary>
    public static void ValidateDashboardYaml(string yamlContent, string filePath)
    {
        var schema = GetDashboardSchema();
        ValidateBySchema(schema, yamlContent, filePath, "dashboard");
    }

    private static void ValidateBySchema(JsonSchema schema, string yamlContent, string filePath, string schemaName)
    {
        var jsonElement = ConvertYamlToJsonElement(yamlContent);
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

            throw new InvalidOperationException(
                $"{schemaName} スキーマ検証に失敗しました: {filePath}\n{string.Join("\n", errors)}");
        }
    }

    private static JsonSchema GetProjectSchema()
    {
        return _projectSchema ??= LoadSchemaFromResource("project-schema.json");
    }

    private static JsonSchema GetUiPageSchema()
    {
        return _uiPageSchema ??= LoadSchemaFromResource("ui-page-schema.json");
    }

    private static JsonSchema GetEntitySchema()
    {
        return _entitySchema ??= LoadSchemaFromResource("entity-schema.json");
    }

    private static JsonSchema GetDashboardSchema()
    {
        return _dashboardSchema ??= LoadSchemaFromResource("dashboard-schema.json");
    }

    private static JsonSchema LoadSchemaFromResource(string schemaFileName)
    {
        lock (_lock)
        {
            var assembly = typeof(YamlSchemaValidator).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(schemaFileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{schemaFileName} が埋め込みリソースに見つかりません。");

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"リソースストリームが取得できません: {resourceName}");
            using var reader = new StreamReader(stream);
            var schemaJson = reader.ReadToEnd();
            return JsonSchema.FromText(schemaJson);
        }
    }

    private static JsonElement ConvertYamlToJsonElement(string yaml)
    {
        // YamlDotNet で object グラフとして読み込み、JSON 経由で JsonElement に変換します。
        // Deserialize<object> ではスカラー値が string になるため、
        // ConvertYamlValue で YAML の boolean/number を適切な型に変換します。
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var raw = deserializer.Deserialize<object>(yaml);
        var converted = ConvertYamlValue(raw);
        var json = JsonSerializer.Serialize(converted);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static object? ConvertYamlValue(object? value) => value switch
    {
        // YAML mapping → string キーの Dictionary に変換
        Dictionary<object, object> dict => dict.ToDictionary(
            kv => kv.Key?.ToString() ?? "",
            kv => ConvertYamlValue(kv.Value)),
        // YAML sequence → List に変換
        List<object> list => list.Select(ConvertYamlValue).ToList(),
        // YAML boolean は string で来るため bool に変換
        string s when s.Equals("true", StringComparison.OrdinalIgnoreCase) => (object)true,
        string s when s.Equals("false", StringComparison.OrdinalIgnoreCase) => (object)false,
        // YAML number も string で来るため数値に変換
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
        string s when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => value
    };
}
