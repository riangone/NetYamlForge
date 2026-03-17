// ファイル概要: プロジェクト固有の DB マイグレーション（列追加等）を実行する初期化クラスです。
// DbInitializer から呼ばれ、既存テーブルへの ALTER TABLE ADD COLUMN を安全に行います。
// 例: attendance-ops プロジェクトの ApprovedAt / ApprovedBy 列の追加。

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace NetYamlForge.Data;

/// <summary>
/// プロジェクト固有の DB 初期化（カラム追加等）。
/// 例: attendance-ops プロジェクトに ApprovedAt カラムを追加。
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
        if (!string.Equals(projectName, "attendance-ops", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await EnsureColumnAsync(conn as SqliteConnection, "LeaveRequest", "ApprovedAt", "TEXT", logger);
        await EnsureColumnAsync(conn as SqliteConnection, "OvertimeRequest", "ApprovedAt", "TEXT", logger);
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

        // DCS001 抑制理由: tableName/columnName/columnType はすべて呼び出し元ハードコード値（ユーザー入力なし）
#pragma warning disable DCS001
        var columns = await conn.QueryAsync<string>($"SELECT name FROM pragma_table_info('{tableName}')");
        if (columns.Any(c => string.Equals(c, columnName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await conn.ExecuteAsync($"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnType}");
#pragma warning restore DCS001
        logger.LogInformation("列を追加しました: {Table}.{Column}", tableName, columnName);
    }
}
