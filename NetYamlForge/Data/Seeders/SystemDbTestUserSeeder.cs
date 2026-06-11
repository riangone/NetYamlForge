// ファイル概要: 全テストユーザーを system.db（マルチテナント認証 DB）に同期するシーダーです。
// プロジェクト DB の AppUser テーブルからユーザーを読み取り、system.db の app_user と app_user_project_role に登録します。

#pragma warning disable DCS003

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace NetYamlForge.Data.Seeders;

/// <summary>
/// テストユーザー system.db 同期シーダー。
/// </summary>
public class SystemDbTestUserSeeder
{
    /// <summary>
    /// プロジェクト DB からテストユーザーを読み取り、system.db に同期します。
    /// </summary>
    public async Task SyncTestUsersToSystemDbAsync(
        string systemDbPath,
        IEnumerable<ProjectUserInfo> projects,
        ILogger logger)
    {
        var systemConnStr = new SqliteConnectionStringBuilder { DataSource = systemDbPath }.ConnectionString;
        var totalSynced = 0;

        await using var systemConn = new SqliteConnection(systemConnStr);
        await systemConn.OpenAsync();
        await NetYamlForge.Services.Connection.SqliteConnectionHardening.ApplyAsync(systemConn);

        // 1. プロジェクト一覧を system.db に登録
        foreach (var project in projects)
        {
            await systemConn.ExecuteAsync(
                @"INSERT OR IGNORE INTO projects (name, display_name, created_at) VALUES (@Name, @DisplayName, @CreatedAt)",
                new { project.Name, project.DisplayName, CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        // 2. 各プロジェクト DB からユーザーを同期
        foreach (var project in projects)
        {
            if (!File.Exists(project.DbPath))
            {
                logger.LogDebug("プロジェクト DB が見つかりません: {DbPath}", project.DbPath);
                continue;
            }

            var projectConnStr = new SqliteConnectionStringBuilder { DataSource = project.DbPath }.ConnectionString;
            await using var projectConn = new SqliteConnection(projectConnStr);
            await projectConn.OpenAsync();
            await NetYamlForge.Services.Connection.SqliteConnectionHardening.ApplyAsync(projectConn);

            // AppUser テーブルからユーザーを取得
            var users = await projectConn.QueryAsync<dynamic>(
                "SELECT Id, UserName, DisplayName, PasswordHash, PreferredLanguage, IsAdmin, IsActive, CreatedAt FROM AppUser");

            foreach (var user in users)
            {
                // system.db にユーザーが存在するか確認
                var existingUser = await systemConn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT id, owning_project FROM app_user WHERE user_name = @UserName",
                    new { UserName = (string)user.UserName });

                if (existingUser != null)
                {
                    // 既存ユーザーのプロジェクトロールのみ更新
                    await EnsureProjectRoleAsync(systemConn, (int)existingUser.id, project.Name, logger);
                    
                    // owning_project が NULL の場合は、このプロジェクトをセットして回填
                    if (existingUser.owning_project == null)
                    {
                        await systemConn.ExecuteAsync(
                            "UPDATE app_user SET owning_project = @OwningProject WHERE id = @Id",
                            new { OwningProject = project.Name, Id = (int)existingUser.id });
                    }
                    continue;
                }

                // system.db にユーザーを作成
                // プロジェクト DB の PasswordHash（既に ASP.NET PasswordHasher 形式）をそのまま使用
                var passwordHash = (string)user.PasswordHash;

                var isActive = Convert.ToInt32(user.IsActive) != 0;
                var isAdmin = Convert.ToInt32(user.IsAdmin) != 0;

                var userId = await systemConn.ExecuteScalarAsync<int>(
                    @"INSERT INTO app_user (user_name, password_hash, display_name, user_type, default_project_name, owning_project, is_admin, preferred_language, is_active, created_at, updated_at)
                      VALUES (@UserName, @PasswordHash, @DisplayName, @UserType, @DefaultProjectName, @OwningProject, @IsAdmin, @PreferredLanguage, @IsActive, @CreatedAt, @UpdatedAt)
                      RETURNING id",
                    new
                    {
                        UserName = (string)user.UserName,
                        PasswordHash = passwordHash,
                        DisplayName = (string)user.DisplayName,
                        UserType = isAdmin ? "admin" : "user",
                        DefaultProjectName = project.Name,
                        OwningProject = project.Name, // 设置归属项目
                        IsAdmin = isAdmin ? 1 : 0,
                        PreferredLanguage = (string)(user.PreferredLanguage ?? "ja-JP"),
                        IsActive = isActive ? 1 : 0,
                        CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                    });

                // プロジェクトロールを割り当て
                var role = isAdmin ? "admin" : GetUserRoleFromProject(project.Name, user.UserName);
                await AssignProjectRoleAsync(systemConn, userId, project.Name, role, logger);

                totalSynced++;
                logger.LogDebug("ユーザー {UserName} を system.db に同期しました (プロジェクト: {Project})", (string)user.UserName, project.Name);
            }
        }

        logger.LogInformation("system.db へのテストユーザー同期完了: {Count} 名", totalSynced);
    }

    /// <summary>
    /// プロジェクトロールを確保します。
    /// </summary>
    private async Task EnsureProjectRoleAsync(IDbConnection conn, int userId, string projectName, ILogger logger)
    {
        var existingRole = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM app_user_project_role WHERE user_id = @UserId AND project_name = @ProjectName",
            new { UserId = userId, ProjectName = projectName });

        if (existingRole == 0)
        {
            // デフォルトロールを割り当て
            await AssignProjectRoleAsync(conn, userId, projectName, "user", logger);
        }
    }

    /// <summary>
    /// プロジェクトロールを割り当てます。
    /// </summary>
    private async Task AssignProjectRoleAsync(IDbConnection conn, int userId, string projectName, string role, ILogger logger)
    {
        await conn.ExecuteAsync(
            @"INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at)
              VALUES (@UserId, @ProjectName, @RoleName, 1, @CreatedAt)",
            new
            {
                UserId = userId,
                ProjectName = projectName,
                RoleName = role,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });

        logger.LogDebug("ユーザー {UserId} にプロジェクト {Project} のロール {Role} を割り当てました", userId, projectName, role);
    }

    /// <summary>
    /// ユーザー名からロールを推測します。
    /// </summary>
    private static string GetUserRoleFromProject(string projectName, string userName)
    {
        if (userName.Contains("admin")) return "admin";
        if (userName.Contains("operator")) return "operator";
        if (userName.Contains("sales")) return "sales_rep";
        if (userName.Contains("manager")) return "sales_manager";
        if (userName.Contains("service")) return "service_staff";
        if (userName.Contains("customer")) return "customer";
        if (userName.Contains("executive")) return "executive";
        if (userName.Contains("ai_admin")) return "ai_admin";
        if (userName.Contains("vendor")) return "vendor";
        if (userName.Contains("logistics")) return "logistics";
        if (userName.Contains("finance")) return "finance";
        if (userName.Contains("insurance")) return "insurance";
        return "user";
    }

    /// <summary>
    /// system.db 用のパスワードハッシュ（ASP.NET Core Identity PasswordHasher 互換）を生成します。
    /// </summary>
    private static string HashPasswordForSystemDb(string password)
    {
        var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
        return passwordHasher.HashPassword(null!, password);
    }
}

/// <summary>
/// プロジェクト情報（system.db 同期用）
/// </summary>
public class ProjectUserInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DbPath { get; set; } = "";
}
