using System.Security.Claims;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Models.Auth;

namespace NetYamlForge.Services.Tenant;

/// <summary>
/// 多租户用户服务接口
/// </summary>
public interface ITenantUserService
{
    /// <summary>
    /// 验证用户凭据
    /// </summary>
    Task<AppUser?> ValidateCredentialsAsync(string userName, string password);
    
    /// <summary>
    /// 获取用户在指定项目的角色列表
    /// </summary>
    Task<IReadOnlyList<string>> GetProjectRolesAsync(int userId, string projectName);
    
    /// <summary>
    /// 分配项目角色给用户
    /// </summary>
    Task AssignProjectRoleAsync(int userId, string projectName, string roleName, int assignedByUserId, System.Data.IDbConnection? dbConn = null, System.Data.IDbTransaction? transaction = null);
    
    /// <summary>
    /// 获取用户可访问的所有项目
    /// </summary>
    Task<IReadOnlyList<ProjectInfo>> GetAccessibleProjectsAsync(int userId);
    
    /// <summary>
    /// 获取用户在指定项目的访问权限
    /// </summary>
    Task<bool> HasProjectAccessAsync(int userId, string projectName);

    /// <summary>
    /// 获取用户的全局角色列表
    /// </summary>
    Task<IReadOnlyList<string>> GetUserRolesAsync(string userName);

    /// <summary>
    /// 更新最后登录时间
    /// </summary>
    Task UpdateLastLoginAsync(int userId);
    
    /// <summary>
    /// 创建新用户并分配到项目
    /// </summary>
    Task<int> CreateUserWithProjectRoleAsync(CreateUserRequest request);
    
    /// <summary>
    /// 获取用户的详细信息（包含项目角色）
    /// </summary>
    Task<UserDetail?> GetUserDetailAsync(int userId);
}

/// <summary>
/// 创建用户请求
/// </summary>
public class CreateUserRequest
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string UserType { get; set; } = "employee"; // employee, customer, third_party
    public string? DefaultProjectName { get; set; }
    public string? ProjectRole { get; set; }
    public int CreatedByUserId { get; set; }
}

/// <summary>
/// 项目信息
/// </summary>
public class ProjectInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? DefaultRole { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// 用户详细信息
/// </summary>
public class UserDetail
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string UserType { get; set; } = "";
    public string? DefaultProjectName { get; set; }
    public string? OwningProject { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<ProjectRoleInfo> ProjectRoles { get; set; } = new List<ProjectRoleInfo>();
}

/// <summary>
/// 项目角色信息
/// </summary>
public class ProjectRoleInfo
{
    public string ProjectName { get; set; } = "";
    public string ProjectDisplayName { get; set; } = "";
    public string RoleName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
