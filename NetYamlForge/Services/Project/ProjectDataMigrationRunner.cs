// ファイル概要: projects/<name>/database/migrations/ 配下の番号付き SQL を起動時に順次適用します。
// 適用記録は各プロジェクト DB の _nyf_data_migrations に保存されます。

using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services;

/// <summary>
/// データマイグレーションの適用結果サマリ。
/// </summary>
public sealed record DataMigrationSummary(
    int AppliedCount,
    int SkippedCount,
    IReadOnlyList<DataMigrationRecord> Records);

/// <summary>
/// 個別マイグレーションの適用記録。
/// </summary>
public sealed record DataMigrationRecord(
    long Version,
    string Name,
    string Checksum,
    bool Applied,
    string? AppliedAt,
    string? RolledBackAt);

/// <summary>
/// projects/&lt;name&gt;/database/migrations/ 配下の番号付き SQL を起動時に順次適用します。
/// 適用記録は各プロジェクト DB の _nyf_data_migrations に保存されます。
/// </summary>
public sealed class ProjectDataMigrationRunner
{
    private readonly ILogger<ProjectDataMigrationRunner> _logger;

    private static readonly System.Text.RegularExpressions.Regex FileNameRegex =
        new(@"^(\d{3,})_[A-Za-z0-9_\-]+\.sql$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public ProjectDataMigrationRunner(ILogger<ProjectDataMigrationRunner> logger)
    {
        _logger = logger;
    }

    private static IDbConnection CreateConnection(string connectionString)
    {
        var type = typeof(SqliteConnection);
        return (IDbConnection)Activator.CreateInstance(type, connectionString)!;
    }

    public async Task<DataMigrationSummary> ApplyPendingAsync(
        string projectName, string projectDir, string connectionString, CancellationToken ct)
    {
        var migrationsDir = Path.Combine(projectDir, "database", "migrations");
        if (!Directory.Exists(migrationsDir))
        {
            _logger.LogDebug("マイグレーションディレクトリが存在しません: {Dir}", migrationsDir);
            return new DataMigrationSummary(0, 0, Array.Empty<DataMigrationRecord>());
        }

        var files = Directory.GetFiles(migrationsDir, "*.sql")
            .Select(f => new { Path = f, Match = FileNameRegex.Match(Path.GetFileName(f)) })
            .Where(x => x.Match.Success)
            .OrderBy(x => long.Parse(x.Match.Groups[1].Value))
            .ToList();

        if (files.Count == 0)
        {
            return new DataMigrationSummary(0, 0, Array.Empty<DataMigrationRecord>());
        }

        using var conn = CreateConnection(connectionString);
        conn.Open();
        await EnsureTableAsync(conn);

        var applied = await GetAppliedVersionsAsync(conn);
        var records = new List<DataMigrationRecord>();
        int appliedCount = 0, skippedCount = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var version = long.Parse(file.Match.Groups[1].Value);
            var name = file.Match.Groups[0].Value;
            var content = await File.ReadAllTextAsync(file.Path, ct);
            var checksum = ComputeChecksum(content);
            var (upSql, downSql) = ParseUpDown(content);

            if (applied.TryGetValue(version, out var existing))
            {
                if (existing.Checksum != checksum)
                {
                    _logger.LogWarning(
                        "[{Project}] マイグレーション {Version}_{Name} のチェックサムが変更されています（適用済み: {Old}、現在: {New}）。スキップします。",
                        projectName, version, name.Replace($"{version}_", "").Replace(".sql", ""),
                        existing.Checksum[..12], checksum[..12]);
                }
                records.Add(new DataMigrationRecord(
                    version, name, checksum, true, existing.AppliedAt, existing.RolledBackAt));
                skippedCount++;
                continue;
            }

            try
            {
                using var tx = conn.BeginTransaction();
                try
                {
                    foreach (var stmt in upSql.Split(";", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (string.IsNullOrWhiteSpace(stmt)) continue;
                        await conn.ExecuteAsync(stmt, transaction: tx);
                    }

                    await conn.ExecuteAsync(@"
                        INSERT INTO _nyf_data_migrations (version, name, checksum, up_sql, down_sql, applied_at)
                        VALUES (@Version, @Name, @Checksum, @UpSql, @DownSql, @AppliedAt)",
                        new { Version = version, Name = name, Checksum = checksum, UpSql = upSql, DownSql = downSql, AppliedAt = DateTime.UtcNow.ToString("o") },
                        tx);

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }

                appliedCount++;
                records.Add(new DataMigrationRecord(version, name, checksum, true, DateTime.UtcNow.ToString("o"), null));
                _logger.LogInformation(
                    "[{Project}] マイグレーション適用完了: {Version}_{Name}",
                    projectName, version, name.Replace($"{version}_", "").Replace(".sql", ""));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "[{Project}] マイグレーション {Version}_{Name} の適用に失敗しました。以降のマイグレーションはスキップします。",
                    projectName, version, name.Replace($"{version}_", "").Replace(".sql", ""));
                records.Add(new DataMigrationRecord(version, name, checksum, false, null, null));
                break;
            }
        }

        return new DataMigrationSummary(appliedCount, skippedCount, records);
    }

    public async Task<IReadOnlyList<DataMigrationRecord>> GetStatusAsync(
        string projectName, string projectDir, string connectionString, CancellationToken ct)
    {
        var migrationsDir = Path.Combine(projectDir, "database", "migrations");
        var fileList = !Directory.Exists(migrationsDir)
            ? Enumerable.Empty<(string FilePath, System.Text.RegularExpressions.Match Match)>()
            : Directory.GetFiles(migrationsDir, "*.sql")
                .Select(f => (FilePath: f, Match: FileNameRegex.Match(Path.GetFileName(f))))
                .Where(x => x.Match.Success)
                .OrderBy(x => long.Parse(x.Match.Groups[1].Value))
                .AsEnumerable();

        using var conn = CreateConnection(connectionString);
        conn.Open();
        await EnsureTableAsync(conn);
        var applied = await GetAppliedVersionsAsync(conn);

        var records = new List<DataMigrationRecord>();
        foreach (var file in fileList)
        {
            var version = long.Parse(file.Match.Groups[1].Value);
            var name = file.Match.Groups[0].Value;
            var content = await File.ReadAllTextAsync(file.FilePath, ct);
            var checksum = ComputeChecksum(content);

            if (applied.TryGetValue(version, out var existing))
                records.Add(new DataMigrationRecord(version, name, checksum, true, existing.AppliedAt, existing.RolledBackAt));
            else
                records.Add(new DataMigrationRecord(version, name, checksum, false, null, null));
        }

        return records;
    }

    public async Task RollbackAsync(
        string projectName, string projectDir, string connectionString, long version, CancellationToken ct)
    {
        using var conn = CreateConnection(connectionString);
        conn.Open();
        await EnsureTableAsync(conn);

        var record = await conn.QueryFirstOrDefaultAsync<(string down_sql, string name)?>(
            "SELECT down_sql, name FROM _nyf_data_migrations WHERE version = @Version AND rolled_back_at IS NULL",
            new { Version = version });

        if (record == null)
        {
            _logger.LogWarning("[{Project}] バージョン {Version} の適用済みマイグレーションが見つかりません。", projectName, version);
            return;
        }

        var (downSql, name) = record.Value;

        if (string.IsNullOrWhiteSpace(downSql))
        {
            _logger.LogWarning("[{Project}] バージョン {Version} ({Name}) には down SQL がありません。ロールバック不可。", projectName, version, name);
            return;
        }

        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var stmt in downSql.Split(";", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(stmt)) continue;
                await conn.ExecuteAsync(stmt, transaction: tx);
            }

            await conn.ExecuteAsync(
                "UPDATE _nyf_data_migrations SET rolled_back_at = @Now WHERE version = @Version",
                new { Now = DateTime.UtcNow.ToString("o"), Version = version },
                tx);

            tx.Commit();
            _logger.LogInformation("[{Project}] マイグレーション ロールバック完了: {Version}_{Name}", projectName, version, name);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static async Task EnsureTableAsync(IDbConnection conn)
    {
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS _nyf_data_migrations (
                version     INTEGER PRIMARY KEY,
                name        TEXT NOT NULL,
                checksum    TEXT NOT NULL,
                up_sql      TEXT NOT NULL,
                down_sql    TEXT,
                applied_at  TEXT NOT NULL,
                rolled_back_at TEXT
            )");
    }

    private static async Task<Dictionary<long, (string Checksum, string AppliedAt, string? RolledBackAt)>> GetAppliedVersionsAsync(
        IDbConnection conn)
    {
        var rows = await conn.QueryAsync<(long version, string checksum, string applied_at, string? rolled_back_at)>(
            "SELECT version, checksum, applied_at, rolled_back_at FROM _nyf_data_migrations ORDER BY version");
        return rows.ToDictionary(
            r => r.version,
            r => (r.checksum, r.applied_at, r.rolled_back_at));
    }

    private static string ComputeChecksum(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash);
    }

    private static (string UpSql, string? DownSql) ParseUpDown(string content)
    {
        var upIdx = content.IndexOf("-- +up", StringComparison.OrdinalIgnoreCase);
        var downIdx = content.IndexOf("-- +down", StringComparison.OrdinalIgnoreCase);

        if (upIdx < 0 && downIdx < 0)
            return (content.Trim(), null);

        var upSql = upIdx >= 0
            ? content.Substring(upIdx + 6, (downIdx > upIdx ? downIdx : content.Length) - upIdx - 6).Trim()
            : content.Substring(0, downIdx > 0 ? downIdx : content.Length).Trim();

        var downSql = downIdx >= 0
            ? content.Substring(downIdx + 8).Trim()
            : null;

        return (upSql, string.IsNullOrWhiteSpace(downSql) ? null : downSql);
    }
}
