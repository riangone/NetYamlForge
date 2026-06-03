using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Services.Tenant;
using Xunit;
using Microsoft.Data.Sqlite;

namespace NetYamlForge.Tests.Services.Tenant;

/// <summary>
/// 多租户用户服务测试
/// </summary>
public class TenantUserServiceTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly TenantUserService _service;
    private readonly Mock<ILogger<TenantUserService>> _loggerMock;
    private readonly string _testDbConnectionString;
    private readonly string _tempDbFile;

    public TenantUserServiceTests()
    {
        // 设置 Dapper 映射规则
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        // 使用临时文件数据库进行测试
        _tempDbFile = Path.GetTempFileName();
        _testDbConnectionString = $"Data Source={_tempDbFile};";

#pragma warning disable DCS003
        _db = new SqliteConnection(_testDbConnectionString);
#pragma warning restore DCS003
        _db.Open();

        // 初始化数据库表
        InitializeDatabase();

        // 创建 Mock 对象
        _loggerMock = new Mock<ILogger<TenantUserService>>();

        // 使用内部构造函数直接传入连接字符串进行测试
        _service = new TenantUserService(_loggerMock.Object, _testDbConnectionString);
    }

    private void InitializeDatabase()
    {
        // 创建 app_user 表
        _db.Execute(@"
            CREATE TABLE app_user (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_name TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                display_name TEXT NOT NULL,
                email TEXT,
                phone TEXT,
                user_type TEXT NOT NULL DEFAULT 'employee',
                default_project_name TEXT,
                owning_project TEXT,
                is_admin INTEGER NOT NULL DEFAULT 0,
                preferred_language TEXT,
                last_login_at TEXT,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            )
        ");
        
        // 创建 app_user_project_role 表
        _db.Execute(@"
            CREATE TABLE app_user_project_role (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                project_name TEXT NOT NULL,
                role_name TEXT NOT NULL,
                permission_scope TEXT,
                assigned_by INTEGER,
                created_at TEXT NOT NULL,
                UNIQUE(user_id, project_name)
            )
        ");
        
        // 创建 projects 表（用于关联查询）
        _db.Execute(@"
            CREATE TABLE projects (
                name TEXT PRIMARY KEY,
                display_name TEXT
            )
        ");
        
        // 插入测试数据
        _db.Execute(@"
            INSERT INTO projects (name, display_name) VALUES 
            ('auto-dealer-demo', '汽车销售演示'),
            ('inventory', '库存管理'),
            ('service-center', '服务中心')
        ");
    }

    #region ValidateCredentialsAsync 测试

    [Fact]
    public async Task ValidateCredentialsAsync_ValidCredentials_ReturnsUser()
    {
        // Arrange
        var userName = "testuser";
        var password = "TestPass123";
        var passwordHash = HashPassword(password);
        
        _db.Execute(@"
            INSERT INTO app_user (user_name, password_hash, display_name, email, user_type, is_active, created_at, updated_at)
            VALUES (@UserName, @PasswordHash, @DisplayName, @Email, @UserType, 1, datetime('now'), datetime('now'))
        ", new { UserName = userName, PasswordHash = passwordHash, DisplayName = "测试用户", Email = "test@example.com", UserType = "employee" });
        
        // Act
        var result = await _service.ValidateCredentialsAsync(userName, password);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(userName, result.UserName);
        Assert.Equal("测试用户", result.DisplayName);
        Assert.Equal("employee", result.UserType);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_InvalidPassword_ReturnsNull()
    {
        // Arrange
        var userName = "testuser";
        var password = "TestPass123";
        var wrongPassword = "WrongPass456";
        var passwordHash = HashPassword(password);
        
        _db.Execute(@"
            INSERT INTO app_user (user_name, password_hash, display_name, email, user_type, is_active, created_at, updated_at)
            VALUES (@UserName, @PasswordHash, @DisplayName, @Email, @UserType, 1, datetime('now'), datetime('now'))
        ", new { UserName = userName, PasswordHash = passwordHash, DisplayName = "测试用户", Email = "test@example.com", UserType = "employee" });
        
        // Act
        var result = await _service.ValidateCredentialsAsync(userName, wrongPassword);
        
        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_InactiveUser_ReturnsNull()
    {
        // Arrange
        var userName = "inactiveuser";
        var password = "TestPass123";
        var passwordHash = HashPassword(password);
        
        _db.Execute(@"
            INSERT INTO app_user (user_name, password_hash, display_name, email, user_type, is_active, created_at, updated_at)
            VALUES (@UserName, @PasswordHash, @DisplayName, @Email, @UserType, 0, datetime('now'), datetime('now'))
        ", new { UserName = userName, PasswordHash = passwordHash, DisplayName = "禁用用户", Email = "test@example.com", UserType = "employee" });
        
        // Act
        var result = await _service.ValidateCredentialsAsync(userName, password);
        
        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_NonExistentUser_ReturnsNull()
    {
        // Act
        var result = await _service.ValidateCredentialsAsync("nonexistent", "TestPass123");
        
        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetProjectRolesAsync 测试

    [Fact]
    public async Task GetProjectRolesAsync_UserHasRoles_ReturnsRoles()
    {
        // Arrange
        var userId = 1;
        var projectName = "auto-dealer-demo";
        
        _db.Execute(@"
            INSERT INTO app_user_project_role (user_id, project_name, role_name, created_at)
            VALUES (@UserId, @ProjectName, @RoleName, datetime('now'))
        ", new { UserId = userId, ProjectName = projectName, RoleName = "sales_rep" });
        
        // Act
        var result = await _service.GetProjectRolesAsync(userId, projectName);
        
        // Assert
        Assert.Single(result);
        Assert.Contains("sales_rep", result);
    }

    [Fact]
    public async Task GetProjectRolesAsync_UserHasNoRoles_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetProjectRolesAsync(999, "auto-dealer-demo");
        
        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region HasProjectAccessAsync 测试

    [Fact]
    public async Task HasProjectAccessAsync_UserHasAccess_ReturnsTrue()
    {
        // Arrange
        var userId = 1;
        var projectName = "auto-dealer-demo";
        
        _db.Execute(@"
            INSERT INTO app_user_project_role (user_id, project_name, role_name, created_at)
            VALUES (@UserId, @ProjectName, @RoleName, datetime('now'))
        ", new { UserId = userId, ProjectName = projectName, RoleName = "sales_rep" });
        
        // Act
        var result = await _service.HasProjectAccessAsync(userId, projectName);
        
        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasProjectAccessAsync_UserHasNoAccess_ReturnsFalse()
    {
        // Arrange
        var userId = 1;
        _db.Execute(@"
            INSERT INTO app_user_project_role (user_id, project_name, role_name, created_at)
            VALUES (@UserId, 'inventory', 'sales_rep', datetime('now'))
        ", new { UserId = userId });

        // Act
        var result = await _service.HasProjectAccessAsync(userId, "auto-dealer-demo");
        
        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetAccessibleProjectsAsync 测试

    [Fact]
    public async Task GetAccessibleProjectsAsync_UserHasProjects_ReturnsProjects()
    {
        // Arrange
        var userId = 1;
        
        _db.Execute(@"
            INSERT INTO app_user_project_role (user_id, project_name, role_name, created_at)
            VALUES (@UserId, @ProjectName, @RoleName, datetime('now'))
        ", new { UserId = userId, ProjectName = "auto-dealer-demo", RoleName = "sales_rep" });
        
        _db.Execute(@"
            INSERT INTO app_user_project_role (user_id, project_name, role_name, created_at)
            VALUES (@UserId, @ProjectName, @RoleName, datetime('now'))
        ", new { UserId = userId, ProjectName = "inventory", RoleName = "viewer" });
        
        // Act
        var result = await _service.GetAccessibleProjectsAsync(userId);
        
        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "auto-dealer-demo");
        Assert.Contains(result, p => p.Name == "inventory");
    }

    #endregion

    #region AssignProjectRoleAsync 测试

    [Fact]
    public async Task AssignProjectRoleAsync_NewAssignment_InsertsRecord()
    {
        // Arrange
        var userId = 1;
        var projectName = "auto-dealer-demo";
        var roleName = "sales_rep";
        var assignedBy = 999;
        
        // Act
        await _service.AssignProjectRoleAsync(userId, projectName, roleName, assignedBy);
        
        // Assert
        var result = _db.QueryFirstOrDefault(@"
            SELECT * FROM app_user_project_role WHERE user_id = @UserId AND project_name = @ProjectName
        ", new { UserId = userId, ProjectName = projectName });
        
        Assert.NotNull(result);
        Assert.Equal(roleName, result.role_name);
        Assert.Equal(assignedBy, result.assigned_by);
    }

    [Fact]
    public async Task AssignProjectRoleAsync_ExistingAssignment_UpdatesRecord()
    {
        // Arrange
        var userId = 1;
        var projectName = "auto-dealer-demo";
        var initialRole = "viewer";
        var newRole = "sales_rep";
        var assignedBy = 999;
        
        _db.Execute(@"
            INSERT INTO app_user_project_role (user_id, project_name, role_name, created_at)
            VALUES (@UserId, @ProjectName, @RoleName, datetime('now'))
        ", new { UserId = userId, ProjectName = projectName, RoleName = initialRole });
        
        // Act
        await _service.AssignProjectRoleAsync(userId, projectName, newRole, assignedBy);
        
        // Assert
        var result = _db.QueryFirstOrDefault(@"
            SELECT role_name FROM app_user_project_role WHERE user_id = @UserId AND project_name = @ProjectName
        ", new { UserId = userId, ProjectName = projectName });
        
        Assert.NotNull(result);
        Assert.Equal(newRole, result.role_name);
    }

    #endregion

    #region CreateUserWithProjectRoleAsync 测试

    [Fact]
    public async Task CreateUserWithProjectRoleAsync_ValidRequest_CreatesUserAndRole()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            UserName = "newuser",
            Password = "SecurePass123",
            DisplayName = "新用户",
            Email = "newuser@example.com",
            Phone = "13800138000",
            UserType = "employee",
            DefaultProjectName = "auto-dealer-demo",
            ProjectRole = "sales_rep",
            CreatedByUserId = 999
        };
        
        // Act
        var userId = await _service.CreateUserWithProjectRoleAsync(request);
        
        // Assert
        Assert.True(userId > 0);
        
        // 验证用户已创建
        var user = _db.QueryFirstOrDefault(@"
            SELECT * FROM app_user WHERE id = @Id
        ", new { Id = userId });
        
        Assert.NotNull(user);
        Assert.Equal(request.UserName, user.user_name);
        Assert.Equal(request.DisplayName, user.display_name);
        
        // 验证项目角色已分配
        var role = _db.QueryFirstOrDefault(@"
            SELECT * FROM app_user_project_role WHERE user_id = @UserId
        ", new { UserId = userId });
        
        Assert.NotNull(role);
        Assert.Equal(request.ProjectRole, role.role_name);
    }

    #endregion

    #region GetUserDetailAsync 测试

    [Fact]
    public async Task GetUserDetailAsync_UserExists_ReturnsUserDetail()
    {
        // Arrange
        var userId = 1;
        
        _db.Execute(@"
            INSERT INTO app_user (id, user_name, password_hash, display_name, email, user_type, is_active, created_at, updated_at)
            VALUES (@Id, @UserName, @PasswordHash, @DisplayName, @Email, @UserType, 1, datetime('now'), datetime('now'))
        ", new { Id = userId, UserName = "testuser", PasswordHash = "hash", DisplayName = "测试用户", Email = "test@example.com", UserType = "employee" });
        
        _db.Execute(@"
            INSERT INTO app_user_project_role (user_id, project_name, role_name, created_at)
            VALUES (@UserId, @ProjectName, @RoleName, datetime('now'))
        ", new { UserId = userId, ProjectName = "auto-dealer-demo", RoleName = "sales_rep" });
        
        // Act
        var result = await _service.GetUserDetailAsync(userId);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.Id);
        Assert.Equal("testuser", result.UserName);
        Assert.Equal("测试用户", result.DisplayName);
        Assert.Single(result.ProjectRoles);
        Assert.Equal("auto-dealer-demo", result.ProjectRoles[0].ProjectName);
    }

    [Fact]
    public async Task GetUserDetailAsync_UserNotExists_ReturnsNull()
    {
        // Act
        var result = await _service.GetUserDetailAsync(999);
        
        // Assert
        Assert.Null(result);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _db?.Close();
        _db?.Dispose();
        
        // 删除临时数据库文件
        if (!string.IsNullOrEmpty(_tempDbFile) && File.Exists(_tempDbFile))
        {
            try
            {
                File.Delete(_tempDbFile);
            }
            catch
            {
                // 忽略删除失败
            }
        }
    }

    #endregion

    #region Helper Methods

    private static string HashPassword(string password)
    {
        var passwordHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
        return passwordHasher.HashPassword(null!, password);
    }

    #endregion
}
