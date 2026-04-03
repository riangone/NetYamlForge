// ファイル概要: 各サブプロジェクト固有のテストユーザーシーダーです。
// todo-app, framework, biz-docs, inventory 等项目の业务角色用户を作成します。
// 既に存在するユーザーはスキップします。パスワードはすべて Test@123! です。

using System.Data;
using Dapper;
using NetYamlForge.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// 各サブプロジェクト固有のテストユーザーシード。
/// 项目特定的业务角色用户创建。
/// </summary>
public class ProjectSpecificTestUserSeeder
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    /// <summary>
    /// 各プロジェクト固有のテストユーザーを作成します（存在しない場合のみ）。
    /// </summary>
    public async Task EnsureProjectSpecificTestUsersAsync(IDbConnection conn, string projectName, ILogger logger)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var createdCount = 0;

        switch (projectName.ToLowerInvariant())
        {
            case "todo-app":
                createdCount = await EnsureTodoAppTestUsersAsync(conn, now, logger);
                break;

            case "framework":
                createdCount = await EnsureFrameworkTestUsersAsync(conn, now, logger);
                break;

            case "biz-docs":
                createdCount = await EnsureBizDocsTestUsersAsync(conn, now, logger);
                break;

            case "inventory":
                createdCount = await EnsureInventoryTestUsersAsync(conn, now, logger);
                break;

            case "task-management":
                createdCount = await EnsureTaskManagementTestUsersAsync(conn, now, logger);
                break;

            case "ui-showcase":
                createdCount = await EnsureUIShowcaseTestUsersAsync(conn, now, logger);
                break;

            default:
                // 其他项目使用通用业务用户
                createdCount = await EnsureGenericProjectTestUsersAsync(conn, projectName, now, logger);
                break;
        }

        if (createdCount > 0)
        {
            logger.LogInformation("プロジェクト '{ProjectName}' のテストユーザー ({Count} 名) を作成しました", projectName, createdCount);
        }
    }

    /// <summary>
    /// todo-app 用テストユーザー
    /// </summary>
    private async Task<int> EnsureTodoAppTestUsersAsync(IDbConnection conn, string now, ILogger logger)
    {
        var count = 0;
        var todoUsers = new[]
        {
            ("todo_admin1", "任务管理员1号", false, new[] { "admin", "operator" }),
            ("todo_user1", "任务用户1号", false, new[] { "user" }),
            ("todo_user2", "任务用户2号", false, new[] { "user" }),
            ("todo_viewer1", "任务查看者1号", false, new[] { "viewer" }),
        };

        foreach (var (userName, displayName, isAdmin, roles) in todoUsers)
        {
            if (await CreateUserIfNotExists(conn, userName, displayName, isAdmin, now, logger))
            {
                count++;
            }

            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        return count;
    }

    /// <summary>
    /// framework 用テストユーザー
    /// </summary>
    private async Task<int> EnsureFrameworkTestUsersAsync(IDbConnection conn, string now, ILogger logger)
    {
        var count = 0;
        var frameworkUsers = new[]
        {
            ("framework_editor", "框架编辑者", false, new[] { "editor", "operator" }),
            ("framework_user1", "框架用户1号", false, new[] { "user" }),
            ("framework_user2", "框架用户2号", false, new[] { "user" }),
        };

        foreach (var (userName, displayName, isAdmin, roles) in frameworkUsers)
        {
            if (await CreateUserIfNotExists(conn, userName, displayName, isAdmin, now, logger))
            {
                count++;
            }

            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        return count;
    }

    /// <summary>
    /// biz-docs 用テストユーザー
    /// </summary>
    private async Task<int> EnsureBizDocsTestUsersAsync(IDbConnection conn, string now, ILogger logger)
    {
        var count = 0;
        var bizDocsUsers = new[]
        {
            ("bizdocs_manager", "文档管理员", false, new[] { "admin", "operator" }),
            ("bizdocs_author", "文档作者", false, new[] { "editor", "author" }),
            ("bizdocs_reviewer", "文档审核者", false, new[] { "reviewer" }),
            ("bizdocs_user1", "文档用户1号", false, new[] { "user" }),
        };

        foreach (var (userName, displayName, isAdmin, roles) in bizDocsUsers)
        {
            if (await CreateUserIfNotExists(conn, userName, displayName, isAdmin, now, logger))
            {
                count++;
            }

            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        return count;
    }

    /// <summary>
    /// inventory 用テストユーザー
    /// </summary>
    private async Task<int> EnsureInventoryTestUsersAsync(IDbConnection conn, string now, ILogger logger)
    {
        var count = 0;
        var inventoryUsers = new[]
        {
            ("inventory_manager", "库存管理员", false, new[] { "admin", "operator" }),
            ("inventory_staff1", "库存员工1号", false, new[] { "operator", "staff" }),
            ("inventory_staff2", "库存员工2号", false, new[] { "operator", "staff" }),
            ("inventory_viewer", "库存查看者", false, new[] { "viewer" }),
        };

        foreach (var (userName, displayName, isAdmin, roles) in inventoryUsers)
        {
            if (await CreateUserIfNotExists(conn, userName, displayName, isAdmin, now, logger))
            {
                count++;
            }

            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        return count;
    }

    /// <summary>
    /// task-management 用テストユーザー
    /// </summary>
    private async Task<int> EnsureTaskManagementTestUsersAsync(IDbConnection conn, string now, ILogger logger)
    {
        var count = 0;
        var taskMgmtUsers = new[]
        {
            ("taskmgr_admin", "任务管理管理员", false, new[] { "admin", "operator" }),
            ("taskmgr_manager", "任务经理", false, new[] { "manager" }),
            ("taskmgr_worker1", "任务执行者1号", false, new[] { "worker" }),
            ("taskmgr_worker2", "任务执行者2号", false, new[] { "worker" }),
            ("taskmgr_viewer", "任务查看者", false, new[] { "viewer" }),
        };

        foreach (var (userName, displayName, isAdmin, roles) in taskMgmtUsers)
        {
            if (await CreateUserIfNotExists(conn, userName, displayName, isAdmin, now, logger))
            {
                count++;
            }

            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        return count;
    }

    /// <summary>
    /// ui-showcase 用テストユーザー
    /// </summary>
    private async Task<int> EnsureUIShowcaseTestUsersAsync(IDbConnection conn, string now, ILogger logger)
    {
        var count = 0;
        var uiUsers = new[]
        {
            ("ui_demo_user", "UI演示用户", false, new[] { "user", "operator" }),
        };

        foreach (var (userName, displayName, isAdmin, roles) in uiUsers)
        {
            if (await CreateUserIfNotExists(conn, userName, displayName, isAdmin, now, logger))
            {
                count++;
            }

            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        return count;
    }

    /// <summary>
    /// 通用项目用户（适用于未特别指定的项目）
    /// </summary>
    private async Task<int> EnsureGenericProjectTestUsersAsync(IDbConnection conn, string projectName, string now, ILogger logger)
    {
        var count = 0;
        var genericUsers = new[]
        {
            ($"{projectName.Replace("-", "_")}_admin", $"{projectName}管理员", true, new[] { "admin", "operator" }),
            ($"{projectName.Replace("-", "_")}_user1", $"{projectName}用户1号", false, new[] { "user" }),
        };

        foreach (var (userName, displayName, isAdmin, roles) in genericUsers)
        {
            if (await CreateUserIfNotExists(conn, userName, displayName, isAdmin, now, logger))
            {
                count++;
            }

            foreach (var role in roles)
            {
                await EnsureUserRole(conn, userName, role, now, logger);
            }
        }

        return count;
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

        logger.LogDebug("プロジェクト固有ユーザー '{UserName}' を作成しました", userName);
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
}
