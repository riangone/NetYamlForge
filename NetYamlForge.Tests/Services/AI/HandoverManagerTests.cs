using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.BatchJob;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// HandoverManager のテスト
/// </summary>
public class HandoverManagerTests
{
    private readonly Mock<IDbConnectionFactory> _mockDbFactory;
    private readonly Mock<IConversationManager> _mockConversationManager;
    private readonly HandoverManager _handoverManager;

    public HandoverManagerTests()
    {
        _mockDbFactory = new Mock<IDbConnectionFactory>();
        _mockConversationManager = new Mock<IConversationManager>();

        var config = new AiWindowConfig
        {
            Handover = new HandoverConfig
            {
                AutoEnabled = true,
                ConfidenceThreshold = 0.6,
                NegativeSentimentThreshold = -0.5,
                VipAutoHandover = true
            }
        };

        var logger = new LoggerFactory().CreateLogger<HandoverManager>();

        _handoverManager = new HandoverManager(
            _mockDbFactory.Object,
            _mockConversationManager.Object,
            Options.Create(config),
            logger);
    }

    [Fact]
    public async Task EvaluateHandoverNeedAsync_LowConfidence_ReturnsHandoverNeeded()
    {
        // Arrange
        var intentResult = new IntentResult
        {
            Intent = "general_inquiry",
            Confidence = 0.3 // 閾値未満
        };

        // Act
        var evaluation = await _handoverManager.EvaluateHandoverNeedAsync(intentResult);

        // Assert
        Assert.True(evaluation.NeedsHandover);
        Assert.Equal("ai_unable", evaluation.Reason);
    }

    [Fact]
    public async Task EvaluateHandoverNeedAsync_NegativeSentiment_ReturnsHandoverNeeded()
    {
        // Arrange
        var intentResult = new IntentResult
        {
            Intent = "general_inquiry",  // complaint 以外の意図
            Confidence = 0.8,
            SentimentScore = -0.8 // 不満
        };

        // Act
        var evaluation = await _handoverManager.EvaluateHandoverNeedAsync(intentResult);

        // Assert
        Assert.True(evaluation.NeedsHandover);
        Assert.Equal("negative_sentiment", evaluation.Reason);
        Assert.Equal("high", evaluation.Priority);
    }

    [Fact]
    public async Task EvaluateHandoverNeedAsync_ComplaintIntent_ReturnsHandoverNeeded()
    {
        // Arrange
        var intentResult = new IntentResult
        {
            Intent = "complaint",
            Confidence = 0.9
        };

        // Act
        var evaluation = await _handoverManager.EvaluateHandoverNeedAsync(intentResult);

        // Assert
        Assert.True(evaluation.NeedsHandover);
        Assert.Equal("complaint", evaluation.Reason);
        Assert.Equal("quality", evaluation.TargetDepartment);
    }

    [Fact]
    public async Task EvaluateHandoverNeedAsync_HumanAgentIntent_ReturnsHandoverNeeded()
    {
        // Arrange
        var intentResult = new IntentResult
        {
            Intent = "human_agent",
            Confidence = 0.95
        };

        // Act
        var evaluation = await _handoverManager.EvaluateHandoverNeedAsync(intentResult);

        // Assert
        Assert.True(evaluation.NeedsHandover);
        Assert.Equal("customer_request", evaluation.Reason);
    }

    [Fact]
    public async Task EvaluateHandoverNeedAsync_HighConfidence_NoHandoverNeeded()
    {
        // Arrange
        var intentResult = new IntentResult
        {
            Intent = "hours_inquiry",
            Confidence = 0.95,
            SentimentScore = 0.2
        };

        // Act
        var evaluation = await _handoverManager.EvaluateHandoverNeedAsync(intentResult);

        // Assert
        Assert.False(evaluation.NeedsHandover);
    }
}
