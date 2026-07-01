using System.Threading.Tasks;

namespace NetYamlForge.Services.Tenant;

public interface ITenantQuotaValidator
{
    Task CheckEntityCreationQuotaAsync(string tenantId);
    Task CheckDatabaseRowsQuotaAsync(string tenantId, string tableName);
    Task CheckStorageQuotaAsync(string tenantId, long incomingFileSizeBytes);
}
