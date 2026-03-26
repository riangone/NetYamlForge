// ファイル概要: SQLite 用の認証テーブル初期化クラスです。
// AppUser / AuditLog / AppUserSavedView / AppUserRole / AppRolePermission を
// CREATE TABLE IF NOT EXISTS で安全に作成します。
// EnsureColumnAsync でマイグレーション相当の列追加も行います。


using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace NetYamlForge.Data.Schemas;

/// <summary>
/// SQLiteデータベースの認証スキーマ初期化。
/// AppUser、AuditLog、AppRolePermission等の共通テーブルをCREATEします。
/// CRM 専用テーブルはここには含みません。
/// </summary>
public class SqliteAuthSchemaInitializer : IAuthSchemaInitializer
{
    public async Task InitializeAsync(IDbConnection conn, ILogger logger)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS AppUser (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    PreferredLanguage TEXT NOT NULL DEFAULT 'en-US',
    IsAdmin INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    LastLoginAt TEXT
);

CREATE TABLE IF NOT EXISTS AuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT,
    Action TEXT NOT NULL,
    Entity TEXT,
    Detail TEXT,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS AppUserSavedView (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectName TEXT NOT NULL,
    PageName TEXT NOT NULL,
    UserName TEXT NOT NULL,
    ViewName TEXT NOT NULL,
    FiltersJson TEXT NOT NULL,
    IsDefault INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(ProjectName, PageName, UserName, ViewName)
);

CREATE TABLE IF NOT EXISTS AppUserRole (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL,
    RoleName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UNIQUE(UserName, RoleName)
);

CREATE TABLE IF NOT EXISTS AppRolePermission (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectName TEXT NOT NULL,
    RoleName TEXT NOT NULL,
    ResourceType TEXT NOT NULL,
    ResourceName TEXT NOT NULL,
    CanRead INTEGER NOT NULL DEFAULT 1,
    CanWrite INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(ProjectName, RoleName, ResourceType, ResourceName)
);";

        await conn.ExecuteAsync(sql);
        await EnsureColumnAsync(conn as SqliteConnection, "AppUser", "LastLoginAt", "TEXT", logger);
        logger.LogInformation("認証テーブル確認済み (SQLite)");
    }

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
