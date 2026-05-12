using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using NetYamlForge.Models.Auth;

namespace NetYamlForge.Services.Tenant;

/// <summary>
/// 多租户用户服务实现
/// </summary>
public class TenantUserService : ITenantUserService
{
    private readonly string _systemDbConnectionString;
    private readonly ILogger<TenantUserService> _logger;
    private readonly IConfiguration _config;

    public TenantUserService(
        ILogger<TenantUserService> logger,
        IConfiguration config)
    {
        _logger = logger;
        _config = config;

        // 支持通过配置指定系统数据库路径（测试时使用内存数据库）
        var dbPath = config["SystemDbPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "system.db");

        // 如果已经是完整的连接字符串（包含分号），直接使用；否则构建标准连接字符串
        if (dbPath.Contains(';'))
        {
            _systemDbConnectionString = dbPath;
        }
        else
        {
            _systemDbConnectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString;
        }
    }

    /// <summary>
    /// 测试用构造函数 - 直接传入连接字符串
    /// </summary>
    internal TenantUserService(
        ILogger<TenantUserService> logger,
        string connectionString)
    {
        _logger = logger;
        _config = null!;
        _systemDbConnectionString = connectionString;
    }

    private IDbConnection CreateSystemDbConnection()
    {
        // DCS003 抑制理由：システム DB（system.db）はマルチテナント認証のグローバル DB であり、
        // ProjectScope に依存しないため、直接接続します。
#pragma warning disable DCS003
        var conn = new SqliteConnection(_systemDbConnectionString);
        if (conn.State != ConnectionState.Open)
        {
            conn.Open();
        }
        return conn;
#pragma warning restore DCS003
    }

    /// <summary>
    /// 验证用户凭据
    /// </summary>
    public async Task<AppUser?> ValidateCredentialsAsync(string userName, string password)
    {
        const string sql = @"
            SELECT 
                id as Id,
                user_name as UserName,
                password_hash as PasswordHash,
                display_name as DisplayName,
                email as Email,
                phone as Phone,
                user_type as UserType,
                default_project_name as DefaultProjectName,
                is_admin as IsAdmin,
                preferred_language as PreferredLanguage,
                is_active as IsActive,
                created_at as CreatedAt,
                updated_at as UpdatedAt,
                last_login_at as LastLoginAt
            FROM app_user
            WHERE user_name = @UserName AND is_active = 1
        ";

        using var db = CreateSystemDbConnection();
        var user = await db.QueryFirstOrDefaultAsync<AppUser>(sql, new { UserName = userName });

        if (user == null)
        {
            _logger.LogInformation("用户 {UserName} 不存在或已禁用", userName);
            return null;
        }

        // 验证密码哈希
        if (!VerifyPasswordHash(password, user.PasswordHash))
        {
            _logger.LogInformation("用户 {UserName} 密码验证失败", userName);
            return null;
        }

        _logger.LogInformation("用户 {UserName} 验证成功", userName);
        return user;
    }

    /// <summary>
    /// 获取用户在指定项目的角色列表
    /// </summary>
    public async Task<IReadOnlyList<string>> GetProjectRolesAsync(int userId, string projectName)
    {
        const string sql = @"
            SELECT role_name
            FROM app_user_project_role
            WHERE user_id = @UserId AND project_name = @ProjectName
        ";

        using var db = CreateSystemDbConnection();
        var roles = await db.QueryAsync<string>(sql, new { UserId = userId, ProjectName = projectName });
        return roles.ToList().AsReadOnly();
    }

    /// <summary>
    /// 分配项目角色给用户
    /// </summary>
    public async Task AssignProjectRoleAsync(int userId, string projectName, string roleName, int assignedByUserId)
    {
        const string sql = @"
            INSERT INTO app_user_project_role (user_id, project_name, role_name, assigned_by, created_at)
            VALUES (@UserId, @ProjectName, @RoleName, @AssignedBy, @CreatedAt)
            ON CONFLICT (user_id, project_name) DO UPDATE SET
                role_name = excluded.role_name,
                assigned_by = excluded.assigned_by
        ";

        using var db = CreateSystemDbConnection();
        await db.ExecuteAsync(sql, new
        {
            UserId = userId,
            ProjectName = projectName,
            RoleName = roleName,
            AssignedBy = assignedByUserId,
            CreatedAt = DateTime.UtcNow
        });

        _logger.LogInformation("用户 {UserId} 被分配角色 {RoleName} 到项目 {ProjectName}", userId, roleName, projectName);
    }

    /// <summary>
    /// 获取用户可访问的所有项目
    /// </summary>
    public async Task<IReadOnlyList<ProjectInfo>> GetAccessibleProjectsAsync(int userId)
    {
        const string sql = @"
            SELECT
                pr.project_name as Name,
                p.display_name as DisplayName,
                pr.role_name as DefaultRole,
                CASE WHEN u.default_project_name = pr.project_name THEN 1 ELSE 0 END as IsDefault
            FROM app_user_project_role pr
            LEFT JOIN projects p ON pr.project_name = p.name
            LEFT JOIN app_user u ON u.id = @UserId
            WHERE pr.user_id = @UserId
            ORDER BY IsDefault DESC, pr.project_name
        ";

        using var db = CreateSystemDbConnection();
        var projects = await db.QueryAsync<ProjectInfo>(sql, new { UserId = userId });
        return projects.ToList().AsReadOnly();
    }

    /// <summary>
    /// 检查用户是否有指定项目的访问权限
    /// </summary>
    public async Task<bool> HasProjectAccessAsync(int userId, string projectName)
    {
        const string sql = @"
            SELECT COUNT(1)
            FROM app_user_project_role
            WHERE user_id = @UserId AND project_name = @ProjectName
        ";

        using var db = CreateSystemDbConnection();
        var count = await db.ExecuteScalarAsync<int>(sql, new { UserId = userId, ProjectName = projectName });
        return count > 0;
    }

    /// <summary>
    /// 获取用户的全局角色列表
    /// </summary>
    public async Task<IReadOnlyList<string>> GetUserRolesAsync(string userName)
    {
        // 先获取全局角色
        const string sqlGlobal = @"
            SELECT role_name 
            FROM app_user_role 
            WHERE user_name = @UserName
        ";

        using var db = CreateSystemDbConnection();
        var roles = (await db.QueryAsync<string>(sqlGlobal, new { UserName = userName })).ToList();

        // 再获取该用户在所有项目中的角色（作为补充）
        const string sqlProject = @"
            SELECT DISTINCT role_name 
            FROM app_user_project_role r
            JOIN app_user u ON r.user_id = u.id
            WHERE u.user_name = @UserName
        ";
        var projectRoles = await db.QueryAsync<string>(sqlProject, new { UserName = userName });
        
        foreach (var role in projectRoles)
        {
            if (!roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                roles.Add(role);
            }
        }

        return roles.AsReadOnly();
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        const string sql = @"
            UPDATE app_user 
            SET last_login_at = @Now 
            WHERE id = @Id
        ";

        using var db = CreateSystemDbConnection();
        await db.ExecuteAsync(sql, new { Id = userId, Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
    }

    /// <summary>
    /// 创建新用户并分配到项目
    /// </summary>
    public async Task<int> CreateUserWithProjectRoleAsync(CreateUserRequest request)
    {
        using var db = CreateSystemDbConnection();
        using var transaction = db.BeginTransaction();

        try
        {
            // 1. 创建全局用户
            const string insertUserSql = @"
                INSERT INTO app_user (user_name, password_hash, display_name, email, phone,
                                      user_type, default_project_name, is_active, created_at, updated_at)
                VALUES (@UserName, @PasswordHash, @DisplayName, @Email, @Phone,
                        @UserType, @DefaultProjectName, 1, @CreatedAt, @UpdatedAt)
                RETURNING id
            ";

            var passwordHash = HashPassword(request.Password);

            var userId = await db.ExecuteScalarAsync<int>(insertUserSql, new
            {
                UserName = request.UserName,
                PasswordHash = passwordHash,
                DisplayName = request.DisplayName,
                Email = request.Email,
                Phone = request.Phone,
                UserType = request.UserType,
                DefaultProjectName = request.DefaultProjectName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, transaction);

            // 2. 如果指定了项目，分配角色
            if (!string.IsNullOrEmpty(request.DefaultProjectName) && !string.IsNullOrEmpty(request.ProjectRole))
            {
                await AssignProjectRoleAsync(userId, request.DefaultProjectName, request.ProjectRole, request.CreatedByUserId);
            }

            transaction.Commit();

            _logger.LogInformation("创建新用户 {UserName} (ID={UserId})", request.UserName, userId);

            return userId;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "创建用户失败：{UserName}", request.UserName);
            throw;
        }
    }

    /// <summary>
    /// 获取用户的详细信息（包含项目角色）
    /// </summary>
    public async Task<UserDetail?> GetUserDetailAsync(int userId)
    {
        const string userSql = @"
            SELECT id, user_name, display_name, email, phone, user_type,
                   default_project_name, is_active, created_at
            FROM app_user
            WHERE id = @UserId
        ";

        using var db = CreateSystemDbConnection();
        var user = await db.QueryFirstOrDefaultAsync<UserDetail>(userSql, new { UserId = userId });

        if (user == null)
        {
            return null;
        }

        // 获取项目角色
        const string rolesSql = @"
            SELECT
                pr.project_name as ProjectName,
                p.display_name as ProjectDisplayName,
                pr.role_name as RoleName,
                pr.created_at as CreatedAt
            FROM app_user_project_role pr
            LEFT JOIN projects p ON pr.project_name = p.name
            WHERE pr.user_id = @UserId
            ORDER BY pr.created_at DESC
        ";

        var roles = await db.QueryAsync<ProjectRoleInfo>(rolesSql, new { UserId = userId });
        user.ProjectRoles = roles.ToList().AsReadOnly();

        return user;
    }

    #region Helper Methods

    /// <summary>
    /// 哈希密码（使用 ASP.NET Core PasswordHasher）
    /// </summary>
    private static string HashPassword(string password)
    {
        var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
        return passwordHasher.HashPassword(null!, password);
    }

    /// <summary>
    /// 验证密码哈希
    /// </summary>
    private static bool VerifyPasswordHash(string password, string storedHash)
    {
        var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
        var result = passwordHasher.VerifyHashedPassword(null!, storedHash, password);
        return result == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success;
    }

    #endregion
}
