using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services;
using NetYamlForge.AI.Services;
using NetYamlForge.AI.Services.ToolValidation;
using Xunit;
using Moq;

namespace NetYamlForge.Tests.Services.AI.ToolValidation;

/// <summary>
/// ToolCallValidator 单元测试
/// </summary>
public class ToolCallValidatorTests
{
    private readonly Mock<ILogger<ToolCallValidator>> _loggerMock;
    private readonly ToolCallValidator _validator;

    public ToolCallValidatorTests()
    {
        _loggerMock = new Mock<ILogger<ToolCallValidator>>();
        _validator = new ToolCallValidator(_loggerMock.Object, new NetYamlForge.AI.Infrastructure.DefaultSqlSafetyGuard());
    }

    [Fact]
    public async Task ValidateAsync_ValidToolCall_ShouldReturnSuccess()
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = "list",
            ["filters"] = new JsonArray(),
            ["top"] = 10
        };

        // Act
        var result = await _validator.ValidateAsync(toolCall, "auto-dealer-demo");

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_EmptyEntity_ShouldReturnFail()
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "",
            ["action"] = "list"
        };

        // Act
        var result = await _validator.ValidateAsync(toolCall, "auto-dealer-demo");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("entity 字段不能为空", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_EmptyAction_ShouldReturnFail()
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = ""
        };

        // Act
        var result = await _validator.ValidateAsync(toolCall, "auto-dealer-demo");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("action 字段不能为空", result.ErrorMessage);
    }

    [Theory]
    [InlineData("invalid_entity", false)]
    [InlineData("vehicles", true)]
    [InlineData("sales_leads", true)]
    [InlineData("customers", true)]
    public async Task ValidateAsync_EntityWhitelist_ShouldValidate(
        string entity,
        bool expectedValid)
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = entity,
            ["action"] = "list"
        };

        // Act
        var result = await _validator.ValidateAsync(toolCall, "auto-dealer-demo");

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("list", true)]
    [InlineData("count", true)]
    [InlineData("get", true)]
    [InlineData("create", true)]
    [InlineData("update", true)]
    [InlineData("delete", false)]
    [InlineData("drop", false)]
    public async Task ValidateAsync_ActionWhitelist_ShouldValidate(
        string action,
        bool expectedValid)
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = action
        };

        // Act
        var result = await _validator.ValidateAsync(toolCall, "auto-dealer-demo");

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_SqlInjectionInEntity_ShouldFail()
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles; DROP TABLE customers",
            ["action"] = "list"
        };

        // Act
        var result = await _validator.ValidateAsync(toolCall, "auto-dealer-demo");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("不安全", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_StateWhitelist_EscalateShouldBlockAllTools()
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "query_data",
            ["entity"] = "vehicles",
            ["action"] = "list"
        };

        // Act
        var result = await _validator.ValidateAsync(
            toolCall,
            "auto-dealer-demo",
            AppointmentStateMachine.State.Escalate);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("不允许", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_StateWhitelist_ConfirmingShouldAllowCreateAppointment()
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "create_appointment_request",
            ["entity"] = "service_appointments",
            ["action"] = "create"
        };

        // Act
        var result = await _validator.ValidateAsync(
            toolCall,
            "auto-dealer-demo",
            AppointmentStateMachine.State.Confirming);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_StateWhitelist_CollectDateShouldBlockAppointmentCreation()
    {
        // Arrange
        var toolCall = new JsonObject
        {
            ["tool_call"] = "create_appointment_request",
            ["entity"] = "service_appointments",
            ["action"] = "create"
        };

        // Act
        var result = await _validator.ValidateAsync(
            toolCall,
            "auto-dealer-demo",
            AppointmentStateMachine.State.CollectDate);

        // Assert
        Assert.False(result.IsValid);
    }
}

/// <summary>
/// SqlSafetyGuard 集成测试
/// </summary>
public class SqlSafetyGuardIntegrationTests
{
    [Theory]
    [InlineData("vehicles", true)]
    [InlineData("sales_leads", true)]
    [InlineData("customers", true)]
    [InlineData("DROP TABLE customers;--", false)]
    [InlineData("customers; DROP TABLE", false)]
    public void EnsureIdentifier_ShouldValidateTableNames(string input, bool shouldPass)
    {
        if (shouldPass)
        {
            // 不应抛出异常
            SqlSafetyGuard.EnsureIdentifier(input, "test");
        }
        else
        {
            // 应抛出 InvalidOperationException
            var ex = Assert.Throws<InvalidOperationException>(
                () => SqlSafetyGuard.EnsureIdentifier(input, "test"));

            Assert.Contains("Unsafe identifier", ex.Message);
        }
    }

    [Theory]
    [InlineData("customer_name", true)]
    [InlineData("phone", true)]
    [InlineData("created_at", true)]
    [InlineData("column; DROP TABLE", false)]
    [InlineData("name' OR '1'='1", false)]
    public void EnsureIdentifier_ShouldValidateColumnNames(string input, bool shouldPass)
    {
        if (shouldPass)
        {
            SqlSafetyGuard.EnsureIdentifier(input, "test");
        }
        else
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => SqlSafetyGuard.EnsureIdentifier(input, "test"));

            Assert.Contains("Unsafe identifier", ex.Message);
        }
    }

    [Theory]
    [InlineData("list", true)]
    [InlineData("create", true)]
    [InlineData("DROP", false)]
    [InlineData("DELETE", false)]
    [InlineData("UPDATE; DROP", false)]
    public void EnsureIdentifier_ShouldValidateActions(string input, bool shouldPass)
    {
        if (shouldPass)
        {
            SqlSafetyGuard.EnsureIdentifier(input, "test");
        }
        else
        {
            Assert.Throws<InvalidOperationException>(
                () => SqlSafetyGuard.EnsureIdentifier(input, "test"));
        }
    }

    [Fact]
    public void IsUnsafeToken_ShouldDetectSqlInjectionMarkers()
    {
        Assert.True(SqlSafetyGuard.IsUnsafeToken("value; DROP TABLE"));
        Assert.True(SqlSafetyGuard.IsUnsafeToken("value--comment"));
        Assert.True(SqlSafetyGuard.IsUnsafeToken("value/*comment*/"));
        Assert.False(SqlSafetyGuard.IsUnsafeToken("normal_value"));
    }
}
