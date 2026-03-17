// 目的: 全プロジェクトのエンティティYAMLファイルが entity-schema.json に準拠しているかを
//       自動検証する回帰テスト。YAMLを変更した際のスキーマ違反を早期発見する。

using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// 全プロジェクトの entities/*.yml が entity-schema.json に準拠していることを保証する。
/// 新しいエンティティYAMLを追加した場合、このテストが自動的にそのファイルを検証対象に含める。
/// </summary>
public class YamlSchemaValidationTests
{
    // テスト対象: リポジトリ内の全プロジェクトから entities YAML ファイルを収集
    private static readonly string ProjectsRoot = FindProjectsRoot();

    private static string FindProjectsRoot()
    {
        // テストバイナリから NetYamlForge/projects/ を探す
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "NetYamlForge.Tests")
            dir = dir.Parent;

        var root = dir?.Parent?.FullName
            ?? throw new InvalidOperationException("リポジトリルートが見つかりません。");

        return Path.Combine(root, "NetYamlForge", "projects");
    }

    /// <summary>
    /// 全プロジェクトの entities/*.yml ファイルを収集して Theory データとして提供する。
    /// </summary>
    public static IEnumerable<object[]> AllEntityYamlFiles()
    {
        if (!Directory.Exists(ProjectsRoot))
            yield break;

        foreach (var projectDir in Directory.GetDirectories(ProjectsRoot))
        {
            var entitiesDir = Path.Combine(projectDir, "entities");
            if (!Directory.Exists(entitiesDir))
                continue;

            foreach (var yamlFile in Directory.GetFiles(entitiesDir, "*.yml"))
            {
                // テスト名が読みやすくなるよう相対パスにする
                var relativePath = Path.GetRelativePath(
                    Path.GetDirectoryName(ProjectsRoot)!, yamlFile);
                yield return new object[] { yamlFile, relativePath };
            }

            foreach (var yamlFile in Directory.GetFiles(entitiesDir, "*.yaml"))
            {
                var relativePath = Path.GetRelativePath(
                    Path.GetDirectoryName(ProjectsRoot)!, yamlFile);
                yield return new object[] { yamlFile, relativePath };
            }
        }
    }

    /// <summary>
    /// 全プロジェクトのエンティティYAMLがスキーマに準拠していること。
    /// YAMLを変更して壊した場合、このテストが失敗する。
    /// </summary>
    [Theory]
    [MemberData(nameof(AllEntityYamlFiles))]
    public void EntityYaml_ConformsToSchema(string fullPath, string displayPath)
    {
        var yaml = File.ReadAllText(fullPath);

        var ex = Record.Exception(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, displayPath));

        Assert.Null(ex);
    }

    /// <summary>
    /// 全プロジェクトの project.yaml がスキーマに準拠していること。
    /// </summary>
    public static IEnumerable<object[]> AllProjectYamlFiles()
    {
        if (!Directory.Exists(ProjectsRoot))
            yield break;

        foreach (var projectDir in Directory.GetDirectories(ProjectsRoot))
        {
            foreach (var name in new[] { "project.yaml", "project.yml" })
            {
                var path = Path.Combine(projectDir, name);
                if (File.Exists(path))
                {
                    var rel = Path.GetRelativePath(
                        Path.GetDirectoryName(ProjectsRoot)!, path);
                    yield return new object[] { path, rel };
                    break;
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllProjectYamlFiles))]
    public void ProjectYaml_ConformsToSchema(string fullPath, string displayPath)
    {
        var yaml = File.ReadAllText(fullPath);

        var ex = Record.Exception(() =>
            YamlSchemaValidator.ValidateProjectYaml(yaml, displayPath));

        Assert.Null(ex);
    }

    // ─────────────────────────────────────────────────────────────
    // 固定シナリオ: よくある間違いのパターンを検出できることを確認
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EntityYaml_WithoutTable_FailsValidation()
    {
        const string yaml = """
            entities:
              Product:
                key: id
                displayName: "Products"
                columns:
                  name: {type: string}
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, "test.yml"));

        Assert.Contains("スキーマ検証に失敗", ex.Message);
    }

    [Fact]
    public void EntityYaml_WithoutKeyOrKeys_FailsValidation()
    {
        const string yaml = """
            entities:
              Product:
                table: product
                displayName: "Products"
                columns:
                  name: {type: string}
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, "test.yml"));

        Assert.Contains("スキーマ検証に失敗", ex.Message);
    }

    [Fact]
    public void EntityYaml_WithoutDisplayName_FailsValidation()
    {
        const string yaml = """
            entities:
              Product:
                table: product
                key: id
                columns:
                  name: {type: string}
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, "test.yml"));

        Assert.Contains("スキーマ検証に失敗", ex.Message);
    }

    [Fact]
    public void EntityYaml_MinimalValid_PassesValidation()
    {
        const string yaml = """
            entities:
              Product:
                table: product
                key: id
                displayName: "Products"
            """;

        var ex = Record.Exception(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, "test.yml"));

        Assert.Null(ex);
    }

    [Fact]
    public void EntityYaml_WithCompositeKeys_PassesValidation()
    {
        const string yaml = """
            entities:
              OrderItem:
                table: order_item
                keys: [order_id, product_id]
                displayName: "Order Items"
                columns:
                  quantity: {type: int}
            """;

        var ex = Record.Exception(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, "test.yml"));

        Assert.Null(ex);
    }

    [Fact]
    public void EntityYaml_WithI18nDisplayName_PassesValidation()
    {
        const string yaml = """
            entities:
              Customer:
                table: customer
                key: id
                displayNameI18n:
                  en-US: "Customers"
                  ja-JP: "顧客"
                columns:
                  name: {type: string}
            """;

        var ex = Record.Exception(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, "test.yml"));

        Assert.Null(ex);
    }

    [Fact]
    public void EntityYaml_WithHooks_PassesValidation()
    {
        // 実際の YAML は camelCase キー（beforeCreate, afterCreate 等）を使う
        const string yaml = """
            entities:
              Customer:
                table: customer
                key: id
                displayName: "Customers"
                hooks:
                  beforeCreate: [trim, validate_email]
                  afterCreate: [audit_log]
            """;

        var ex = Record.Exception(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, "test.yml"));

        Assert.Null(ex);
    }

    [Fact]
    public void EntityYaml_WithPresetsHook_PassesValidation()
    {
        // presets セクション + @ 参照パターンの検証
        const string yaml = """
            entities:
              Post:
                table: posts
                key: id
                displayName: "Posts"
                hooks:
                  presets:
                    common_before:
                      - validate_required
                      - trim
                  beforeCreate:
                    - '@common_before'
                    - audit_log
            """;

        var ex = Record.Exception(() =>
            YamlSchemaValidator.ValidateEntityYaml(yaml, "test.yml"));

        Assert.Null(ex);
    }
}
