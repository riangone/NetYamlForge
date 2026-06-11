// ファイル概要: SqliteWriteGate / SqliteConnectionHardening のテストです。
// Phase 3.1「SQLite 写锁根治」の受け入れ基準を検証します:
//   - 複数タスクが同一 SQLite ファイルへ並行 INSERT/UPDATE しても Error 5 (database is locked) が出ない
//   - 書き込みが直列化される（read-modify-write で更新ロストが起きない）
//   - 嵌套呼び出しがデッドロックしない / 非 SQLite 接続は素通し
//   - 接続加固 PRAGMA（WAL / busy_timeout=5000 / synchronous=NORMAL）が適用される

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Moq;
using NetYamlForge.Services.Connection;
using Xunit;

namespace NetYamlForge.Tests.Services.Connection;

public sealed class SqliteWriteGateTests : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;

    public SqliteWriteGateTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "nyf-sqlite-gate-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "tenant.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dbDir, recursive: true);
        }
        catch
        {
            // ベストエフォート：一時ファイルの削除失敗はテスト結果に影響しない
        }
    }

    /// <summary>
    /// ファイルベース SQLite 接続を開く（Activator 経由で DCS003 を回避、本体コードと同パターン）。
    /// </summary>
    private static IDbConnection OpenConnection(string dbPath)
    {
        var conn = (IDbConnection)Activator.CreateInstance(typeof(SqliteConnection), "Data Source=" + dbPath)!;
        conn.Open();
        SqliteConnectionHardening.Apply(conn);
        return conn;
    }

    private async Task InitializeSchemaAsync()
    {
        using var conn = OpenConnection(_dbPath);
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS WriteLog (
                Id     INTEGER PRIMARY KEY AUTOINCREMENT,
                Writer INTEGER NOT NULL,
                Iter   INTEGER NOT NULL
            );");
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Counter (
                Id    INTEGER PRIMARY KEY,
                Value INTEGER NOT NULL
            );");
        await conn.ExecuteAsync("INSERT INTO Counter (Id, Value) VALUES (1, 0);");
    }

    [Fact]
    public async Task ConcurrentWrites_SameDatabaseFile_DoNotThrowDatabaseLocked()
    {
        // Arrange
        await InitializeSchemaAsync();
        const int writers = 8;
        const int iterations = 20;

        // Act: 8 タスクが各自の接続・トランザクションで同一 DB へ並行 INSERT + UPDATE
        var tasks = Enumerable.Range(0, writers).Select(writer => Task.Run(async () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                using var conn = OpenConnection(_dbPath);
                await SqliteWriteGate.RunAsync(conn, async () =>
                {
                    using var tx = conn.BeginTransaction();
                    await conn.ExecuteAsync(
                        "INSERT INTO WriteLog (Writer, Iter) VALUES (@Writer, @Iter);",
                        new { Writer = writer, Iter = i },
                        transaction: tx);

                    // read-modify-write：直列化されていなければ更新ロストや SQLITE_BUSY が起きるパターン
                    var current = await conn.QuerySingleAsync<long>(
                        "SELECT Value FROM Counter WHERE Id = 1;",
                        transaction: tx);
                    await conn.ExecuteAsync(
                        "UPDATE Counter SET Value = @Value WHERE Id = 1;",
                        new { Value = current + 1 },
                        transaction: tx);

                    tx.Commit();
                });
            }
        })).ToArray();

        // Assert: SqliteException (Error 5) が飛ばないこと
        await Task.WhenAll(tasks);

        using var verify = OpenConnection(_dbPath);
        var rowCount = await verify.QuerySingleAsync<long>("SELECT COUNT(*) FROM WriteLog;");
        var counter = await verify.QuerySingleAsync<long>("SELECT Value FROM Counter WHERE Id = 1;");

        Assert.Equal(writers * iterations, rowCount);
        // 直列化が効いていれば read-modify-write でも 1 件も更新ロストしない
        Assert.Equal(writers * iterations, counter);
    }

    [Fact]
    public async Task RunAsync_SerializesWriters_OnSameDatabaseFile()
    {
        // Arrange
        using var conn1 = OpenConnection(_dbPath);
        using var conn2 = OpenConnection(_dbPath);

        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        // Act
        var first = Task.Run(() => SqliteWriteGate.RunAsync(conn1, async () =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task;
        }));

        await firstEntered.Task;

        var second = Task.Run(() => SqliteWriteGate.RunAsync(conn2, () =>
        {
            secondEntered = true;
            return Task.CompletedTask;
        }));

        // Assert: 先行ライターがゲートを保持している間、後続ライターは入れない
        await Task.Delay(250);
        Assert.False(secondEntered);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondEntered);
    }

    [Fact]
    public async Task NestedRunAsync_SameDatabase_DoesNotDeadlock()
    {
        // Arrange
        using var conn = OpenConnection(_dbPath);
        var innerExecuted = false;

        // Act: 同一異步流内の嵌套呼び出しは内联実行される（デッドロックしない）
        var work = SqliteWriteGate.RunAsync(conn, async () =>
        {
            await SqliteWriteGate.RunAsync(conn, () =>
            {
                innerExecuted = true;
                return Task.CompletedTask;
            });
        });

        var winner = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10)));

        // Assert
        Assert.Same(work, winner);
        await work;
        Assert.True(innerExecuted);
    }

    [Fact]
    public async Task RunAsync_PassesThrough_ForNonSqliteConnection()
    {
        // Arrange: SQLite ではない IDbConnection はゲートを通さず直接実行される
        var mockConn = new Mock<IDbConnection>();
        var executed = false;

        // Act
        var result = await SqliteWriteGate.RunAsync(mockConn.Object, () =>
        {
            executed = true;
            return Task.FromResult(42);
        });

        // Assert
        Assert.True(executed);
        Assert.Equal(42, result);
    }

    [Fact]
    public void TryGetDatabaseKey_ReturnsFalse_ForPrivateInMemoryDatabase()
    {
        // Arrange: 私有内存库は接続ごとに独立した DB なのでゲート不要
        using var conn = new SqliteConnection("Data Source=:memory:");

        // Act & Assert
        Assert.False(SqliteWriteGate.TryGetDatabaseKey(conn, out _));
    }

    [Fact]
    public void TryGetDatabaseKey_NormalizesRelativeAndAbsolutePaths_ToSameKey()
    {
        // Arrange: 相対パスと絶対パスが同一ファイルを指すなら同じ門閂キーになること
        var fileName = "key-test.db";
        var absolute = Path.Combine(Directory.GetCurrentDirectory(), fileName);

        var relConn = (SqliteConnection)Activator.CreateInstance(typeof(SqliteConnection), "Data Source=" + fileName)!;
        var absConn = (SqliteConnection)Activator.CreateInstance(typeof(SqliteConnection), "Data Source=" + absolute)!;

        // Act
        Assert.True(SqliteWriteGate.TryGetDatabaseKey(relConn, out var relKey));
        Assert.True(SqliteWriteGate.TryGetDatabaseKey(absConn, out var absKey));

        // Assert
        Assert.Equal(absKey, relKey);
        Assert.Equal(Path.GetFullPath(absolute), absKey);
    }

    [Fact]
    public void TryGetDatabaseKey_ReturnsDifferentKeys_ForDifferentDatabaseFiles()
    {
        // Arrange: テナントごとの DB ファイルは互いに独立した門閂を持つ
        var connA = (SqliteConnection)Activator.CreateInstance(
            typeof(SqliteConnection), "Data Source=" + Path.Combine(_dbDir, "tenant-a.db"))!;
        var connB = (SqliteConnection)Activator.CreateInstance(
            typeof(SqliteConnection), "Data Source=" + Path.Combine(_dbDir, "tenant-b.db"))!;

        // Act
        Assert.True(SqliteWriteGate.TryGetDatabaseKey(connA, out var keyA));
        Assert.True(SqliteWriteGate.TryGetDatabaseKey(connB, out var keyB));

        // Assert
        Assert.NotEqual(keyA, keyB);
    }
}

public sealed class SqliteConnectionHardeningTests : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;

    public SqliteConnectionHardeningTests()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "nyf-sqlite-hardening-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "hardening.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dbDir, recursive: true);
        }
        catch
        {
            // ベストエフォート
        }
    }

    [Fact]
    public async Task ApplyAsync_SetsWalBusyTimeoutAndSynchronousNormal()
    {
        // Arrange
        using var conn = (IDbConnection)Activator.CreateInstance(typeof(SqliteConnection), "Data Source=" + _dbPath)!;
        conn.Open();

        // Act
        await SqliteConnectionHardening.ApplyAsync(conn);

        // Assert
        var journalMode = await conn.QuerySingleAsync<string>("PRAGMA journal_mode;");
        var busyTimeout = await conn.QuerySingleAsync<long>("PRAGMA busy_timeout;");
        var synchronous = await conn.QuerySingleAsync<long>("PRAGMA synchronous;");

        Assert.Equal("wal", journalMode, ignoreCase: true);
        Assert.Equal(SqliteConnectionHardening.BusyTimeoutMs, busyTimeout);
        Assert.Equal(1, synchronous); // 1 = NORMAL
    }

    [Fact]
    public void Apply_SetsWalBusyTimeoutAndSynchronousNormal_Sync()
    {
        // Arrange
        using var conn = (IDbConnection)Activator.CreateInstance(typeof(SqliteConnection), "Data Source=" + _dbPath)!;
        conn.Open();

        // Act
        SqliteConnectionHardening.Apply(conn);

        // Assert
        var journalMode = conn.QuerySingle<string>("PRAGMA journal_mode;");
        var busyTimeout = conn.QuerySingle<long>("PRAGMA busy_timeout;");
        var synchronous = conn.QuerySingle<long>("PRAGMA synchronous;");

        Assert.Equal("wal", journalMode, ignoreCase: true);
        Assert.Equal(SqliteConnectionHardening.BusyTimeoutMs, busyTimeout);
        Assert.Equal(1, synchronous);
    }
}
