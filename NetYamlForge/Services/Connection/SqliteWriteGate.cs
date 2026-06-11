// ファイル概要: SQLite データベースファイル単位の「単一ライター」直列化ゲートです。
// SQLite は同一ファイルに対して同時に 1 つの書き込みトランザクションしか許可しないため、
// 複数の実行器（Web CRUD / BatchJobExecutor / AiFolderProcessorExecutor 等）が同じ
// テナント DB へ並行書き込みすると "SQLite Error 5: database is locked" が発生します。
// 本クラスは DB ファイルパスをキーにした SemaphoreSlim で書き込み処理を直列化し、
// busy_timeout（SqliteConnectionHardening）を最後の保険に格下げします。
// 読み取りは WAL モードで書き込みと並行可能なため、本ゲートを通す必要はありません。
// SQLite 以外の方言（SQL Server / PostgreSQL / MySQL）の接続はそのまま素通しします。

using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.Sqlite;

namespace NetYamlForge.Services.Connection;

/// <summary>
/// SQLite 每数据库文件的单写者串行化门闩。
/// 用法：<c>await SqliteWriteGate.RunAsync(connection, async () =&gt; { ...写事务... });</c>
/// 非 SQLite 连接直接执行委托（不加锁）；同一异步流内的嵌套调用直接内联执行（防死锁）。
/// </summary>
public static class SqliteWriteGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 当前异步流已持有的门闩键集合（重入保护：嵌套写操作内联执行而不是二次等待）。
    /// </summary>
    private static readonly AsyncLocal<IReadOnlySet<string>?> _heldKeys = new();

    /// <summary>
    /// 解析连接对应的串行化键（SQLite 数据库文件的规范化绝对路径）。
    /// 返回 false 的情况：非 SQLite 连接、私有内存库（:memory:，每个连接独立无锁竞争）、连接串无法解析。
    /// </summary>
    public static bool TryGetDatabaseKey(IDbConnection connection, out string key)
    {
        key = string.Empty;

        var actual = connection is LazyDbConnection lazy ? lazy.InnerConnection : connection;
        if (actual is not SqliteConnection sqlite)
            return false;

        string dataSource;
        try
        {
            dataSource = new SqliteConnectionStringBuilder(sqlite.ConnectionString).DataSource;
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dataSource))
            return false;

        // 私有内存库：每个连接是独立数据库，不存在跨连接写锁，串行化只会白白降低测试并行度。
        if (dataSource == ":memory:")
            return false;

        // file: URI（含 shared cache 内存库）按原样作键；普通路径规范化为绝对路径，
        // 保证 "data/app.db" 与 "./data/app.db" 映射到同一个门闩。
        key = dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? dataSource
            : Path.GetFullPath(dataSource);
        return true;
    }

    /// <summary>
    /// 在目标数据库的单写者门闩内执行写操作（无返回值版本）。
    /// </summary>
    public static async Task RunAsync(IDbConnection connection, Func<Task> action, CancellationToken cancellationToken = default)
    {
        await RunAsync(connection, async () =>
        {
            await action();
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// 在目标数据库的单写者门闩内执行写操作并返回结果。
    /// 非 SQLite 连接、私有内存库、以及当前异步流已持有同一门闩时直接执行委托。
    /// </summary>
    public static async Task<T> RunAsync<T>(IDbConnection connection, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (!TryGetDatabaseKey(connection, out var key))
            return await action();

        var held = _heldKeys.Value;
        if (held != null && held.Contains(key))
        {
            // 重入：外层已对该数据库持锁（例如 BatchJobExecutor 事务内的步骤处理器再次包裹写操作）。
            return await action();
        }

        var gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        var inner = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };
        if (held != null)
            inner.UnionWith(held);
        _heldKeys.Value = inner;

        try
        {
            return await action();
        }
        finally
        {
            _heldKeys.Value = held;
            gate.Release();
        }
    }
}
