using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Api;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.BatchJob;
using NetYamlForge.Controllers;
using Xunit;

namespace NetYamlForge.Tests.Services.Api;

public class ApiEntityWriteServiceTests
{
    private readonly Mock<IDynamicCrudRepository> _mockRepo;
    private readonly Mock<DynamicEntityCommandService> _mockCommandService;
    private readonly DynamicEntityFormValidationService _formValidationService;
    private readonly Mock<IEntityHooksService> _mockEntityHooks;
    private readonly Mock<IAuditLogService> _mockAudit;
    private readonly Mock<ILogger<ApiEntityWriteService>> _mockLogger;
    private readonly ApiEntityWriteService _sut;

    public ApiEntityWriteServiceTests()
    {
        _mockRepo = new Mock<IDynamicCrudRepository>();
        
        // Mock DynamicEntityCommandService using null for parameters since the tested methods will be intercepted.
        _mockCommandService = new Mock<DynamicEntityCommandService>(
            (IDynamicCrudRepository)null!,
            (EntityCrudExecutionService)null!
        );

        _formValidationService = new DynamicEntityFormValidationService(
            new FormValueValidationService(new ValueConverter())
        );

        _mockEntityHooks = new Mock<IEntityHooksService>();
        _mockAudit = new Mock<IAuditLogService>();
        _mockLogger = new Mock<ILogger<ApiEntityWriteService>>();

        _sut = new ApiEntityWriteService(
            _mockRepo.Object,
            _mockCommandService.Object,
            _formValidationService,
            _mockEntityHooks.Object,
            _mockAudit.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task CreateAsync_WhenValidationFails_ReturnsFailureWithErrors()
    {
        // Arrange
        var meta = new EntityDefinition
        {
            Table = "Products",
            Key = "Id",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Name"] = new() { Type = "string", Required = true }
            }
        };
        var body = new Dictionary<string, object?>(); // Missing required "Name"

        // Act
        var result = await _sut.CreateAsync("Products", body, meta, "test-project", "admin");

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Errors.ContainsKey("Name"));
        Assert.Equal("Required", result.Errors["Name"]);
    }

    [Fact]
    public async Task PartialUpdateAsync_WhenEntityNotFound_ReturnsNotFound()
    {
        // Arrange
        var meta = new EntityDefinition
        {
            Table = "Products",
            Key = "Id",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Name"] = new() { Type = "string" }
            }
        };
        _mockRepo.Setup(r => r.GetByIdAsync("Products", "999"))
            .ReturnsAsync((Dictionary<string, object?>?)null);

        var body = new Dictionary<string, object?> { ["Name"] = "New Name" };

        // Act
        var result = await _sut.PartialUpdateAsync("Products", "999", body, meta, "test-project", "admin");

        // Assert
        Assert.False(result.Success);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task PartialUpdateAsync_SkipsValidationOfMissingRequiredFields()
    {
        // Arrange
        var meta = new EntityDefinition
        {
            Table = "Products",
            Key = "Id",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Id"] = new() { Type = "int", Identity = true },
                ["Name"] = new() { Type = "string", Required = true },
                ["Price"] = new() { Type = "decimal", Required = false }
            }
        };

        var existingRecord = new Dictionary<string, object?>
        {
            ["Id"] = 1,
            ["Name"] = "Old Product",
            ["Price"] = 9.99m
        };

        _mockRepo.Setup(r => r.GetByIdAsync("Products", "1"))
            .ReturnsAsync(existingRecord);

        // In PATCH, we only update Price, omitting the required "Name"
        var body = new Dictionary<string, object?> { ["Price"] = 19.99 };

        // Mock command service response using CommandResult.Success()
        _mockCommandService.Setup(c => c.UpdateAsync(
            "Products",
            "Id",
            "1",
            It.IsAny<Dictionary<string, object?>>(),
            null,
            null,
            "admin"
        )).ReturnsAsync(CommandResult.Success());

        // Mock repo returning the updated entity
        var updatedRecord = new Dictionary<string, object?>
        {
            ["Id"] = 1,
            ["Name"] = "Old Product",
            ["Price"] = 19.99m
        };
        _mockRepo.Setup(r => r.GetByIdAsync("Products", "1"))
            .ReturnsAsync(updatedRecord);

        // Act
        var result = await _sut.PartialUpdateAsync("Products", "1", body, meta, "test-project", "admin");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Entity);
        Assert.Equal(19.99m, Convert.ToDecimal(result.Entity.Data["Price"]));
        Assert.Equal("Old Product", result.Entity.Data["Name"]);
        _mockAudit.Verify(a => a.WriteAsync("api.patch", "Products", "id=1", "admin"), Times.Once);
    }
}
