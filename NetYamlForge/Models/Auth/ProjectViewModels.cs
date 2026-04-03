using NetYamlForge.Services.Tenant;

namespace NetYamlForge.Models.Auth;

public class SelectProjectViewModel
{
    public IReadOnlyList<ProjectInfo> Projects { get; set; } = new List<ProjectInfo>();
    public string? ReturnUrl { get; set; }
}

public class AccessDeniedViewModel
{
    public string? ProjectName { get; set; }
    public string Message { get; set; } = "";
}
