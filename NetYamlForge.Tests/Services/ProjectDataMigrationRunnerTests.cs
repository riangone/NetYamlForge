#pragma warning disable DCS003
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Services;
using Xunit;
using Dapper;

namespace NetYamlForge.Tests.Services;

public class ProjectDataMigrationRunnerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly ProjectDataMigrationRunner _runner;

    public ProjectDataMigrationRunnerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "nyf-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _dbPath = Path.Combine(_tempRoot, "test.db");
        _connectionString = $"Data Source={_dbPath}";

        _runner = new ProjectDataMigrationRunner(NullLogger<ProjectDataMigrationRunner>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch { }
    }

    private void CreateMigrationFile(string fileName, string content)
    {
        var dir = Path.Combine(_tempRoot, "database", "migrations");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    [Fact]
    public async Task ApplyPendingAsync_ShouldApplyInOrderAndRecordCorrectly()
    {
        CreateMigrationFile("001_create_users.sql", @"
-- +up
CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT);
INSERT INTO users (name) VALUES ('Alice');
-- +down
DROP TABLE users;
");
        CreateMigrationFile("002_add_email.sql", @"
-- +up
ALTER TABLE users ADD COLUMN email TEXT;
INSERT INTO users (name, email) VALUES ('Bob', 'bob@example.com');
-- +down
ALTER TABLE users DROP COLUMN email;
");

        var summary = await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);

        Assert.Equal(2, summary.AppliedCount);
        Assert.Equal(0, summary.SkippedCount);
        Assert.Equal(2, summary.Records.Count);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users");
        Assert.Equal(2, count);

        var aliceEmailExists = await conn.QueryFirstOrDefaultAsync<string>("SELECT email FROM users WHERE name = 'Alice'");
        Assert.Null(aliceEmailExists);

        var bobEmail = await conn.QueryFirstOrDefaultAsync<string>("SELECT email FROM users WHERE name = 'Bob'");
        Assert.Equal("bob@example.com", bobEmail);

        var migrationRecords = await conn.QueryAsync<(long version, string name, string applied_at)>("SELECT version, name, applied_at FROM _nyf_data_migrations ORDER BY version");
        var list = migrationRecords.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0].version);
        Assert.Equal("001_create_users.sql", list[0].name);
        Assert.NotNull(list[0].applied_at);
        Assert.Equal(2, list[1].version);
        Assert.Equal("002_add_email.sql", list[1].name);
        Assert.NotNull(list[1].applied_at);
    }

    [Fact]
    public async Task ApplyPendingAsync_Idempotency_ShouldNotReapply()
    {
        CreateMigrationFile("001_create_counter.sql", @"
CREATE TABLE counter (val INTEGER);
INSERT INTO counter (val) VALUES (1);
");
        var summary1 = await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);
        Assert.Equal(1, summary1.AppliedCount);

        var summary2 = await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);
        Assert.Equal(0, summary2.AppliedCount);
        Assert.Equal(1, summary2.SkippedCount);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var sum = await conn.ExecuteScalarAsync<int>("SELECT SUM(val) FROM counter");
        Assert.Equal(1, sum);
    }

    [Fact]
    public async Task ApplyPendingAsync_StopOnError_ShouldStopSubsequentMigrationsAndNotThrow()
    {
        CreateMigrationFile("001_create_t1.sql", "CREATE TABLE t1 (id INT);");
        CreateMigrationFile("002_bad_sql.sql", "CREATE TABLE t2 (id INT; -- Syntax error");
        CreateMigrationFile("003_create_t3.sql", "CREATE TABLE t3 (id INT);");

        var summary = await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);

        Assert.Equal(1, summary.AppliedCount);
        Assert.Equal(2, summary.Records.Count);
        Assert.True(summary.Records[0].Applied);
        Assert.False(summary.Records[1].Applied);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var t1Exists = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='t1'");
        Assert.Equal(1, t1Exists);

        var t3Exists = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='t3'");
        Assert.Equal(0, t3Exists);
    }

    [Fact]
    public async Task ApplyPendingAsync_NoTags_ShouldBeEntirelyUp()
    {
        CreateMigrationFile("001_plain.sql", "CREATE TABLE t_plain (id INT);");

        var summary = await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);
        Assert.Equal(1, summary.AppliedCount);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var tableExists = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='t_plain'");
        Assert.Equal(1, tableExists);
    }

    [Fact]
    public async Task RollbackAsync_WithDownSql_ShouldRollbackAndMark()
    {
        CreateMigrationFile("001_rollbackable.sql", @"
-- +up
CREATE TABLE t_roll (id INT);
-- +down
DROP TABLE t_roll;
");

        await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);
        await _runner.RollbackAsync("test_proj", _tempRoot, _connectionString, 1, CancellationToken.None);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var tableExists = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='t_roll'");
        Assert.Equal(0, tableExists);

        var rolledBackAt = await conn.ExecuteScalarAsync<string>("SELECT rolled_back_at FROM _nyf_data_migrations WHERE version = 1");
        Assert.NotNull(rolledBackAt);
    }

    [Fact]
    public async Task RollbackAsync_NoDownSql_ShouldNotPerformRollback()
    {
        CreateMigrationFile("001_nodown.sql", @"
-- +up
CREATE TABLE t_nodown (id INT);
");

        await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);
        await _runner.RollbackAsync("test_proj", _tempRoot, _connectionString, 1, CancellationToken.None);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var tableExists = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='t_nodown'");
        Assert.Equal(1, tableExists);

        var rolledBackAt = await conn.ExecuteScalarAsync<string>("SELECT rolled_back_at FROM _nyf_data_migrations WHERE version = 1");
        Assert.Null(rolledBackAt);
    }

    [Fact]
    public async Task ApplyPendingAsync_ChecksumMismatch_ShouldSkipAndLogWarning()
    {
        CreateMigrationFile("001_checksum.sql", "CREATE TABLE t_checksum (id INT);");
        await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);

        CreateMigrationFile("001_checksum.sql", "CREATE TABLE t_checksum (id INT, extra INT);");

        var summary = await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);

        Assert.Equal(0, summary.AppliedCount);
        Assert.Equal(1, summary.SkippedCount);
    }

    [Fact]
    public async Task ApplyPendingAsync_InvalidFileName_ShouldBeSkipped()
    {
        CreateMigrationFile("01_too_few_digits.sql", "CREATE TABLE t_invalid1 (id INT);");
        CreateMigrationFile("abc.sql", "CREATE TABLE t_invalid2 (id INT);");

        var summary = await _runner.ApplyPendingAsync("test_proj", _tempRoot, _connectionString, CancellationToken.None);

        Assert.Equal(0, summary.AppliedCount);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var t1 = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='t_invalid1'");
        var t2 = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='t_invalid2'");
        Assert.Equal(0, t1 + t2);
    }
}
