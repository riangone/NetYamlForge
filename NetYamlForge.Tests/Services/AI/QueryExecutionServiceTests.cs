using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI;
using NetYamlForge.Models.AI;
using NetYamlForge.Models;
using NetYamlForge.Services;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// QueryExecutionService 测试
/// </summary>
public class QueryExecutionServiceTests
{
    private readonly Mock<IDynamicCrudRepository> _mockRepo;
    private readonly Mock<IEntityMetadataProvider> _mockMetadata;
    private readonly Mock<ILogger<QueryExecutionService>> _mockLogger;
    private readonly QueryExecutionService _executor;

    public QueryExecutionServiceTests()
    {
        _mockRepo = new Mock<IDynamicCrudRepository>();
        _mockMetadata = new Mock<IEntityMetadataProvider>();
        _mockLogger = new Mock<ILogger<QueryExecutionService>>();
        
        _executor = new QueryExecutionService(
            _mockRepo.Object,
            _mockMetadata.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ListQuery_ReturnsDataAndTotal()
    {
        // Arrange
        var query = new ParsedQueryParams
        {
            Entity = "products",
            Action = "list",
            Filters = new List<FilterClause>(),
            Select = new List<string> { "id", "name", "price" },
            Top = 10
        };

        var mockData = new List<dynamic>
        {
            new System.Dynamic.ExpandoObject(),
            new System.Dynamic.ExpandoObject()
        };

        _mockRepo
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string?>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .ReturnsAsync(mockData);

        _mockRepo
            .Setup(x => x.CountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string?>>()))
            .ReturnsAsync(2);

        var mockEntity = new EntityDefinition
        {
            DisplayName = "产品",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["id"] = new ColumnDefinition { Type = "int" },
                ["name"] = new ColumnDefinition { Type = "string" },
                ["price"] = new ColumnDefinition { Type = "decimal" }
            }
        };
        _mockMetadata.Setup(x => x.Get("products")).Returns(mockEntity);

        // Act
        var (data, total) = await _executor.ExecuteAsync(query, "test-project");

        // Assert
        Assert.NotNull(data);
        Assert.Equal(2, data.Count);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task ExecuteAsync_CountQuery_ReturnsCount()
    {
        // Arrange
        var query = new ParsedQueryParams
        {
            Entity = "products",
            Action = "count",
            Filters = new List<FilterClause>
            {
                new FilterClause { Field = "price", Op = "gt", Value = 100 }
            }
        };

        _mockRepo
            .Setup(x => x.CountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string?>>()))
            .ReturnsAsync(5);

        var mockEntity = new EntityDefinition
        {
            DisplayName = "产品",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["id"] = new ColumnDefinition { Type = "int" },
                ["price"] = new ColumnDefinition { Type = "decimal" }
            }
        };
        _mockMetadata.Setup(x => x.Get("products")).Returns(mockEntity);

        // Act
        var (data, total) = await _executor.ExecuteAsync(query, "test-project");

        // Assert
        Assert.Single(data);
        Assert.Equal(5, total);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilters_BuildsFilterDictionary()
    {
        // Arrange
        var query = new ParsedQueryParams
        {
            Entity = "products",
            Action = "list",
            Filters = new List<FilterClause>
            {
                new FilterClause { Field = "price", Op = "gt", Value = 100 },
                new FilterClause { Field = "stock_quantity", Op = "lt", Value = 10 }
            },
            Select = new List<string> { "id", "name" }
        };

        var mockData = new List<dynamic>
        {
            new System.Dynamic.ExpandoObject()
        };

        _mockRepo
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string?>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .ReturnsAsync(mockData);

        _mockRepo
            .Setup(x => x.CountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string?>>()))
            .ReturnsAsync(1);

        var mockEntity = new EntityDefinition
        {
            DisplayName = "产品",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["id"] = new ColumnDefinition { Type = "int" },
                ["name"] = new ColumnDefinition { Type = "string" },
                ["price"] = new ColumnDefinition { Type = "decimal" },
                ["stock_quantity"] = new ColumnDefinition { Type = "int" }
            }
        };
        _mockMetadata.Setup(x => x.Get("products")).Returns(mockEntity);

        // Act
        var (data, total) = await _executor.ExecuteAsync(query, "test-project");

        // Assert
        Assert.NotNull(data);
        Assert.Equal(1, data.Count);
    }
}
