// ファイル概要：プロジェクト固有の DB マイグレーション（列追加等）を実行する初期化クラスです。
// DbInitializer から呼ばれ、既存テーブルへの ALTER TABLE ADD COLUMN を安全に行います。
// 例：attendance-ops プロジェクトの ApprovedAt / ApprovedBy 列の追加。

using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

namespace NetYamlForge.Data;

/// <summary>
/// プロジェクト固有の DB 初期化（カラム追加等）。
/// 例：attendance-ops プロジェクトに ApprovedAt カラムを追加。
/// </summary>
public class ProjectSpecificInitializer
{
    /// <summary>
    /// プロジェクト固有の列追加を実行します。
    /// </summary>
    public async Task EnsureProjectSpecificColumnsAsync(
        IDbConnection conn,
        string projectName,
        ILogger logger)
    {
        // contact-manager: 初期データロード
        if (string.Equals(projectName, "contact-manager", StringComparison.OrdinalIgnoreCase))
        {
            await InitializeContactManagerAsync(conn as SqliteConnection, logger);
            return;
        }

        // attendance-ops: 承認カラム追加
        if (!string.Equals(projectName, "attendance-ops", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await EnsureColumnAsync(conn as SqliteConnection, "LeaveRequest", "ApprovedAt", "TEXT", logger);
        await EnsureColumnAsync(conn as SqliteConnection, "OvertimeRequest", "ApprovedAt", "TEXT", logger);
    }

    /// <summary>
    /// contact-manager プロジェクトの初期化
    /// </summary>
    private static async Task InitializeContactManagerAsync(SqliteConnection? conn, ILogger logger)
    {
        if (conn == null) return;

        var initSqlPath = Path.Combine(AppContext.BaseDirectory, "projects", "contact-manager", "database", "init.sql");
        
        if (!File.Exists(initSqlPath))
        {
            // 開発環境用のパス
            initSqlPath = Path.Combine(Directory.GetCurrentDirectory(), "NetYamlForge", "projects", "contact-manager", "database", "init.sql");
        }

        if (!File.Exists(initSqlPath))
        {
            logger.LogWarning("contact-manager の初期化スクリプトが見つかりません：{Path}", initSqlPath);
            return;
        }

        // テーブル存在チェック
        var tables = await conn.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type='table'");
        var tableList = tables.ToList();

        if (tableList.Contains("contact") && tableList.Contains("company") && tableList.Contains("interaction"))
        {
            logger.LogInformation("contact-manager のテーブルは既に存在します。初期化をスキップします。");
            return;
        }

        logger.LogInformation("contact-manager の初期化スクリプトを実行します...");
        var sql = await File.ReadAllTextAsync(initSqlPath);
        await conn.ExecuteAsync(sql);
        logger.LogInformation("contact-manager の初期化が完了しました。");
    }

    /// <summary>
    /// SQLite テーブルに列が存在しない場合、追加します。
    /// </summary>
    private static async Task EnsureColumnAsync(
        SqliteConnection? conn,
        string tableName,
        string columnName,
        string columnType,
        ILogger logger)
    {
        if (conn == null) return;

        // DCS001 抑制理由：tableName/columnName/columnType はすべて呼び出し元ハードコード値（ユーザー入力なし）
#pragma warning disable DCS001
        var columns = await conn.QueryAsync<string>($"SELECT name FROM pragma_table_info('{tableName}')");
        if (columns.Any(c => string.Equals(c, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await conn.ExecuteAsync($"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnType}");
#pragma warning restore DCS001
        logger.LogInformation("列を追加しました：{Table}.{Column}", tableName, columnName);
    }
}
