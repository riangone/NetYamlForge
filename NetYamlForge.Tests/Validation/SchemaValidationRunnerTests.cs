// ファイル概要: R2-01 SchemaValidationRunner の単体テスト。
// 一時ディレクトリに projects レイアウトを再現し、合法/非法 YAML fixture で
// 違反件数と JSON Pointer を検証する。

using NetYamlForge.Services;
using NetYamlForge.Services.Validation;
using Xunit;

namespace NetYamlForge.Tests.Validation;

public sealed class SchemaValidationRunnerTests : IDisposable
{
    private readonly string _root;
    private readonly SchemaValidationRunner _runner = new();

    public SchemaValidationRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nyf-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ベストエフォート */ }
    }

    private string WriteFile(string relative, string content)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private const string ValidEntity = """
imports: []
entities:
  category:
    table: Category
    key: Id
    displayName: Category
    columns:
      Id:
        type: int
      Name:
        type: string
""";

    // columns.Amount.type がスキーマ enum 外 → /entities/bad/columns/Amount/type で違反。
    private const string InvalidEntity = """
imports: []
entities:
  bad:
    table: Bad
    columns:
      Amount:
        type: moneyy
""";

    [Fact]
    public void ValidateAll_ValidFixtures_NoViolations()
    {
        WriteFile("demo/entities/category.yml", ValidEntity);

        var violations = _runner.ValidateAll(_root, new SchemaValidationOptions());

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateAll_InvalidEntity_ReportsExpectedPointer()
    {
        WriteFile("demo/entities/bad.yml", InvalidEntity);

        var violations = _runner.ValidateAll(_root, new SchemaValidationOptions());

        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Pointer.Contains("/entities/bad/columns/Amount/type"));
        Assert.All(violations, v => Assert.Equal("Entity", v.SchemaName));
    }

    [Fact]
    public void ValidateAll_NonSchemaFiles_AreSkipped()
    {
        // jobs/ 配下はスキーマ対象外 → たとえ中身が壊れていても違反ゼロ。
        WriteFile("demo/jobs/whatever.yml", "this: [is, not, validated");

        var violations = _runner.ValidateAll(_root, new SchemaValidationOptions());

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateAll_ExcludeGlob_IsHonored()
    {
        WriteFile("demo/_disabled/entities/bad.yml", InvalidEntity);

        var violations = _runner.ValidateAll(_root, new SchemaValidationOptions());

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateAll_EntityNamedProject_IsClassifiedAsEntityNotManifest()
    {
        // 回帰: entities/ 配下の project.yml は「project という名のエンティティ」であり、
        // プロジェクト マニフェスト（Project schema）ではない。ディレクトリ規約がファイル名に優先する。
        WriteFile("demo/entities/project.yml", InvalidEntity);

        var violations = _runner.ValidateAll(_root, new SchemaValidationOptions());

        Assert.NotEmpty(violations);
        Assert.All(violations, v => Assert.Equal("Entity", v.SchemaName));
    }

    [Fact]
    public void ValidateFile_MalformedYaml_ReportedAsSingleViolation()
    {
        var path = WriteFile("demo/entities/broken.yml", "entities: : : :\n  - [unbalanced");

        var violations = _runner.ValidateFile(path, YamlSchemaValidator.SchemaKind.Entity);

        Assert.Single(violations);
        Assert.Equal("/", violations[0].Pointer);
    }
}
