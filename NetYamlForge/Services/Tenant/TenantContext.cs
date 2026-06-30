namespace NetYamlForge.Services.Tenant;

public class TenantContext
{
    public string? TenantId { get; set; }
    public string? ConnectionString { get; set; }
    public string Strategy { get; set; } = "physical"; // "logical" or "physical"
}
