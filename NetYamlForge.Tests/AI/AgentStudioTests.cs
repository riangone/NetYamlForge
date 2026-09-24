using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.AgentStudio;
using Xunit;

namespace NetYamlForge.Tests.AI;

public class AgentStudioTests
{
    [Fact]
    public void RandomRoleBinder_AllocatesAllThreeRolesUniquely()
    {
        var binder = new RandomRoleBinder();
        var allocation = binder.Shuffle();

        Assert.Equal(RandomRoleBinder.RoleArchitect, allocation.Architect.Role);
        Assert.Equal(RandomRoleBinder.RoleDeveloper, allocation.Developer.Role);
        Assert.Equal(RandomRoleBinder.RoleTester, allocation.Tester.Role);

        // Providers must be distinct
        var providers = new[] { allocation.Architect.Provider, allocation.Developer.Provider, allocation.Tester.Provider };
        Assert.Equal(3, providers.Distinct().Count());

        // Providers must be in default set
        foreach (var p in providers)
        {
            Assert.Contains(p, RandomRoleBinder.DefaultProviders);
        }
    }

    [Fact]
    public void RolePromptBuilder_BuildsRoleSpecificPrompt()
    {
        var builder = new RolePromptBuilder();

        var archPrompt = builder.BuildPrompt(
            RandomRoleBinder.RoleArchitect,
            "Add Payment Gateway",
            "Support Stripe payments");

        Assert.Contains("Role: Architect", archPrompt);
        Assert.Contains("Add Payment Gateway", archPrompt);
        Assert.Contains("Support Stripe payments", archPrompt);
        Assert.Contains("Specification", archPrompt);

        var devPrompt = builder.BuildPrompt(
            RandomRoleBinder.RoleDeveloper,
            "Add Payment Gateway",
            "Support Stripe payments",
            priorContext: "RFC-001: Stripe API design");

        Assert.Contains("Role: Developer", devPrompt);
        Assert.Contains("RFC-001: Stripe API design", devPrompt);
    }

    [Fact]
    public async Task AgentOrchestrationService_RunsPipelineSequentially()
    {
        var binderMock = new Mock<IRandomRoleBinder>();
        binderMock.Setup(b => b.Shuffle(It.IsAny<IReadOnlyList<string>>()))
            .Returns(new AgentTaskAllocation(
                new RoleAssignment("Architect", "claude"),
                new RoleAssignment("Developer", "antigravity"),
                new RoleAssignment("Tester", "opencode")));

        var promptBuilder = new RolePromptBuilder();
        var cliChainMock = new Mock<ICliChainService>();

        cliChainMock.Setup(c => c.PromptAsync(
                It.Is<string>(p => p.Contains("Role: Architect")),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "claude",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliChainResult(true, "Spec: Add OAuth", "claude", null));

        cliChainMock.Setup(c => c.PromptAsync(
                It.Is<string>(p => p.Contains("Role: Developer")),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "antigravity",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliChainResult(true, "Code: AuthController.cs created", "antigravity", null));

        cliChainMock.Setup(c => c.PromptAsync(
                It.Is<string>(p => p.Contains("Role: Tester")),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "opencode",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CliChainResult(true, "Tests: 5 passed", "opencode", null));

        var orchestrator = new AgentOrchestrationService(
            binderMock.Object,
            promptBuilder,
            cliChainMock.Object,
            NullLogger<AgentOrchestrationService>.Instance);

        var result = await orchestrator.ExecutePipelineAsync(
            new AgentTaskExecutionRequest("Implement OAuth", "Add OAuth2 authentication"));

        Assert.True(result.Success);
        Assert.Equal("Spec: Add OAuth", result.ArchitectOutput);
        Assert.Equal("Code: AuthController.cs created", result.DeveloperOutput);
        Assert.Equal("Tests: 5 passed", result.TesterOutput);
        Assert.Equal(3, result.Logs.Count);
    }
}
