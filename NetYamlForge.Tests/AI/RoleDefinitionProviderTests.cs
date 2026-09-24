using System.IO;
using NetYamlForge.Services;
using NetYamlForge.Services.AI.AgentStudio;
using Xunit;

namespace NetYamlForge.Tests.AI;

public class RoleDefinitionProviderTests
{
    [Fact]
    public void DefaultRoles_ContainsCoreSystemRoles()
    {
        var provider = new RoleDefinitionProvider();
        var roles = provider.GetRoles();

        Assert.True(roles.ContainsKey("Architect"));
        Assert.True(roles.ContainsKey("Developer"));
        Assert.True(roles.ContainsKey("Tester"));
        Assert.True(roles.ContainsKey("CodeReviewer"));
        Assert.True(roles.ContainsKey("DevOpsEngineer"));

        var arch = provider.GetRole("Architect");
        Assert.NotNull(arch);
        Assert.Contains("high-level structure", arch.Responsibilities[0]);
    }

    [Fact]
    public void RolePromptBuilder_UsesDeclarativeRoleDefinitions()
    {
        var provider = new RoleDefinitionProvider();
        var builder = new RolePromptBuilder(provider);

        var prompt = builder.BuildPrompt(
            "Architect",
            "Design Microservice",
            "Create decoupling specification");

        Assert.Contains("Role: Architect", prompt);
        Assert.Contains("Your Responsibilities as Architect:", prompt);
        Assert.Contains("Design Microservice", prompt);
        Assert.Contains("Create decoupling specification", prompt);
        Assert.Contains("Operational Guidelines:", prompt);
    }

    [Fact]
    public void RolePromptBuilder_CustomRole_RendersProperly()
    {
        var provider = new RoleDefinitionProvider();
        var builder = new RolePromptBuilder(provider);

        var prompt = builder.BuildPrompt(
            "CodeReviewer",
            "Review Security PR",
            "Check for SQL injection and multi-tenant isolation");

        Assert.Contains("Role: CodeReviewer", prompt);
        Assert.Contains("Your Responsibilities as CodeReviewer:", prompt);
        Assert.Contains("security vulnerabilities", prompt);
    }
}
