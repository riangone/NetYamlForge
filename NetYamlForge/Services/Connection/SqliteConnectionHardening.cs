// ファイル概要: SQLite 接続の堅牢化（WAL + busy_timeout + synchronous=NORMAL）を一元化するヘルパーです。
// すべての SQLite 接続生成パス（ConnectionManager / system.db 直接接続 / 起動時初期化）から
// 本クラスを呼び出すことで、"SQLite Error 5: database is locked" の発生を抑止します。
// 設定値を変更する場合は必ずここだけを変更してください（個別の実行器に PRAGMA を書かないこと）。

using System.Data;
using Dapper;

namespace NetYamlForge.Services.Connection;

/// <summary>
/// SQLite 连接加固的唯一入口：统一启用 WAL 日志模式、busy_timeout 与 synchronous=NORMAL。
/// WAL 允许读写并发；busy_timeout 让写锁竞争时等待而不是立刻抛出 SQLITE_BUSY；
/// synchronous=NORMAL 在 WAL 模式下保证一致性的同时减少 fsync 次数。
/// 仅对 SQLite 方言调用本类，其他数据库方言不受影响。
/// </summary>
public static class SqliteConnectionHardening
{
    /// <summary>
    /// 写锁等待上限（毫秒）。与 docs/EVOLUTION_PLAN.md Phase 3.1 规定值保持一致。
    /// 修改时同步更新下方的 PRAGMA 字面量（DCS001 对策で定数文字列を使用）。
    /// </summary>
    public const int BusyTimeoutMs = 5000;

    private const string JournalModePragma = "PRAGMA journal_mode=WAL;";
    private const string BusyTimeoutPragma = "PRAGMA busy_timeout=5000;";
    private const string SynchronousPragma = "PRAGMA synchronous=NORMAL;";

    /// <summary>
    /// 对已打开的 SQLite 连接应用 WAL + busy_timeout + synchronous=NORMAL（异步）。
    /// 注意：PRAGMA 需逐条执行，多语句批量执行时第一条之后可能被静默忽略。
    /// </summary>
    public static async Task ApplyAsync(IDbConnection connection)
    {
        await connection.ExecuteAsync(JournalModePragma);
        await connection.ExecuteAsync(BusyTimeoutPragma);
        await connection.ExecuteAsync(SynchronousPragma);
    }

    /// <summary>
    /// 对已打开的 SQLite 连接应用 WAL + busy_timeout + synchronous=NORMAL（同步）。
    /// </summary>
    public static void Apply(IDbConnection connection)
    {
        connection.Execute(JournalModePragma);
        connection.Execute(BusyTimeoutPragma);
        connection.Execute(SynchronousPragma);
    }
}
