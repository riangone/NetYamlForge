using Moq;
using NetYamlForge.Services.AI;
using Xunit;

namespace NetYamlForge.Tests.AI;

public class IntentRouterServiceTests
{
    [Fact]
    public void ResolveIntent_ReturnsConfiguredIntent()
    {
        var mockLoader = new Mock<IAiScenarioYamlLoader>();
        var config = new AiScenarioConfig();
        config.Intents["book_test_drive"] = new IntentConfig
        {
            Name = "book_test_drive",
            Description = "预约试驾服务",
            TargetScenario = "test_drive",
            RequiredSlots = new() { "vehicle_model", "preferred_date" },
            AllowedTools = new() { "query_data", "create_appointment_request" }
        };
        config.Scenarios["test_drive"] = new ScenarioConfig
        {
            Name = "test_drive",
            Description = "试驾场景",
            InitialState = "Init",
            Tools = new() { ["Init"] = new() { "query_data" } }
        };

        mockLoader.Setup(l => l.GetConfig("demo-project")).Returns(config);

        var router = new IntentRouterService(mockLoader.Object);

        var intent = router.ResolveIntent("book_test_drive", "demo-project");
        Assert.NotNull(intent);
        Assert.Equal("book_test_drive", intent.Name);
        Assert.Equal("test_drive", intent.TargetScenario);

        var scenario = router.ResolveTargetScenario("book_test_drive", "demo-project");
        Assert.NotNull(scenario);
        Assert.Equal("test_drive", scenario.Name);

        var tools = router.ResolveAllowedTools("book_test_drive", "demo-project");
        Assert.Contains("create_appointment_request", tools);
        Assert.Contains("query_data", tools);
    }

    [Fact]
    public void ResolveIntent_NonExistent_ReturnsNull()
    {
        var mockLoader = new Mock<IAiScenarioYamlLoader>();
        mockLoader.Setup(l => l.GetConfig("demo-project")).Returns(new AiScenarioConfig());

        var router = new IntentRouterService(mockLoader.Object);
        var intent = router.ResolveIntent("unknown_intent", "demo-project");

        Assert.Null(intent);
    }
}
