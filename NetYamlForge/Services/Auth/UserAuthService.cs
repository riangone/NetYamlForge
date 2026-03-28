// ファイル概要: ユーザー認証・作成・更新をDB経由で実行するサービス実装です。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。
//
// DCS003 抑制理由: このサービスは認証専用の接続を管理するため、OpenConnection() で
// 直接接続を生成します（認証DB接続のライフサイクルが通常の DI スコープと異なるため）。
#pragma warning disable DCS003

using System.Data;
using System.Data.Common;
using Dapper;
using NetYamlForge.Models.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

namespace NetYamlForge.Services.Auth;

public class UserAuthService : IUserAuthService
{
    private readonly string _connectionString;
    private readonly string _dbType;
    private readonly ILogger<UserAuthService> _logger;
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    public UserAuthService(ProjectScope scope, ILogger<UserAuthService> logger)
    {
        _connectionString = scope.Current.ConnectionString;
        _dbType = scope.Current.DatabaseType;
        _logger = logger;
    }

    public async Task<AppUser?> ValidateCredentialsAsync(string userName, string password)
    {
        // アクティブなユーザーのみを対象にパスワードハッシュ検証を行います。
        await using var conn = OpenConnection();
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
        await using var conn = OpenConnection();
        var roles = await conn.QueryAsync<string>(
            "SELECT RoleName FROM AppUserRole WHERE UserName = @UserName",
            new { UserName = userName });
        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        // ログイン成功時刻の更新は失敗しても認証可否に影響させない方針で呼び出します。
        await using var conn = OpenConnection();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "UPDATE AppUser SET LastLoginAt = @Now WHERE Id = @Id",
            new { Now = now, Id = userId });
    }

    public async Task<IReadOnlyList<AppUser>> GetAllAsync()
    {
        await using var conn = OpenConnection();
        var items = await conn.QueryAsync<AppUser>("SELECT * FROM AppUser ORDER BY Id ASC");
        return items.ToList();
    }

    public async Task<AppUser?> GetByIdAsync(int id)
    {
        await using var conn = OpenConnection();
        return await conn.QueryFirstOrDefaultAsync<AppUser>("SELECT * FROM AppUser WHERE Id = @Id", new { Id = id });
    }

    public async Task<int> CreateAsync(UserEditViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        // 外部Txが渡された場合はその接続を使い、監査ログとの原子性を維持します。
        var ownConnection = connection == null;
        var conn = connection ?? OpenConnection();
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
            var dbType = _dbType.ToLowerInvariant();
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
            if (ownConnection && conn is IDisposable disposable)
            {
                disposable.Dispose();
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
        var conn = connection ?? OpenConnection();
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
            if (ownConnection && conn is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private DbConnection OpenConnection()
    {
        // DbConnection は IAsyncDisposable を実装するため await using が使えます。
        var dbType = _dbType.ToLowerInvariant();
        if (dbType == "sqlserver")
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
        else if (dbType == "postgresql" || dbType == "postgres")
        {
            var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
        else if (dbType == "mysql" || dbType == "mariadb")
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
        else
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
