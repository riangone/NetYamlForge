using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services.Connection;
using Microsoft.AspNetCore.Identity;

namespace NetYamlForge.Services.Auth;

public class JpcsUserSyncService : IJpcsUserSyncService
{
    private readonly IConnectionManager _connectionManager;
    private readonly IUserAuthService _userAuthService;
    private readonly ILogger<JpcsUserSyncService> _logger;
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    public JpcsUserSyncService(
        IConnectionManager connectionManager,
        IUserAuthService userAuthService,
        ILogger<JpcsUserSyncService> logger)
    {
        _connectionManager = connectionManager;
        _userAuthService = userAuthService;
        _logger = logger;
    }

    public async Task<SyncResult> SyncUsersAsync()
    {
        var result = new SyncResult();
        var jpcsDbPath = "NetYamlForge/projects/jpcs/database/jpcs.db";
        
        if (!File.Exists(jpcsDbPath))
        {
            result.Errors.Add($"JPCS database not found at {jpcsDbPath}");
            return result;
        }

        try
        {
#pragma warning disable DCS003
            using var jpcsConn = new SqliteConnection($"Data Source={jpcsDbPath}");
#pragma warning restore DCS003
            await jpcsConn.OpenAsync();

            // 1. Get active employees from jpcs.db
            var employees = (await jpcsConn.QueryAsync<JpcsEmployee>(
                "SELECT ad_user_id, name, email, password FROM ad_user WHERE isactive = 'Y'")).ToList();

            result.TotalFound = employees.Count;
            _logger.LogInformation("Found {Count} active employees in JPCS", employees.Count);

            // 2. Sync to system.db
            foreach (var emp in employees)
            {
                try
                {
                    await SyncEmployeeAsync(emp, result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing employee {Name}", emp.Name);
                    result.Errors.Add($"Error syncing {emp.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync JPCS users");
            result.Errors.Add($"General error: {ex.Message}");
        }

        return result;
    }

    private async Task SyncEmployeeAsync(JpcsEmployee emp, SyncResult result)
    {
        // Generate a clean user name
        var userName = emp.Name.Replace(" ", "").ToLowerInvariant();
        if (string.IsNullOrEmpty(userName)) return;

        // Use direct connection to system.db (central auth db)
        var systemDbPath = Path.Combine(Directory.GetCurrentDirectory(), "system.db");
#pragma warning disable DCS003
        using var systemConn = new SqliteConnection($"Data Source={systemDbPath}");
#pragma warning restore DCS003
        await systemConn.OpenAsync();

        var existing = await systemConn.QueryFirstOrDefaultAsync<AppUser>(
            "SELECT * FROM app_user WHERE (external_id = @ExternalId AND external_source = 'jpcs') OR user_name = @UserName",
            new { ExternalId = emp.Ad_User_Id.ToString(), UserName = userName });

        if (existing == null)
        {
            // Create new user
            var userViewModel = new UserEditViewModel
            {
                UserName = userName,
                DisplayName = emp.Name,
                Email = emp.Email,
                IsActive = true,
                IsAdmin = false,
                PreferredLanguage = "ja-JP",
                Password = "ChangeMe123!", // Default password
                ExternalId = emp.Ad_User_Id.ToString(),
                ExternalSource = "jpcs",
                OwningProject = "jpcs" // 隔离标记：该用户归属于 jpcs 项目
            };

            var userId = await _userAuthService.CreateAsync(userViewModel);
            
            // Assign project role
            await systemConn.ExecuteAsync(
                "INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, created_at) VALUES (@UserId, 'jpcs', 'user', @Now)",
                new { UserId = userId, Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

            result.CreatedCount++;
            _logger.LogInformation("Created system user {UserName} for JPCS employee {Name} (OwningProject: jpcs)", userName, emp.Name);
        }
        else
        {
            // Update existing user if needed
            // Ensure OwningProject is set to jpcs for isolation
            if (existing.OwningProject != "jpcs")
            {
                await systemConn.ExecuteAsync(
                    "UPDATE app_user SET owning_project = 'jpcs' WHERE id = @Id",
                    new { Id = existing.Id });
            }

            // Ensure project role exists
            await systemConn.ExecuteAsync(
                "INSERT OR IGNORE INTO app_user_project_role (user_id, project_name, role_name, created_at) VALUES (@UserId, 'jpcs', 'user', @Now)",
                new { UserId = existing.Id, Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });
            
            // Update external info if it was missing
            if (string.IsNullOrEmpty(existing.ExternalId))
            {
                await systemConn.ExecuteAsync(
                    "UPDATE app_user SET external_id = @ExternalId, external_source = 'jpcs' WHERE id = @Id",
                    new { ExternalId = emp.Ad_User_Id.ToString(), Id = existing.Id });
            }

            result.UpdatedCount++;
            _logger.LogInformation("User {UserName} already exists, ensured JPCS project role and OwningProject=jpcs", userName);
        }
    }

    private class JpcsEmployee
    {
        public decimal Ad_User_Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Password { get; set; }
    }
}
