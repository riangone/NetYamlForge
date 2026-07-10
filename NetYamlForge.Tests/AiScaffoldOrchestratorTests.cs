// DCS003 抑制理由: --ai-scaffold の生成結果を検証するため、テスト側から直接 sqlite ファイルを覗く必要がある。
#pragma warning disable DCS003

using System.IO;
using Microsoft.Data.Sqlite;
using NetYamlForge.Services.Cli;
using Xunit;

namespace NetYamlForge.Tests;

/// <summary>
/// --ai-scaffold パイプライン全体の統合テスト。
/// 実際に一時ディレクトリを content root として、spec → DB スキーマ投入 → entities YAML 逆生成 →
/// hook/batchJob 雛形 → validate-project という全ゲートを最後まで走らせ、成果物を検証する。
/// </summary>
public class AiScaffoldOrchestratorTests : IDisposable
{
    private readonly string _tempRoot;

    public AiScaffoldOrchestratorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ai-scaffold-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "projects"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string WriteSpec(string yaml)
    {
        var path = Path.Combine(_tempRoot, "spec.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private const string HelpdeskSpecYaml = """
project: helpdesk-mini
displayName: Helpdesk Mini
dbType: sqlite
entities:
  - table: ticket
    columns:
      - { name: id, type: integer, primaryKey: true, identity: true }
      - { name: subject, type: text, notNull: true }
      - { name: status, type: text, notNull: true, default: "'open'" }
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
  - "ticket_comment entity generated"
  - "TicketStatusGuard hook generated"
  - "stale_ticket_reminder batch job generated"
""";

    [Fact]
    public void Run_ValidSpec_PassesAllGatesAndGeneratesEntityYamlFromRealSchema()
    {
        var specPath = WriteSpec(HelpdeskSpecYaml);
        var result = new CliScaffoldResult { Command = "ai-scaffold" };

        var exitCode = AiScaffoldOrchestrator.Run(_tempRoot, specPath, enableAiReview: false, result);

        Assert.Equal(0, exitCode);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(result.Errors);

        var projectDir = Path.Combine(_tempRoot, "projects", "helpdesk-mini");
        Assert.True(File.Exists(Path.Combine(projectDir, "entities", "ticket.yml")));
        Assert.True(File.Exists(Path.Combine(projectDir, "entities", "ticket_comment.yml")));
        Assert.True(File.Exists(Path.Combine(projectDir, "Hooks", "TicketStatusGuardHook.cs")));
        Assert.True(File.Exists(Path.Combine(projectDir, "jobs", "stale_ticket_reminder.yml")));
        Assert.True(File.Exists(Path.Combine(projectDir, "pages", "StarterOverview.yaml")));

        // entities YAML は実 DB スキーマから逆生成されたものであり、FK もエンティティ参照として反映されている。
        var ticketCommentYaml = File.ReadAllText(Path.Combine(projectDir, "entities", "ticket_comment.yml"));
        Assert.Contains("foreignKey", ticketCommentYaml, StringComparison.Ordinal);
        Assert.Contains("entity: ticket", ticketCommentYaml, StringComparison.Ordinal);

        // gate #4 の受け入れ基準チェックリストがすべて機械照合で ✅ になっている。
        Assert.Contains(result.Messages, m => m.Contains("[gate4] ✅ \"ticket entity generated\"", StringComparison.Ordinal)
            || m.Contains("[gate4] ✅ ticket entity generated", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_ValidSpec_DbActuallyHasExpectedTablesAndForeignKey()
    {
        var specPath = WriteSpec(HelpdeskSpecYaml);
        var result = new CliScaffoldResult { Command = "ai-scaffold" };
        AiScaffoldOrchestrator.Run(_tempRoot, specPath, enableAiReview: false, result);

        var dbPath = Path.Combine(_tempRoot, "projects", "helpdesk-mini", "database", "helpdesk-mini.db");
        Assert.True(File.Exists(dbPath));

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read()) tables.Add(reader.GetString(0));

        Assert.Contains("ticket", tables);
        Assert.Contains("ticket_comment", tables);
    }

    [Fact]
    public void Run_InvalidSpec_FailsFastAtGate1WithoutTouchingFileSystem()
    {
        var specPath = WriteSpec("""
project: bad
dbType: sqlite
entities: []
""");
        var result = new CliScaffoldResult { Command = "ai-scaffold" };

        var exitCode = AiScaffoldOrchestrator.Run(_tempRoot, specPath, enableAiReview: false, result);

        Assert.Equal(1, exitCode);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.False(Directory.Exists(Path.Combine(_tempRoot, "projects", "bad")));
    }

    [Fact]
    public void Run_MissingSpecPath_FailsWithClearError()
    {
        var result = new CliScaffoldResult { Command = "ai-scaffold" };
        var exitCode = AiScaffoldOrchestrator.Run(_tempRoot, specPath: null, enableAiReview: false, result);

        Assert.Equal(1, exitCode);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("--spec", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_TwiceOnSameProject_SecondRunReusesExistingSkeleton()
    {
        var specPath = WriteSpec(HelpdeskSpecYaml);
        var first = new CliScaffoldResult { Command = "ai-scaffold" };
        var firstExit = AiScaffoldOrchestrator.Run(_tempRoot, specPath, enableAiReview: false, first);
        Assert.Equal(0, firstExit);

        var second = new CliScaffoldResult { Command = "ai-scaffold" };
        var secondExit = AiScaffoldOrchestrator.Run(_tempRoot, specPath, enableAiReview: false, second);

        Assert.Equal(0, secondExit);
        Assert.True(second.Success, string.Join("; ", second.Errors));
        Assert.Contains(second.Messages, m => m.Contains("既存プロジェクトを再利用", StringComparison.Ordinal));
    }
}
