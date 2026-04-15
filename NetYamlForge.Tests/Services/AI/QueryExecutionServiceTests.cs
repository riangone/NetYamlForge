using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Services;
using NetYamlForge.AI.Models;
using NetYamlForge.AI.Infrastructure;

namespace NetYamlForge.Tests.Services.AI;

/// <summary>
/// QueryExecutionService 测试
/// </summary>
public class QueryExecutionServiceTests
{
    private readonly Mock<IAIQueryExecutor> _mockQueryExecutor;
    private readonly Mock<ILogger<QueryExecutionService>> _mockLogger;
    private readonly QueryExecutionService _executor;

    public QueryExecutionServiceTests()
    {
        _mockQueryExecutor = new Mock<IAIQueryExecutor>();
        _mockLogger = new Mock<ILogger<QueryExecutionService>>();

        _executor = new QueryExecutionService(
            _mockQueryExecutor.Object,
            new DefaultSqlSafetyGuard(),
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

        var mockRows = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = 1, ["name"] = "A", ["price"] = 100.0 },
            new() { ["id"] = 2, ["name"] = "B", ["price"] = 200.0 }
        };

        _mockQueryExecutor
            .Setup(x => x.ExecuteQueryAsync("products", It.IsAny<QueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockRows.Cast<Dictionary<string, object?>>());

        _mockQueryExecutor
            .Setup(x => x.CountAsync("products", It.IsAny<QueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        _mockQueryExecutor
            .Setup(x => x.GetEntityMetadataAsync("products", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIEntityMetadata { Name = "products", TableName = "products" });

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

        _mockQueryExecutor
            .Setup(x => x.CountAsync("products", It.IsAny<QueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5L);

        // Act
        var (data, total) = await _executor.ExecuteAsync(query, "test-project");

        // Assert
        Assert.Single(data);
        Assert.Equal(5, total);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilters_ReturnsFilteredData()
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

        var mockRows = new List<Dictionary<string, object?>>
        {
            new() { ["id"] = 1, ["name"] = "A" }
        };

        _mockQueryExecutor
            .Setup(x => x.ExecuteQueryAsync("products", It.IsAny<QueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockRows.Cast<Dictionary<string, object?>>());

        _mockQueryExecutor
            .Setup(x => x.CountAsync("products", It.IsAny<QueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        _mockQueryExecutor
            .Setup(x => x.GetEntityMetadataAsync("products", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIEntityMetadata { Name = "products", TableName = "products" });

        // Act
        var (data, total) = await _executor.ExecuteAsync(query, "test-project");

        // Assert
        Assert.NotNull(data);
        Assert.Single(data);
    }
}
