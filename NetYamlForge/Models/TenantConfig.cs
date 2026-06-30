namespace NetYamlForge.Models;

public class TenantConfig
{
    public string Strategy { get; set; } = "physical"; // "logical" or "physical"
    public TenantResolverConfig TenantResolver { get; set; } = new();
}

public class TenantResolverConfig
{
    public string Source { get; set; } = "header"; // "header", "query", "cookie", "claim"
    public string Key { get; set; } = "X-Tenant-ID";
}
