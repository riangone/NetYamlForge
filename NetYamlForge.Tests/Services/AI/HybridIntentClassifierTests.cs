using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI;
using NetYamlForge.Services.AI.Providers;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// HybridIntentClassifier のテスト
/// </summary>
public class HybridIntentClassifierTests
{
    private readonly HybridIntentClassifier _classifier;
    private readonly AiWindowConfig _config;

    public HybridIntentClassifierTests()
    {
        _config = new AiWindowConfig
        {
            Intent = new IntentConfig
            {
                RuleBasedEnabled = true,
                LlmEnabled = false, // テストではルールベースのみ
                ConfidenceThreshold = 0.6
            }
        };

        var configOptions = Options.Create(_config);
        var logger = new LoggerFactory().CreateLogger<HybridIntentClassifier>();
        
        // LLM プロバイダーは null（ルールベースのみテスト）
        _classifier = new HybridIntentClassifier(null, configOptions, logger);
    }

    [Fact]
    public async Task ClassifyAsync_Greeting_ReturnsGreetingIntent()
    {
        // Arrange
        var message = "こんにちは";

        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Equal("greeting", result.Intent);
        Assert.True(result.Confidence >= 0.6);
        Assert.Equal("rule", result.Method);
    }

    [Fact]
    public async Task ClassifyAsync_HoursInquiry_ReturnsHoursIntent()
    {
        // Arrange
        var message = "営業時間を教えてください";

        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Equal("hours_inquiry", result.Intent);
        Assert.True(result.Confidence >= 0.6);
    }

    [Fact]
    public async Task ClassifyAsync_AppointmentBooking_ReturnsBookingIntent()
    {
        // Arrange
        var message = "予約を申し込みたいです";

        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Equal("appointment_booking", result.Intent);
        Assert.True(result.Confidence >= 0.6);
    }

    [Fact]
    public async Task ClassifyAsync_Complaint_DetectsNegativeIntent()
    {
        // Arrange
        var message = "苦情を言いたい";

        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Equal("complaint", result.Intent);
        // ルールベースでは SuggestHandover は設定されない
    }

    [Fact]
    public async Task ClassifyAsync_Unknown_ReturnsGeneralInquiry()
    {
        // Arrange
        var message = "訳の分からない文章";

        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Equal("general_inquiry", result.Intent);
    }

    [Fact]
    public async Task ClassifyAsync_ExtractsDateEntity()
    {
        // Arrange
        var message = "明日の 10 時に予約したい";

        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Contains("preferred_date", result.Entities.Keys);
    }

    [Fact]
    public async Task ClassifyAsync_WithContext_IncreasesConfidence()
    {
        // Arrange
        var context = new ConversationContext
        {
            CurrentIntent = "greeting"
        };
        var message = "こんにちは";

        // Act
        var result = await _classifier.ClassifyAsync(message, context);

        // Assert
        Assert.Equal("greeting", result.Intent);
        // 文脈により置信度が上昇
        Assert.True(result.Confidence >= 0.6);
    }

    // ─────────────────────────────────────────────
    // 試乗予約インテントのテスト（修正検証）
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData("試乗を予約したい")]
    [InlineData("試乗したい")]
    [InlineData("テストドライブをお願いします")]
    [InlineData("実際に乗ってみたいです")]
    [InlineData("試乗予約")]
    public async Task ClassifyAsync_TestDriveBooking_ReturnsTestDriveIntent(string message)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Equal("test_drive_booking", result.Intent);
        Assert.True(result.Confidence >= 0.8);
        Assert.Equal("rule", result.Method);
    }

    [Theory]
    [InlineData("プリウスの試乗を予約したい")]
    [InlineData("ランドクルーザーを試乗したい")]
    public async Task ClassifyAsync_TestDriveWithVehicle_ReturnsTestDriveIntent(string message)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        // 試乗キーワードがtest_drive_bookingルールにのみ含まれるため、
        // vehicle_inquiryではなくtest_drive_bookingが優先される
        Assert.Equal("test_drive_booking", result.Intent);
        Assert.True(result.Confidence >= 0.8);
    }

    [Theory]
    [InlineData("在庫のある車種を教えてください")]
    [InlineData("新車のカタログを見せて")]
    public async Task ClassifyAsync_VehicleInquiry_ReturnsVehicleInquiryIntent(string message)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        // 試乗キーワードを含まない車両問い合わせはvehicle_inquiryになる
        Assert.Equal("vehicle_inquiry", result.Intent);
        Assert.True(result.Confidence >= 0.6);
    }
}
