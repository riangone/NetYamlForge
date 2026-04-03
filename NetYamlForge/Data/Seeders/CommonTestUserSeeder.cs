// ファイル概要: 全プロジェクト共通のテストユーザーシーダーです。
// 全局管理员、各项目管理员、各业务角色のテストユーザーを作成します。
// 既に存在するユーザーはスキップします。パスワードはすべて Test@123! です。

using System.Data;
using Dapper;
using NetYamlForge.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// 全プロジェクト共通のテストユーザーシード。
/// 全局管理员、项目管理员、各业务角色のテストアカウントを作成します。
/// </summary>
public class CommonTestUserSeeder
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    /// <summary>
    /// 共通テストユーザーを作成します（存在しない場合のみ）。
    /// </summary>
    public async Task EnsureCommonTestUsersAsync(IDbConnection conn, ILogger logger)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var createdCount = 0;

        // 全局管理员（可登录任何项目）
        var globalAdmins = new[]
        {
            ("globaladmin", "全局管理员", "Global Admin", true, new[] { "AdminOps", "admin" }),
        };

        // 项目管理员（特定项目管理）
        // 注意：每个项目管理员需要分配到对应的项目，否则无法登录
        var projectAdmins = new[]
        {
            ("framework_admin", "框架管理员", "Framework Admin", true, new[] { "AdminOps", "admin", "operator" }, "framework"),
            ("auto_admin", "汽车项目管理", "Auto Project Admin", true, new[] { "AdminOps", "admin", "ai_admin" }, "auto-dealer-demo"),
            ("bizdocs_admin", "文档项目管理", "BizDocs Admin", true, new[] { "AdminOps", "admin", "operator" }, "framework"),
            ("inventory_admin", "库存项目管理", "Inventory Admin", true, new[] { "AdminOps", "admin", "operator" }, "inventory"),
            ("todo_admin", "任务项目管理", "Todo Admin", true, new[] { "AdminOps", "admin", "operator" }, "framework"),
        };

        // 通用业务角色（可根据需要添加到各项目）
        var commonBusinessUsers = new[]
        {
            ("operator1", "操作员1号", "Operator 1", false, new[] { "operator" }),
            ("viewer1", "查看者1号", "Viewer 1", false, new[] { "viewer" }),
            ("editor1", "编辑者1号", "Editor 1", false, new[] { "editor" }),
        };

        // 创建全局管理员
        foreach (var (userName, displayNameCn, displayNameEn, isAdmin, roles) in globalAdmins)
        {
            if (await CreateUserIfNotExists(conn, userName, displayNameEn, isAdmin, now, logger))
            {
                createdCount++;
            }

            // 确保角色分配
            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        // 创建项目管理员
        foreach (var (userName, displayNameCn, displayNameEn, isAdmin, roles, projectName) in projectAdmins)
        {
            if (await CreateUserIfNotExists(conn, userName, displayNameEn, isAdmin, now, logger))
            {
                createdCount++;
            }

            // 确保用户角色
            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }

            // [已禁用] 项目分配由 system.db 的多租户系统管理
            // await EnsureUserProjectRole(conn, userName, projectName, "admin", now, logger);
        }

        // 创建通用业务用户
        foreach (var (userName, displayNameCn, displayNameEn, isAdmin, roles) in commonBusinessUsers)
        {
            if (await CreateUserIfNotExists(conn, userName, displayNameEn, isAdmin, now, logger))
            {
                createdCount++;
            }

            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        logger.LogInformation("共通テストユーザー ({Count} 名) のセットアップ完了", createdCount);
    }

    /// <summary>
    /// ユーザーが存在しない場合のみ作成します。
    /// </summary>
    private async Task<bool> CreateUserIfNotExists(
        IDbConnection conn,
        string userName,
        string displayName,
        bool isAdmin,
        string createdAt,
        ILogger logger)
    {
        var existingId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT Id FROM AppUser WHERE UserName = @UserName",
            new { UserName = userName });

        if (existingId.HasValue)
        {
            return false;
        }

        var user = new AppUser
        {
            UserName = userName,
            DisplayName = displayName,
            PreferredLanguage = "zh-CN",
            IsAdmin = isAdmin,
            IsActive = true,
            CreatedAt = createdAt
        };

        // 统一密码：Test@123!
        user.PasswordHash = _hasher.HashPassword(user, "Test@123!");

        await conn.ExecuteAsync(@"
INSERT INTO AppUser (UserName, PasswordHash, DisplayName, PreferredLanguage, IsAdmin, IsActive, CreatedAt)
VALUES (@UserName, @PasswordHash, @DisplayName, @PreferredLanguage, @IsAdmin, @IsActive, @CreatedAt)", user);

        logger.LogInformation("テストユーザー '{UserName}' ({DisplayName}) を作成しました", userName, displayName);
        return true;
    }

    /// <summary>
    /// ユーザーロールを確保します（存在しない場合のみ）。
    /// </summary>
    private async Task EnsureUserRole(
        IDbConnection conn,
        string userName,
        string roleName,
        string createdAt,
        ILogger logger)
    {
        var existingRole = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT COUNT(*) FROM AppUserRole WHERE UserName = @UserName AND RoleName = @RoleName",
            new { UserName = userName, RoleName = roleName });

        if (!existingRole.HasValue || existingRole.Value == 0)
        {
            await conn.ExecuteAsync(
                @"INSERT OR IGNORE INTO AppUserRole (UserName, RoleName, CreatedAt) VALUES (@UserName, @RoleName, @CreatedAt)",
                new { UserName = userName, RoleName = roleName, CreatedAt = createdAt });
        }
    }

    /// <summary>
    /// ユーザーのプロジェクトロールを確保します（存在しない場合のみ）。
    /// これにより、ユーザーが特定プロジェクトにログインできるようになります。
    /// </summary>
    private async Task EnsureUserProjectRole(
        IDbConnection conn,
        string userName,
        string projectName,
        string roleName,
        string createdAt,
        ILogger logger)
    {
        // まずユーザーIDを取得
        var userId = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT Id FROM AppUser WHERE UserName = @UserName",
            new { UserName = userName });

        if (!userId.HasValue)
        {
            logger.LogWarning("ユーザー '{UserName}' が見つからないため、プロジェクトロールをスキップします", userName);
            return;
        }

        // 既存のプロジェクトロールを確認
        var existingProjectRole = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT COUNT(*) FROM app_user_project_role WHERE user_id = @UserId AND project_name = @ProjectName",
            new { UserId = userId.Value, ProjectName = projectName });

        if (!existingProjectRole.HasValue || existingProjectRole.Value == 0)
        {
            await conn.ExecuteAsync(
                @"INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at)
                  VALUES (@UserId, @ProjectName, @RoleName, @UserId, @CreatedAt)",
                new { UserId = userId.Value, ProjectName = projectName, RoleName = roleName, CreatedAt = createdAt });

            logger.LogInformation("ユーザー '{UserName}' をプロジェクト '{ProjectName}' に管理者として割り当てました", userName, projectName);
        }
    }
}
