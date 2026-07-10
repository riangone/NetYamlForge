using System.IO;
using NetYamlForge.Services.Cli;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// --ai-scaffold の gate #1（spec 静的検証）に対するテスト。
/// ここで弾かれるべきエラーは、生成処理（DB/ファイル操作）が始まる前に検出されなければならない。
/// </summary>
public class AiScaffoldSpecTests
{
    private const string ValidSpecYaml = """
project: helpdesk-mini
displayName: Helpdesk Mini
dbType: sqlite
entities:
  - table: ticket
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
      - { name: subject, type: text, notNull: true }
  - table: ticket_comment
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
      - name: ticket_id
        type: integer
        notNull: true
        foreignKey: { table: ticket, column: id }
      - { name: body, type: text, notNull: true }
hooks:
  - name: TicketStatusGuard
    entity: ticket
batchJobs:
  - name: stale_ticket_reminder
acceptanceCriteria:
  - "ticket entity generated"
""";

    private static AiScaffoldSpec LoadFromString(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ai-scaffold-spec-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            return AiScaffoldSpec.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_ValidSpec_ReturnsNoErrors()
    {
        var spec = LoadFromString(ValidSpecYaml);
        var errors = spec.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => AiScaffoldSpec.Load("/nonexistent/path/spec.yaml"));
    }

    [Fact]
    public void Validate_NoEntities_ReturnsError()
    {
        var spec = LoadFromString("""
project: empty-project
dbType: sqlite
entities: []
""");
        var errors = spec.Validate();
        Assert.Contains(errors, e => e.Contains("entities", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateTableNames_ReturnsError()
    {
        var spec = LoadFromString("""
project: dup-project
dbType: sqlite
entities:
  - table: ticket
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
  - table: ticket
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
""");
        var errors = spec.Validate();
        Assert.Contains(errors, e => e.Contains("重複", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingPrimaryKey_ReturnsError()
    {
        var spec = LoadFromString("""
project: no-pk-project
dbType: sqlite
entities:
  - table: widget
    columns:
      - { name: name, type: text, notNull: true }
""");
        var errors = spec.Validate();
        Assert.Contains(errors, e => e.Contains("primaryKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DisallowedColumnType_ReturnsError()
    {
        var spec = LoadFromString("""
project: bad-type-project
dbType: sqlite
entities:
  - table: widget
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
      - { name: payload, type: json }
""");
        var errors = spec.Validate();
        Assert.Contains(errors, e => e.Contains("payload", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ForeignKeyReferencesUnknownTable_ReturnsError()
    {
        var spec = LoadFromString("""
project: dangling-fk-project
dbType: sqlite
entities:
  - table: widget
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
      - name: owner_id
        type: integer
        foreignKey: { table: nonexistent_table, column: id }
""");
        var errors = spec.Validate();
        Assert.Contains(errors, e => e.Contains("nonexistent_table", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UnsupportedDbType_ReturnsError()
    {
        var spec = LoadFromString("""
project: sqlserver-project
dbType: sqlserver
entities:
  - table: widget
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
""");
        var errors = spec.Validate();
        Assert.Contains(errors, e => e.Contains("dbType", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_InvalidProjectName_ReturnsError()
    {
        var spec = LoadFromString("""
project: Invalid_Project_Name
dbType: sqlite
entities:
  - table: widget
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
""");
        var errors = spec.Validate();
        Assert.Contains(errors, e => e.Contains("project", StringComparison.Ordinal));
    }
}
