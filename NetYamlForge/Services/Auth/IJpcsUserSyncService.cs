using NetYamlForge.Models.Auth;

namespace NetYamlForge.Services.Auth;

public interface IJpcsUserSyncService
{
    Task<SyncResult> SyncUsersAsync();
}

public class SyncResult
{
    public int TotalFound { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
