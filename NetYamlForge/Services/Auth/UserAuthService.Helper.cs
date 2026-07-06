#pragma warning disable DCS001

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models.Auth;

namespace NetYamlForge.Services.Auth;

public partial class UserAuthService
{
    /// <summary>
    /// user_name 列を持つ Customer テーブルを検索する
    /// </summary>
    private async Task<string?> FindCustomerTableAsync(IDbConnection conn, IDbTransaction? transaction)
    {
        // 候補となるテーブル名をチェック（一般的な命名規則）
        var candidateTableNames = new[] { "customers", "Customer", "customer", "Customers" };
        
        foreach (var tableName in candidateTableNames)
        {
            var tableExists = await CheckTableExistsAsync(conn, tableName, transaction);
            if (!tableExists)
            {
                continue;
            }

            // user_name または UserName 列が存在するかチェック
            var hasUserNameColumn = await CheckColumnExistsAsync(conn, tableName, new[] { "user_name", "UserName" }, transaction);
            if (hasUserNameColumn)
            {
                return tableName;
            }
        }

        return null;
    }

    /// <summary>
    /// テーブル構造情報を取得する
    /// </summary>
    private async Task<CustomerTableInfo?> GetCustomerTableInfoAsync(IDbConnection conn, string tableName, IDbTransaction? transaction)
    {
        var dbType = _scope.Current.DatabaseType.ToLowerInvariant();
        
        // 主キー列と user_name 列、updated_at 列を検出
        string primaryKeyColumn = "customer_id"; // デフォルト
        string userNameColumn = "user_name";     // デフォルト
        string updatedAtColumn = "updated_at";   // デフォルト
        bool primaryKeyIsIdentity = false;

        // SQLite の場合
        if (dbType == "sqlite")
        {
            // tableName は FindCustomerTableAsync で検証済みのため安全
            var columns = await conn.QueryAsync<(string name, string type, bool isPk)>(
                $"PRAGMA table_info({tableName})", transaction: transaction);
            
            foreach (var col in columns)
            {
                if (col.isPk)
                {
                    primaryKeyColumn = col.name;
                    primaryKeyIsIdentity = col.type.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);
                }
                if (col.name.Equals("user_name", StringComparison.OrdinalIgnoreCase) || 
                    col.name.Equals("UserName", StringComparison.OrdinalIgnoreCase))
                {
                    userNameColumn = col.name;
                }
                if (col.name.Equals("updated_at", StringComparison.OrdinalIgnoreCase) || 
                    col.name.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase) ||
                    col.name.Equals("update_at", StringComparison.OrdinalIgnoreCase))
                {
                    updatedAtColumn = col.name;
                }
            }
        }
        // SQL Server の場合
        else if (dbType == "sqlserver")
        {
            var sql = @"
SELECT 
    c.COLUMN_NAME as name,
    c.DATA_TYPE as type,
    CASE WHEN kcu.CONSTRAINT_TYPE = 'PRIMARY KEY' THEN 1 ELSE 0 END as isPk
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu 
    ON c.TABLE_NAME = kcu.TABLE_NAME AND c.COLUMN_NAME = kcu.COLUMN_NAME
WHERE c.TABLE_NAME = @tableName";
            
            var columns = await conn.QueryAsync<(string name, string type, int isPk)>(sql, new { tableName }, transaction);
            
            foreach (var col in columns)
            {
                if (col.isPk == 1)
                {
                    primaryKeyColumn = col.name;
                    primaryKeyIsIdentity = true; // SQL Server の主キーは通常 IDENTITY
                }
                if (col.name.Equals("user_name", StringComparison.OrdinalIgnoreCase) || 
                    col.name.Equals("UserName", StringComparison.OrdinalIgnoreCase))
                {
                    userNameColumn = col.name;
                }
                if (col.name.Equals("updated_at", StringComparison.OrdinalIgnoreCase) || 
                    col.name.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase))
                {
                    updatedAtColumn = col.name;
                }
            }
        }
        // PostgreSQL/MySQL の場合
        else
        {
            var sql = dbType switch
            {
                "postgresql" or "postgres" => @"
SELECT 
    c.column_name as name,
    c.data_type as type,
    CASE WHEN kcu.column_name IS NOT NULL THEN 1 ELSE 0 END as isPk
FROM information_schema.columns c
LEFT JOIN information_schema.key_column_usage kcu 
    ON c.table_name = kcu.table_name AND c.column_name = kcu.column_name
WHERE c.table_name = @tableName",
                _ => @"
SELECT 
    c.COLUMN_NAME as name,
    c.DATA_TYPE as type,
    CASE WHEN kcu.CONSTRAINT_TYPE = 'PRIMARY KEY' THEN 1 ELSE 0 END as isPk
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu 
    ON c.TABLE_NAME = kcu.TABLE_NAME AND c.COLUMN_NAME = kcu.COLUMN_NAME
WHERE c.TABLE_NAME = @tableName"
            };
            
            var columns = await conn.QueryAsync<(string name, string type, int isPk)>(sql, new { tableName }, transaction);
            
            foreach (var col in columns)
            {
                if (col.isPk == 1)
                {
                    primaryKeyColumn = col.name;
                    primaryKeyIsIdentity = true;
                }
                if (col.name.Equals("user_name", StringComparison.OrdinalIgnoreCase) || 
                    col.name.Equals("UserName", StringComparison.OrdinalIgnoreCase))
                {
                    userNameColumn = col.name;
                }
                if (col.name.Equals("updated_at", StringComparison.OrdinalIgnoreCase) || 
                    col.name.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase))
                {
                    updatedAtColumn = col.name;
                }
            }
        }

        return new CustomerTableInfo
        {
            TableName = tableName,
            PrimaryKeyColumn = primaryKeyColumn,
            PrimaryKeyIsIdentity = primaryKeyIsIdentity,
            UserNameColumn = userNameColumn,
            UpdatedAtColumn = updatedAtColumn
        };
    }

    /// <summary>
    /// 列の存在チェックを行う
    /// </summary>
    private async Task<bool> CheckColumnExistsAsync(IDbConnection conn, string tableName, string[] columnNames, IDbTransaction? transaction)
    {
        var dbType = _scope.Current.DatabaseType.ToLowerInvariant();
        
        foreach (var columnName in columnNames)
        {
            string sql = dbType switch
            {
                "sqlserver" => @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName
) THEN 1 ELSE 0 END",
                "postgresql" or "postgres" => @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM information_schema.columns 
    WHERE table_name = @tableName AND column_name = @columnName
) THEN 1 ELSE 0 END",
                "mysql" or "mariadb" => @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM information_schema.columns 
    WHERE table_schema = DATABASE() AND table_name = @tableName AND column_name = @columnName
) THEN 1 ELSE 0 END",
                _ => @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM pragma_table_info(@tableName) WHERE name = @columnName
) THEN 1 ELSE 0 END"
            };

            var result = await conn.ExecuteScalarAsync<int>(sql, new { tableName, columnName }, transaction);
            if (result > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Customer テーブルへの INSERT 文を構築する
    /// </summary>
    private string BuildCustomerInsertSql(CustomerTableInfo tableInfo, string customerId, string now)
    {
        // 標準的なカラムリスト（auto-dealer-demo 形式）
        return $@"
INSERT INTO {tableInfo.TableName} (
    {tableInfo.PrimaryKeyColumn}, 
    {tableInfo.UserNameColumn}, 
    customer_type, 
    name, 
    name_kana, 
    phone, 
    mobile, 
    email, 
    tier_level, 
    preferred_contact, 
    created_at, 
    {tableInfo.UpdatedAtColumn})
VALUES (
    @CustomerId, 
    @UserName, 
    @CustomerType, 
    @Name, 
    @NameKana, 
    @Phone, 
    @Mobile, 
    @Email, 
    @TierLevel, 
    @PreferredContact, 
    @CreatedAt, 
    @UpdatedAt)";
    }

    /// <summary>
    /// テーブルの存在チェックを行う（SQLite/PostgreSQL/MySQL/SQL Server 対応）
    /// </summary>
    private async Task<bool> CheckTableExistsAsync(IDbConnection conn, string tableName, IDbTransaction? transaction)
    {
        var dbType = _scope.Current.DatabaseType.ToLowerInvariant();
        string sql = dbType switch
        {
            "sqlserver" => @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE name = @tableName) THEN 1 ELSE 0 END",
            "postgresql" or "postgres" => @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = @tableName) THEN 1 ELSE 0 END",
            "mysql" or "mariadb" => @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @tableName) THEN 1 ELSE 0 END",
            _ => @"SELECT CASE WHEN EXISTS (SELECT 1 FROM sqlite_master WHERE type='table' AND name = @tableName) THEN 1 ELSE 0 END"
        };

        var result = await conn.ExecuteScalarAsync<int>(sql, new { tableName }, transaction);
        return result > 0;
    }

    private async Task<long> InsertUserAsync(AppUser user, IDbConnection conn, IDbTransaction? transaction)
    {
        // system.db は常に SQLite 形式
        var sql = @"
INSERT INTO app_user (user_name, password_hash, display_name, preferred_language, is_admin, is_active, external_id, external_source, owning_project, created_at, updated_at)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @ExternalId, @ExternalSource, @OwningProject, @CreatedAt, @CreatedAt);
SELECT last_insert_rowid();";
        return await conn.ExecuteScalarAsync<long>(sql, user, transaction);
    }

    private static string GenerateRandomPassword()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-_=+";
        var random = new Random();
        var password = new char[16];
        for (int i = 0; i < 16; i++)
        {
            password[i] = chars[random.Next(chars.Length)];
        }
        return new string(password);
    }
}
