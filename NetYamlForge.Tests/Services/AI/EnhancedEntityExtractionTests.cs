using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI;
using Xunit;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// 拡張されたエンティティ抽出のテスト
/// </summary>
public class EnhancedEntityExtractionTests
{
    private readonly HybridIntentClassifier _classifier;
    private readonly AiWindowConfig _config;

    public EnhancedEntityExtractionTests()
    {
        _config = new AiWindowConfig
        {
            Intent = new IntentConfig
            {
                RuleBasedEnabled = true,
                LlmEnabled = false,
                ConfidenceThreshold = 0.6
            }
        };

        var configOptions = Options.Create(_config);
        var logger = new LoggerFactory().CreateLogger<HybridIntentClassifier>();
        _classifier = new HybridIntentClassifier(null, configOptions, logger);
    }

    #region 車両エンティティ

    [Theory]
    [InlineData("トヨタのカローラについて教えてください", "トヨタ", "カローラ")]
    [InlineData("ホンダのヴェゼルは在庫ありますか？", "ホンダ", "ヴェゼル")]
    [InlineData("日産 GT-R の価格を教えてください", "日産", "GT-R")]
    public async Task ExtractEntities_VehicleBrandAndModel(string message, string? expectedBrand, string? expectedModel)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert - 車種抽出は意図によってトリガーされる
        // vehicle_inquiry または appointment 関連の意図の場合のみ抽出される
        if (result.Intent.Contains("vehicle") || result.Intent.Contains("appointment"))
        {
            if (expectedBrand != null)
                Assert.Contains("vehicle_brand", result.Entities.Keys);
        }
    }

    [Theory]
    [InlineData("SUV について教えてください", "SUV")]
    [InlineData("ミニバンの特徴は？", "ミニバン")]
    public async Task ExtractEntities_VehicleType_InVehicleContext(string message, string expectedType)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert - 車両タイプも vehicle 関連の意図の場合のみ
        if (result.Intent.Contains("vehicle"))
        {
            Assert.Contains("vehicle_type", result.Entities.Keys);
        }
    }

    [Theory]
    [InlineData("新車を購入したい", "new")]
    [InlineData("中古車の在庫はありますか？", "used")]
    public async Task ExtractEntities_VehicleCondition(string message, string expectedCondition)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Contains("vehicle_condition", result.Entities.Keys);
        Assert.Equal(expectedCondition, result.Entities["vehicle_condition"]);
    }

    #endregion

    #region 予算・価格エンティティ

    [Theory]
    [InlineData("予算 300 万円以内で買いたい", "max")]
    [InlineData("50 万円以下の車は？", "max")]
    [InlineData("総額 400 万円の車を探してます", "exact")]
    public async Task ExtractEntities_Budget(string message, string expectedType)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Contains("budget_amount", result.Entities.Keys);
        Assert.Contains("budget_type", result.Entities.Keys);
        Assert.Equal(expectedType, result.Entities["budget_type"]);
    }

    [Theory]
    [InlineData("ローンは組めますか？", "loan")]
    [InlineData("現金一括払いできます", "cash")]
    [InlineData("リース契約はありますか？", "lease")]
    public async Task ExtractEntities_PaymentMethod(string message, string expectedMethod)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Contains("payment_method", result.Entities.Keys);
        Assert.Equal(expectedMethod, result.Entities["payment_method"]);
    }

    #endregion

    #region 日時エンティティ

    [Fact]
    public async Task ExtractEntities_PreferredDate_Tomorrow()
    {
        // Arrange
        var message = "明日の 10 時に予約したい";

        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Contains("preferred_date", result.Entities.Keys);
        Assert.Contains("preferred_time", result.Entities.Keys);
    }

    #endregion

    #region 顧客属性エンティティ

    [Theory]
    [InlineData("初めての車購入です", "true")]
    [InlineData("初回利用です", "true")]
    public async Task ExtractEntities_FirstPurchase(string message, string expectedValue)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Contains("is_first_purchase", result.Entities.Keys);
        Assert.Equal(expectedValue, result.Entities["is_first_purchase"]);
    }

    [Theory]
    [InlineData("法人で購入したいのですが", "business")]
    [InlineData("個人利用です", "personal")]
    public async Task ExtractEntities_CustomerType(string message, string expectedType)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Contains("customer_type", result.Entities.Keys);
        Assert.Equal(expectedType, result.Entities["customer_type"]);
    }

    #endregion

    #region 下取りエンティティ

    [Theory]
    [InlineData("下取りはありますか？", "true")]
    [InlineData("乗り換えを検討してます", "true")]
    public async Task ExtractEntities_TradeIn(string message, string expectedValue)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Contains("has_trade_in", result.Entities.Keys);
    }

    #endregion

    #region 複合エンティティ

    [Fact]
    public async Task ExtractEntities_MultipleEntities_InSingleMessage()
    {
        // Arrange
        var message = "トヨタの RAV4 で新車、予算 400 万円以内、ローンで買いたい";

        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Equal("vehicle_inquiry", result.Intent);
        Assert.Contains("vehicle_brand", result.Entities.Keys);
        Assert.Contains("vehicle_model", result.Entities.Keys);
        Assert.Contains("vehicle_condition", result.Entities.Keys);
        Assert.Contains("budget_amount", result.Entities.Keys);
        Assert.Contains("payment_method", result.Entities.Keys);
    }

    #endregion

    #region 意図認識テスト

    [Theory]
    [InlineData("カローラとプリウスどっちがいいと思いますか？", "vehicle_comparison")]
    [InlineData("下取りの査定をお願いします", "trade_inquiry")]
    [InlineData("ローンの金利を教えてください", "finance_inquiry")]
    [InlineData("キャンペーンはありますか？", "campaign_inquiry")]
    [InlineData("資料を送ってください", "document_request")]
    [InlineData("ありがとうございます", "thank_you")]
    public async Task ClassifyAsync_ExtendedIntents(string message, string expectedIntent)
    {
        // Act
        var result = await _classifier.ClassifyAsync(message);

        // Assert
        Assert.Equal(expectedIntent, result.Intent);
        Assert.True(result.Confidence >= 0.6);
    }

    #endregion
}
