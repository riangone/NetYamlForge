using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetYamlForge.Services.AI;
using NetYamlForge.Models.AI;
using NetYamlForge.Services.AI.Providers;
using NetYamlForge.Services;
using NetYamlForge.Models;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// QueryParserService 测试
/// </summary>
public class QueryParserServiceTests
{
    private readonly Mock<ILlmProvider> _mockLlmProvider;
    private readonly Mock<IEntityMetadataProvider> _mockMetadata;
    private readonly Mock<ILogger<QueryParserService>> _mockLogger;
    private readonly Mock<IOptions<CliConfig>> _mockConfig;
    private readonly QueryParserService _parser;

    public QueryParserServiceTests()
    {
        _mockLlmProvider = new Mock<ILlmProvider>();
        _mockMetadata = new Mock<IEntityMetadataProvider>();
        _mockLogger = new Mock<ILogger<QueryParserService>>();
        _mockConfig = new Mock<IOptions<CliConfig>>();
        
        _mockConfig.Setup(x => x.Value).Returns(new CliConfig());
        
        _parser = new QueryParserService(
            _mockLlmProvider.Object,
            _mockMetadata.Object,
            _mockLogger.Object,
            _mockConfig.Object);
    }

    [Fact]
    public async Task ParseAsync_SimpleQuery_ReturnsParsedQuery()
    {
        // Arrange
        var naturalLanguage = "显示所有产品";
        
        var llmResponse = @"{
            ""entity"": ""products"",
            ""action"": ""list"",
            ""filters"": [],
            ""select"": [""id"", ""name"", ""price""]
        }";
        
        _mockLlmProvider
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var mockEntity = CreateMockEntityMetadata("products");
        _mockMetadata.Setup(x => x.Get("products")).Returns(mockEntity);

        // Act
        var result = await _parser.ParseAsync(naturalLanguage, null, "products");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("products", result.Entity);
        Assert.Equal("list", result.Action);
        Assert.Empty(result.Filters);
    }

    [Fact]
    public async Task ParseAsync_WithFilters_ReturnsParsedQueryWithFilters()
    {
        // Arrange
        var naturalLanguage = "找出库存低于 5 的商品";
        
        var llmResponse = @"{
            ""entity"": ""products"",
            ""action"": ""list"",
            ""filters"": [
                {
                    ""field"": ""stock_quantity"",
                    ""op"": ""lt"",
                    ""value"": 5
                }
            ],
            ""select"": [""id"", ""name"", ""stock_quantity""]
        }";
        
        _mockLlmProvider
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var mockEntity = CreateMockEntityMetadata("products");
        _mockMetadata.Setup(x => x.Get("products")).Returns(mockEntity);

        // Act
        var result = await _parser.ParseAsync(naturalLanguage, null, "products");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Filters);
        Assert.Equal("stock_quantity", result.Filters[0].Field);
        Assert.Equal("lt", result.Filters[0].Op);
        Assert.Equal(5, result.Filters[0].Value);
    }

    [Fact]
    public async Task ParseAsync_WithOrderBy_ReturnsParsedQueryWithOrderBy()
    {
        // Arrange
        var naturalLanguage = "显示销售额前 10 的产品";
        
        var llmResponse = @"{
            ""entity"": ""products"",
            ""action"": ""list"",
            ""filters"": [],
            ""orderBy"": {
                ""field"": ""sales_amount"",
                ""dir"": ""desc""
            },
            ""top"": 10,
            ""select"": [""id"", ""name"", ""sales_amount""]
        }";
        
        _mockLlmProvider
            .Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var mockEntity = CreateMockEntityMetadata("products");
        _mockMetadata.Setup(x => x.Get("products")).Returns(mockEntity);

        // Act
        var result = await _parser.ParseAsync(naturalLanguage, null, "products");

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.OrderBy);
        Assert.Equal("sales_amount", result.OrderBy.Field);
        Assert.Equal("desc", result.OrderBy.Dir);
        Assert.Equal(10, result.Top);
    }

    [Fact]
    public void RelativeDateRanges_Today_ReturnsCorrectRange()
    {
        // Arrange
        var today = DateTime.Today;

        // Act
        var (start, end) = RelativeDateRanges.GetDateRange("today");

        // Assert
        Assert.Equal(today, start);
        Assert.Equal(today.AddDays(1).AddTicks(-1), end);
    }

    [Fact]
    public void RelativeDateRanges_LastMonth_ReturnsCorrectRange()
    {
        // Arrange & Act
        var (start, end) = RelativeDateRanges.GetDateRange("last_month");

        // Assert
        Assert.Equal(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-1).Date, start);
        Assert.Equal(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddTicks(-1), end);
    }

    private EntityDefinition CreateMockEntityMetadata(string name)
    {
        var metadata = new EntityDefinition
        {
            DisplayName = name,
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["id"] = new ColumnDefinition { Type = "int", Hidden = false },
                ["name"] = new ColumnDefinition { Type = "string", Hidden = false },
                ["price"] = new ColumnDefinition { Type = "decimal", Hidden = false },
                ["stock_quantity"] = new ColumnDefinition { Type = "int", Hidden = false },
                ["sales_amount"] = new ColumnDefinition { Type = "decimal", Hidden = false },
                ["created_at"] = new ColumnDefinition { Type = "datetime", Hidden = false }
            }
        };
        return metadata;
    }
}
