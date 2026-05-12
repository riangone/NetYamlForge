// ファイル概要: ユーザー認証・作成・更新をDB経由で実行するサービス実装です。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。
//
// Phase 2 改进：使用 IConnectionManager 替代直接创建连接，实现连接复用
#pragma warning disable DCS001

using System.Data;
using System.Data.Common;
using Dapper;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services.Connection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

namespace NetYamlForge.Services.Auth;

public class UserAuthService : IUserAuthService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ProjectScope _scope;
    private readonly ILogger<UserAuthService> _logger;
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    public UserAuthService(
        IConnectionManager connectionManager,
        ProjectScope scope,
        ILogger<UserAuthService> logger)
    {
        _connectionManager = connectionManager;
        _scope = scope;
        _logger = logger;
    }

    public async Task<AppUser?> ValidateCredentialsAsync(string userName, string password)
    {
        // アクティブなユーザーのみを対象にパスワードハッシュ検証を行います。
        await using var conn = await GetConnectionAsync();
        var user = await conn.QueryFirstOrDefaultAsync<AppUser>(
            "SELECT * FROM AppUser WHERE UserName = @UserName AND IsActive = 1",
            new { UserName = userName });

        if (user == null)
        {
            _logger.LogWarning("Authentication failed for user '{UserName}' - user not found or inactive", userName);
            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            _logger.LogInformation("Authentication success for user '{UserName}'", userName);
            return user;
        }

        _logger.LogWarning("Authentication failed for user '{UserName}' - invalid password", userName);
        return null;
    }

    public async Task<IReadOnlyList<string>> GetUserRolesAsync(string userName)
    {
        await using var conn = await GetConnectionAsync();
        var roles = await conn.QueryAsync<string>(
            "SELECT RoleName FROM AppUserRole WHERE UserName = @UserName",
            new { UserName = userName });
        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        // ログイン成功時刻の更新は失敗しても認証可否に影響させない方針で呼び出します。
        await using var conn = await GetConnectionAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "UPDATE AppUser SET LastLoginAt = @Now WHERE Id = @Id",
            new { Now = now, Id = userId });
    }

    public async Task<IReadOnlyList<AppUser>> GetAllAsync()
    {
        await using var conn = await GetConnectionAsync();
        var items = await conn.QueryAsync<AppUser>("SELECT * FROM AppUser ORDER BY Id ASC");
        return items.ToList();
    }

    public async Task<AppUser?> GetByIdAsync(int id)
    {
        await using var conn = await GetConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<AppUser>("SELECT * FROM AppUser WHERE Id = @Id", new { Id = id });
    }

    public async Task<int> CreateAsync(UserEditViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        // 外部Txが渡された場合はその接続を使い、監査ログとの原子性を維持します。
        var ownConnection = connection == null;
        var conn = connection ?? await GetConnectionAsync();
        try
        {
            var existing = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AppUser WHERE UserName = @UserName", new { input.UserName }, transaction);
            if (existing > 0)
            {
                throw new InvalidOperationException($"User '{input.UserName}' already exists.");
            }

            var user = new AppUser
            {
                UserName = input.UserName,
                DisplayName = input.DisplayName,
                PreferredLanguage = input.PreferredLanguage,
                IsAdmin = input.IsAdmin,
                IsActive = input.IsActive,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var password = string.IsNullOrWhiteSpace(input.Password) ? "ChangeMe123!" : input.Password;
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            long id;
            var dbType = _scope.Current.DatabaseType.ToLowerInvariant();
            if (dbType == "sqlserver")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
OUTPUT INSERTED.Id
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);";
                id = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else if (dbType == "postgresql" || dbType == "postgres")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt)
RETURNING Id;";
                id = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else if (dbType == "mysql" || dbType == "mariadb")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);
SELECT LAST_INSERT_ID();";
                id = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);
SELECT last_insert_rowid();";
                id = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }

            _logger.LogInformation("Created user '{UserName}' with id {UserId}", user.UserName, id);
            return (int)id;
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                // Phase 2: 释放连接回池（而不是关闭）
                _connectionManager.ReleaseConnection(conn);
            }
        }
    }

    public async Task UpdateAsync(UserEditViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        // パスワード未指定時は既存ハッシュを保持し、指定時のみ再ハッシュします。
        if (!input.Id.HasValue)
        {
            throw new InvalidOperationException("User id is required for update.");
        }

        var ownConnection = connection == null;
        var conn = connection ?? await GetConnectionAsync();
        try
        {
            var current = await conn.QueryFirstOrDefaultAsync<AppUser>("SELECT * FROM AppUser WHERE Id = @Id", new { Id = input.Id.Value }, transaction);
            if (current == null)
            {
                throw new InvalidOperationException("User not found.");
            }

            var passwordHash = current.PasswordHash;
            if (!string.IsNullOrWhiteSpace(input.Password))
            {
                current.UserName = input.UserName;
                passwordHash = _passwordHasher.HashPassword(current, input.Password);
            }

            await conn.ExecuteAsync(@"
UPDATE AppUser
SET UserName = @UserName,
    PasswordHash = @PasswordHash,
    DisplayName = @DisplayName,
    PreferredLanguage = @PreferredLanguage,
    IsAdmin = @IsAdmin,
    IsActive = @IsActive
WHERE Id = @Id", new
        {
            Id = input.Id.Value,
            input.UserName,
            PasswordHash = passwordHash,
            input.DisplayName,
            input.PreferredLanguage,
            input.IsAdmin,
            input.IsActive
            }, transaction);

            _logger.LogInformation("Updated user {UserId} ('{UserName}')", input.Id.Value, input.UserName);
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                _connectionManager.ReleaseConnection(conn);
            }
        }
    }

    public async Task<bool> IsUserNameTakenAsync(string userName)
    {
        await using var conn = await GetConnectionAsync();
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM AppUser WHERE UserName = @UserName",
            new { UserName = userName });
        return count > 0;
    }

    public async Task<int> RegisterAsync(RegisterViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var ownConnection = connection == null;
        var conn = connection ?? await GetConnectionAsync();
        try
        {
            // ユーザー名チェック
            var existing = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AppUser WHERE UserName = @UserName",
                new { input.UserName }, transaction);
            if (existing > 0)
            {
                throw new InvalidOperationException($"User '{input.UserName}' already exists.");
            }

            var user = new AppUser
            {
                UserName = input.UserName,
                DisplayName = input.DisplayName,
                PreferredLanguage = input.PreferredLanguage ?? "ja-JP",
                IsAdmin = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var password = string.IsNullOrWhiteSpace(input.Password) ? "ChangeMe123!" : input.Password;
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            long id;
            var dbType = _scope.Current.DatabaseType.ToLowerInvariant();
            if (dbType == "sqlserver")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
OUTPUT INSERTED.Id
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);";
                id = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else if (dbType == "postgresql" || dbType == "postgres")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt)
RETURNING Id;";
                id = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else if (dbType == "mysql" || dbType == "mariadb")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);
SELECT LAST_INSERT_ID();";
                id = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);
SELECT last_insert_rowid();";
                id = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }

            // カスタムロール割り当て（customer ロール）
            await conn.ExecuteAsync(
                "INSERT INTO AppUserRole (UserName, RoleName) VALUES (@UserName, @RoleName)",
                new { input.UserName, RoleName = "customer" }, transaction);

            _logger.LogInformation("Registered user '{UserName}' with id {UserId}", user.UserName, id);
            return (int)id;
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                _connectionManager.ReleaseConnection(conn);
            }
        }
    }

    public async Task<int> RegisterCustomerAsync(CustomerRegisterViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var ownConnection = connection == null;
        var conn = connection ?? await GetConnectionAsync();
        try
        {
            // ユーザー名チェック
            var existing = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AppUser WHERE UserName = @UserName",
                new { input.UserName }, transaction);
            if (existing > 0)
            {
                throw new InvalidOperationException($"User '{input.UserName}' already exists.");
            }

            // トランザクション内でユーザーと顧客の両方を登録
            var user = new AppUser
            {
                UserName = input.UserName,
                DisplayName = input.Name,
                PreferredLanguage = input.PreferredLanguage ?? "ja-JP",
                IsAdmin = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var password = string.IsNullOrWhiteSpace(input.Password) ? "ChangeMe123!" : input.Password;
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            long userId;
            var dbType = _scope.Current.DatabaseType.ToLowerInvariant();
            if (dbType == "sqlserver")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
OUTPUT INSERTED.Id
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);";
                userId = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else if (dbType == "postgresql" || dbType == "postgres")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt)
RETURNING Id;";
                userId = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else if (dbType == "mysql" || dbType == "mariadb")
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);
SELECT LAST_INSERT_ID();";
                userId = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }
            else
            {
                var sql = @"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt);
SELECT last_insert_rowid();";
                userId = await conn.ExecuteScalarAsync<long>(sql, user, transaction);
            }

            // customer ロールを付与
            await conn.ExecuteAsync(
                "INSERT INTO AppUserRole (UserName, RoleName) VALUES (@UserName, @RoleName)",
                new { input.UserName, RoleName = "customer" }, transaction);

            // customers テーブルにもレコードを作成（auto-dealer-demo など）
            // 既存の customers レコードがある場合は user_name を更新する
            var customerId = await SyncCustomerRecordAsync(conn, input, userId, transaction);

            _logger.LogInformation("Registered customer '{UserName}' with user id {UserId}", user.UserName, userId);
            return (int)userId;
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                _connectionManager.ReleaseConnection(conn);
            }
        }
    }

    /// <summary>
    /// 顧客登録時に Customer/Customers テーブルにもレコードを作成/更新する。
    /// user_name/UserName 列を持つテーブルのみが対象（auto-dealer-demo など）。
    /// 取引先マスタ（biz-docs）など、ユーザー認証と関係ない Customer 表は対象外。
    /// </summary>
    private async Task<string?> SyncCustomerRecordAsync(IDbConnection conn, CustomerRegisterViewModel input, long userId, IDbTransaction? transaction)
    {
        // 対象テーブルを検索（customers, Customer, customer の順でチェック）
        var targetTable = await FindCustomerTableAsync(conn, transaction);
        if (targetTable == null)
        {
            _logger.LogDebug("Customer table with user_name column does not exist, skipping sync");
            return null;
        }

        // テーブル構造情報を取得
        var tableInfo = await GetCustomerTableInfoAsync(conn, targetTable, transaction);
        if (tableInfo == null)
        {
            _logger.LogDebug("Failed to get table info for {Table}, skipping sync", targetTable);
            return null;
        }

        // 既に同じ user_name の顧客レコードが存在するかチェック
        // 表名・列名は DB 元情報から取得されるため安全
        var selectSql = $"SELECT {tableInfo.PrimaryKeyColumn} FROM {tableInfo.TableName} WHERE {tableInfo.UserNameColumn} = @UserName";
        var existingCustomerId = await conn.ExecuteScalarAsync<string>(
            selectSql,
            new { UserName = input.UserName }, transaction);

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var customerId = tableInfo.PrimaryKeyIsIdentity
            ? existingCustomerId ?? $"CUST-{DateTime.Now:yyyyMMddHHmmss}-{userId:D6}"
            : $"CUST-{DateTime.Now:yyyyMMddHHmmss}-{userId:D6}";

        if (string.IsNullOrEmpty(existingCustomerId))
        {
            // 新規作成
            var insertSql = BuildCustomerInsertSql(tableInfo, customerId, now);
            var insertParams = new
            {
                CustomerId = customerId,
                UserName = input.UserName,
                CustomerType = "individual",
                Name = input.Name,
                NameKana = input.NameKana ?? "",
                Phone = input.Phone,
                Mobile = input.Mobile ?? "",
                Email = input.Email ?? "",
                TierLevel = "regular",
                PreferredContact = input.PreferredContact ?? "phone",
                CreatedAt = now,
                UpdatedAt = now
            };

            await conn.ExecuteAsync(insertSql, insertParams, transaction);

            _logger.LogDebug("Created customer record '{CustomerId}' for user '{UserName}' in table {Table}",
                customerId, input.UserName, tableInfo.TableName);
        }
        else
        {
            customerId = existingCustomerId;
            // 既存レコードの user_name を更新（既に顧客レコードがある場合）
            var updateSql = $"UPDATE {tableInfo.TableName} SET {tableInfo.UserNameColumn} = @UserName, {tableInfo.UpdatedAtColumn} = @UpdatedAt WHERE {tableInfo.PrimaryKeyColumn} = @CustomerId";
            await conn.ExecuteAsync(updateSql,
                new { UserName = input.UserName, UpdatedAt = now, CustomerId = existingCustomerId },
                transaction);

            _logger.LogDebug("Updated customer record '{CustomerId}' with user_name '{UserName}' in table {Table}",
                existingCustomerId, input.UserName, tableInfo.TableName);
        }

        return customerId;
    }

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

    private class CustomerTableInfo
    {
        public string TableName { get; set; } = string.Empty;
        public string PrimaryKeyColumn { get; set; } = string.Empty;
        public bool PrimaryKeyIsIdentity { get; set; }
        public string UserNameColumn { get; set; } = string.Empty;
        public string UpdatedAtColumn { get; set; } = string.Empty;
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

    /// <summary>
    /// Phase 2: 从连接管理器获取连接（复用连接池）
    /// </summary>
    private async Task<DbConnection> GetConnectionAsync()
    {
        var conn = await _connectionManager.GetConnectionAsync(_scope.Current.Name);
        return (DbConnection)conn;
    }

}
