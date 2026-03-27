using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// SentimentAnalyzer のテスト
/// </summary>
public class SentimentAnalyzerTests
{
    private readonly SentimentAnalyzer _analyzer;

    public SentimentAnalyzerTests()
    {
        var logger = new LoggerFactory().CreateLogger<SentimentAnalyzer>();
        _analyzer = new SentimentAnalyzer(logger);
    }

    [Fact]
    public async Task AnalyzeAsync_PositiveText_ReturnsPositiveScore()
    {
        // Arrange
        var text = "とても素晴らしいサービスです";

        // Act
        var result = await _analyzer.AnalyzeAsync(text);

        // Assert
        Assert.True(result.Score > 0);
        Assert.Equal("positive", result.Label);
    }

    [Fact]
    public async Task AnalyzeAsync_NegativeText_ReturnsNegativeScore()
    {
        // Arrange
        var text = "最悪な対応でした。腹立ちます。";

        // Act
        var result = await _analyzer.AnalyzeAsync(text);

        // Assert
        Assert.True(result.Score < 0);
        Assert.Equal("negative", result.Label);
    }

    [Fact]
    public async Task AnalyzeAsync_NeutralText_ReturnsNeutralScore()
    {
        // Arrange
        var text = "普通の天気です";

        // Act
        var result = await _analyzer.AnalyzeAsync(text);

        // Assert
        Assert.True(result.Score >= -0.3 && result.Score <= 0.3);
        Assert.Equal("neutral", result.Label);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyText_ReturnsNeutral()
    {
        // Arrange
        var text = "";

        // Act
        var result = await _analyzer.AnalyzeAsync(text);

        // Assert
        Assert.Equal(0.0, result.Score);
        Assert.Equal("neutral", result.Label);
    }

    [Fact]
    public async Task AnalyzeAsync_GratitudeText_ReturnsPositiveScore()
    {
        // Arrange
        var text = "ありがとうございました。助かりました。";

        // Act
        var result = await _analyzer.AnalyzeAsync(text);

        // Assert
        Assert.True(result.Score > 0);
        Assert.Contains("ありがとう", result.Keywords);
    }

    [Fact]
    public async Task AnalyzeAsync_ComplaintText_ReturnsNegativeScore()
    {
        // Arrange
        var text = "苦情があります。ひどい対応です。";

        // Act
        var result = await _analyzer.AnalyzeAsync(text);

        // Assert
        Assert.True(result.Score < 0);
        Assert.Contains("苦情", result.Keywords);
        Assert.Contains("ひどい", result.Keywords);
    }
}
