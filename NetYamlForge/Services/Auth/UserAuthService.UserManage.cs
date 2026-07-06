using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace NetYamlForge.Services.Auth;

public partial class UserAuthService
{
    public async Task<IReadOnlyList<AppUser>> GetAllAsync(string? owningProject = null)
    {
        await using var conn = await GetConnectionAsync();
        if (string.IsNullOrEmpty(owningProject))
        {
            var items = await conn.QueryAsync<AppUser>("SELECT * FROM app_user ORDER BY id ASC");
            return items.ToList();
        }
        else
        {
            var items = await conn.QueryAsync<AppUser>(
                "SELECT * FROM app_user WHERE owning_project = @OwningProject ORDER BY Id ASC",
                new { OwningProject = owningProject });
            return items.ToList();
        }
    }

    public async Task<AppUser?> GetByIdAsync(int id)
    {
        await using var conn = await GetConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<AppUser>("SELECT * FROM app_user WHERE id = @Id", new { Id = id });
    }

    public async Task<int> CreateAsync(UserEditViewModel input, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        // 外部Txが渡された場合はその接続を使い、監査ログとの原子性を維持します。
        var ownConnection = connection == null;
        var conn = connection ?? await GetConnectionAsync();
        try
        {
            var existing = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM app_user WHERE user_name = @UserName", new { input.UserName }, transaction);
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
                ExternalId = input.ExternalId,
                ExternalSource = input.ExternalSource,
                OwningProject = input.OwningProject,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var password = string.IsNullOrWhiteSpace(input.Password) ? GenerateRandomPassword() : input.Password;
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            var id = await InsertUserAsync(user, conn, transaction);
            _logger.LogInformation("Created user '{UserName}' with id {UserId}", user.UserName, id);
            return (int)id;
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                // Phase 2: 释放连接回池（而不是关闭）
                conn.Close();
                conn.Dispose();
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
            var current = await conn.QueryFirstOrDefaultAsync<AppUser>("SELECT * FROM app_user WHERE id = @Id", new { Id = input.Id.Value }, transaction);
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
UPDATE app_user
SET user_name = @UserName,
    password_hash = @PasswordHash,
    display_name = @DisplayName,
    preferred_language = @PreferredLanguage,
    is_admin = @IsAdmin,
    is_active = @IsActive,
    owning_project = @OwningProject
WHERE id = @Id", new
        {
            Id = input.Id.Value,
            input.UserName,
            PasswordHash = passwordHash,
            input.DisplayName,
            input.PreferredLanguage,
            input.IsAdmin,
            input.IsActive,
            input.OwningProject
            }, transaction);

            // Sync AppUserRole with IsAdmin flag
            var existingRoles = (await conn.QueryAsync<string>(
                "SELECT role_name FROM app_user_role WHERE user_name = @UserName",
                new { UserName = current.UserName }, transaction)).ToList();
            var hasAdminRole = existingRoles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase));

            if (!string.Equals(current.UserName, input.UserName, StringComparison.OrdinalIgnoreCase))
            {
                await conn.ExecuteAsync(
                    "UPDATE app_user_role SET user_name = @NewUserName WHERE user_name = @OldUserName",
                    new { NewUserName = input.UserName, OldUserName = current.UserName }, transaction);
            }

            if (input.IsAdmin && !hasAdminRole)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO app_user_role (user_name, role_name, created_at) VALUES (@UserName, @RoleName, @Now)",
                    new { UserName = input.UserName, RoleName = "Admin", Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }, transaction);
            }
            else if (!input.IsAdmin && hasAdminRole)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM app_user_role WHERE user_name = @UserName AND role_name = @RoleName",
                    new { UserName = input.UserName, RoleName = "Admin", Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") }, transaction);
            }

            _logger.LogInformation("Updated user {UserId} ('{UserName}')", input.Id.Value, input.UserName);
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                conn.Close();
                conn.Dispose();
            }
        }
    }

    public async Task DeleteAsync(int id, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var ownConnection = connection == null;
        var conn = connection ?? await GetConnectionAsync();
        try
        {
            var user = await conn.QueryFirstOrDefaultAsync<AppUser>("SELECT * FROM app_user WHERE id = @Id", new { Id = id }, transaction);
            if (user == null)
            {
                throw new InvalidOperationException("User not found.");
            }

            await conn.ExecuteAsync(
                "DELETE FROM app_user_role WHERE user_name = @UserName",
                new { user.UserName }, transaction);

            await conn.ExecuteAsync(
                "DELETE FROM app_user WHERE id = @Id",
                new { Id = id }, transaction);

            _logger.LogInformation("Deleted user {UserId} ('{UserName}')", id, user.UserName);
        }
        finally
        {
            if (ownConnection && conn != null)
            {
                conn.Close();
                conn.Dispose();
            }
        }
    }

    public async Task<bool> IsUserNameTakenAsync(string userName)
    {
        await using var conn = await GetConnectionAsync();
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM app_user WHERE user_name = @UserName",
            new { UserName = userName });
        return count > 0;
    }
}
