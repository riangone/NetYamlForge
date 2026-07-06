// ファイル概要: ユーザー認証・作成・更新をDB経由で実行するサービス実装です。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。
//
// Phase 2 改进：使用 IConnectionManager 替代直接创建连接，实现连接复用
#pragma warning disable DCS001

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services.Connection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Auth;

public partial class UserAuthService : IUserAuthService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ProjectScope _scope;
    private readonly ILogger<UserAuthService> _logger;
    private readonly PasswordHasher<AppUser> _passwordHasher = new();
    private readonly string _systemDbConnectionString;

    public UserAuthService(
        IConnectionManager connectionManager,
        ProjectScope scope,
        IConfiguration config,
        ILogger<UserAuthService> logger)
    {
        _connectionManager = connectionManager;
        _scope = scope;
        _logger = logger;
        var dbPath = config["SystemDbPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "system.db");
        _systemDbConnectionString = dbPath.Contains(';') ? dbPath : new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
    }

    public async Task<AppUser?> ValidateCredentialsAsync(string userName, string password)
    {
        // アクティブなユーザーのみを対象にパスワードハッシュ検証を行います。
        await using var conn = await GetConnectionAsync();
        var user = await conn.QueryFirstOrDefaultAsync<AppUser>(
            "SELECT * FROM app_user WHERE user_name = @UserName AND is_active = 1",
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

    public async Task<AppUser?> GetByApiTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var conn = await GetConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<AppUser>(
            "SELECT * FROM app_user WHERE api_token = @Token AND is_active = 1",
            new { Token = token });
    }

    public async Task<IReadOnlyList<string>> GetUserRolesAsync(string userName)
    {
        await using var conn = await GetConnectionAsync();
        var roles = await conn.QueryAsync<string>(
            "SELECT role_name FROM app_user_role WHERE user_name = @UserName",
            new { UserName = userName });
        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        // ログイン成功時刻の更新は失敗しても認証可否に影響させない方針で呼び出します。
        await using var conn = await GetConnectionAsync();
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "UPDATE app_user SET last_login_at = @Now WHERE id = @Id",
            new { Now = now, Id = userId });
    }

    /// <summary>
    /// Phase 2: 从连接管理器获取连接（复用连接池）
    /// </summary>
    private async Task<DbConnection> GetConnectionAsync()
    {
        // DCS003 抑制理由: system.db はグローバル認証 DB であり ProjectScope に依存しないため直接接続する
#pragma warning disable DCS003
        var conn = new SqliteConnection(_systemDbConnectionString);
#pragma warning restore DCS003
        await conn.OpenAsync();
        await NetYamlForge.Services.Connection.SqliteConnectionHardening.ApplyAsync(conn);
        return conn;
    }
}
