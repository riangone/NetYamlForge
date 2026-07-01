namespace NetYamlForge.Models;

public class TenantConfig
{
    public string Strategy { get; set; } = "physical"; // "logical" or "physical"
    public TenantResolverConfig TenantResolver { get; set; } = new();
    
    public string? TenantId { get; set; }
    public string? Name { get; set; }
    public TenantQuotas Quotas { get; set; } = new();
}

public class TenantResolverConfig
{
    public string Source { get; set; } = "header"; // "header", "query", "cookie", "claim"
    public string Key { get; set; } = "X-Tenant-ID";
}

public class TenantQuotas
{
    public int MaxEntitiesCount { get; set; } = 50;
    public int MaxDbRowsPerEntity { get; set; } = 100000;
    public int MaxApiRequestsPerMonth { get; set; } = 500000;
    public long MaxStorageBytes { get; set; } = 5368709120; // 5GB
}
