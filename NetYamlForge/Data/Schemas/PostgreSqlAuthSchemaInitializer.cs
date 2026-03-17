// ファイル概要: PostgreSQL 用の認証テーブル初期化クラスです。
// CREATE TABLE IF NOT EXISTS 構文で AppUser / AuditLog / AppRolePermission 等のテーブルを作成します。
// DbInitializer から DatabaseProvider=postgresql 時に DI 経由で呼ばれます。

using System.Data;
using Dapper;

namespace NetYamlForge.Data.Schemas;

/// <summary>
/// PostgreSQLデータベースの認証スキーマ初期化。
/// AppUser、AuditLog、AppRolePermission等のテーブルをCREATEします。
/// </summary>
public class PostgreSqlAuthSchemaInitializer : IAuthSchemaInitializer
{
    public async Task InitializeAsync(IDbConnection conn, ILogger logger)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS AppUser (
    Id SERIAL PRIMARY KEY,
    UserName TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    DisplayName TEXT NOT NULL,
    PreferredLanguage TEXT NOT NULL DEFAULT 'en-US',
    IsAdmin BOOLEAN NOT NULL DEFAULT FALSE,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TEXT NOT NULL,
    LastLoginAt TEXT
);

CREATE TABLE IF NOT EXISTS AuditLog (
    Id SERIAL PRIMARY KEY,
    UserName TEXT,
    Action TEXT NOT NULL,
    Entity TEXT,
    Detail TEXT,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS AppUserSavedView (
    Id SERIAL PRIMARY KEY,
    ProjectName TEXT NOT NULL,
    PageName TEXT NOT NULL,
    UserName TEXT NOT NULL,
    ViewName TEXT NOT NULL,
    FiltersJson TEXT NOT NULL,
    IsDefault BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(ProjectName, PageName, UserName, ViewName)
);

CREATE TABLE IF NOT EXISTS AppUserRole (
    Id SERIAL PRIMARY KEY,
    UserName TEXT NOT NULL,
    RoleName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UNIQUE(UserName, RoleName)
);

CREATE TABLE IF NOT EXISTS AppRolePermission (
    Id SERIAL PRIMARY KEY,
    ProjectName TEXT NOT NULL,
    RoleName TEXT NOT NULL,
    ResourceType TEXT NOT NULL,
    ResourceName TEXT NOT NULL,
    CanRead BOOLEAN NOT NULL DEFAULT TRUE,
    CanWrite BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(ProjectName, RoleName, ResourceType, ResourceName)
);";
        await conn.ExecuteAsync(sql);
        await conn.ExecuteAsync("ALTER TABLE AppUser ADD COLUMN IF NOT EXISTS LastLoginAt TEXT");
        logger.LogInformation("認証テーブル確認済み (PostgreSQL)");
    }
}
