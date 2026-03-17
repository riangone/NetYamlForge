// ファイル概要: SQL Server 用の認証テーブル初期化クラスです。
// IF NOT EXISTS 相当の T-SQL で AppUser / AuditLog / AppRolePermission 等のテーブルを作成します。
// DbInitializer から DatabaseProvider=sqlserver 時に DI 経由で呼ばれます。

using System.Data;
using Dapper;

namespace NetYamlForge.Data.Schemas;

/// <summary>
/// SQL Serverデータベースの認証スキーマ初期化。
/// AppUser、AuditLog、AppRolePermission等のテーブルをCREATEします。
/// </summary>
public class SqlServerAuthSchemaInitializer : IAuthSchemaInitializer
{
    public async Task InitializeAsync(IDbConnection conn, ILogger logger)
    {
        var sql = @"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AppUser' AND xtype='U')
CREATE TABLE AppUser (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    UserName   NVARCHAR(256) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(512) NOT NULL,
    DisplayName  NVARCHAR(256) NOT NULL,
    PreferredLanguage NVARCHAR(10) NOT NULL DEFAULT 'en-US',
    IsAdmin    BIT NOT NULL DEFAULT 0,
    IsActive   BIT NOT NULL DEFAULT 1,
    CreatedAt  NVARCHAR(32) NOT NULL,
    LastLoginAt NVARCHAR(32) NULL
);

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AuditLog' AND xtype='U')
CREATE TABLE AuditLog (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    UserName NVARCHAR(256),
    Action   NVARCHAR(100) NOT NULL,
    Entity   NVARCHAR(100),
    Detail   NVARCHAR(MAX),
    CreatedAt NVARCHAR(32) NOT NULL
);

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AppUserSavedView' AND xtype='U')
CREATE TABLE AppUserSavedView (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ProjectName NVARCHAR(128) NOT NULL,
    PageName NVARCHAR(128) NOT NULL,
    UserName NVARCHAR(256) NOT NULL,
    ViewName NVARCHAR(128) NOT NULL,
    FiltersJson NVARCHAR(MAX) NOT NULL,
    IsDefault BIT NOT NULL DEFAULT 0,
    CreatedAt NVARCHAR(32) NOT NULL,
    UpdatedAt NVARCHAR(32) NOT NULL,
    CONSTRAINT UQ_AppUserSavedView UNIQUE(ProjectName, PageName, UserName, ViewName)
);

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AppUserRole' AND xtype='U')
CREATE TABLE AppUserRole (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    UserName NVARCHAR(256) NOT NULL,
    RoleName NVARCHAR(128) NOT NULL,
    CreatedAt NVARCHAR(32) NOT NULL,
    CONSTRAINT UQ_AppUserRole UNIQUE(UserName, RoleName)
);

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AppRolePermission' AND xtype='U')
CREATE TABLE AppRolePermission (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ProjectName NVARCHAR(128) NOT NULL,
    RoleName NVARCHAR(128) NOT NULL,
    ResourceType NVARCHAR(64) NOT NULL,
    ResourceName NVARCHAR(256) NOT NULL,
    CanRead BIT NOT NULL DEFAULT 1,
    CanWrite BIT NOT NULL DEFAULT 0,
    CreatedAt NVARCHAR(32) NOT NULL,
    UpdatedAt NVARCHAR(32) NOT NULL,
    CONSTRAINT UQ_AppRolePermission UNIQUE(ProjectName, RoleName, ResourceType, ResourceName)
);";

        await conn.ExecuteAsync(sql);
        await conn.ExecuteAsync(@"
IF COL_LENGTH('AppUser', 'LastLoginAt') IS NULL
    ALTER TABLE AppUser ADD LastLoginAt NVARCHAR(32) NULL;");
        logger.LogInformation("認証テーブル確認済み (SQL Server)");
    }
}
