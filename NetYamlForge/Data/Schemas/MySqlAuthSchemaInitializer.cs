// ファイル概要: MySQL/MariaDB 用の認証テーブル初期化クラスです。
// CREATE TABLE IF NOT EXISTS 構文で AppUser / AuditLog / AppRolePermission 等のテーブルを作成します。
// DbInitializer から DatabaseProvider=mysql 時に DI 経由で呼ばれます。

using System.Data;
using Dapper;

namespace NetYamlForge.Data.Schemas;

/// <summary>
/// MySQL/MariaDBデータベースの認証スキーマ初期化。
/// AppUser、AuditLog、AppRolePermission等のテーブルをCREATEします。
/// </summary>
public class MySqlAuthSchemaInitializer : IAuthSchemaInitializer
{
    public async Task InitializeAsync(IDbConnection conn, ILogger logger)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS AppUser (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    UserName VARCHAR(256) NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    DisplayName VARCHAR(256) NOT NULL,
    PreferredLanguage VARCHAR(16) NOT NULL DEFAULT 'en-US',
    IsAdmin TINYINT(1) NOT NULL DEFAULT 0,
    IsActive TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAt VARCHAR(32) NOT NULL,
    LastLoginAt VARCHAR(32) NULL
);

CREATE TABLE IF NOT EXISTS AuditLog (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    UserName VARCHAR(256) NULL,
    Action VARCHAR(100) NOT NULL,
    Entity VARCHAR(100) NULL,
    Detail TEXT NULL,
    CreatedAt VARCHAR(32) NOT NULL
);

CREATE TABLE IF NOT EXISTS AppUserSavedView (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    ProjectName VARCHAR(128) NOT NULL,
    PageName VARCHAR(128) NOT NULL,
    UserName VARCHAR(256) NOT NULL,
    ViewName VARCHAR(128) NOT NULL,
    FiltersJson TEXT NOT NULL,
    IsDefault TINYINT(1) NOT NULL DEFAULT 0,
    CreatedAt VARCHAR(32) NOT NULL,
    UpdatedAt VARCHAR(32) NOT NULL,
    UNIQUE KEY UQ_AppUserSavedView (ProjectName, PageName, UserName, ViewName)
);

CREATE TABLE IF NOT EXISTS AppUserRole (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    UserName VARCHAR(256) NOT NULL,
    RoleName VARCHAR(128) NOT NULL,
    CreatedAt VARCHAR(32) NOT NULL,
    UNIQUE KEY UQ_AppUserRole (UserName, RoleName)
);

CREATE TABLE IF NOT EXISTS AppRolePermission (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    ProjectName VARCHAR(128) NOT NULL,
    RoleName VARCHAR(128) NOT NULL,
    ResourceType VARCHAR(64) NOT NULL,
    ResourceName VARCHAR(256) NOT NULL,
    CanRead TINYINT(1) NOT NULL DEFAULT 1,
    CanWrite TINYINT(1) NOT NULL DEFAULT 0,
    CreatedAt VARCHAR(32) NOT NULL,
    UpdatedAt VARCHAR(32) NOT NULL,
    UNIQUE KEY UQ_AppRolePermission (ProjectName, RoleName, ResourceType, ResourceName)
);";
        await conn.ExecuteAsync(sql);
        await conn.ExecuteAsync("ALTER TABLE AppUser ADD COLUMN IF NOT EXISTS LastLoginAt VARCHAR(32) NULL");
        logger.LogInformation("認証テーブル確認済み (MySQL/MariaDB)");
    }
}
