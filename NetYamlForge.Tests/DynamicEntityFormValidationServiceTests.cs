using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Validation;
using NetYamlForge.Services.Connection;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Xunit;

namespace NetYamlForge.Tests;

public class DynamicEntityFormValidationServiceTests
{
    [Fact]
    public void ConvertAndValidate_SkipsIdentityColumn()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "Id",
            DisplayName = "Orders",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Id"] = new() { Type = "int", Identity = true },
                ["Name"] = new() { Type = "string" }
            }
        };
        var form = new Dictionary<string, string?> { ["Id"] = "10", ["Name"] = "A" };

        var (values, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Empty(errors);
        Assert.False(values.ContainsKey("Id"));
        Assert.Equal("A", values["Name"]);
    }

    [Fact]
    public void ConvertAndValidate_DefaultsMissingBoolToFalse()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "Id",
            DisplayName = "Orders",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["IsActive"] = new() { Type = "bool" }
            }
        };
        var form = new Dictionary<string, string?>();

        var (values, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Empty(errors);
        Assert.Equal(false, values["IsActive"]);
    }

    [Fact]
    public void ConvertAndValidate_ReturnsError_WhenConversionFails()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "Id",
            DisplayName = "Orders",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Amount"] = new() { Type = "int" }
            }
        };
        var form = new Dictionary<string, string?> { ["Amount"] = "abc" };

        var (_, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Equal("Invalid integer", errors["Amount"]);
    }

    [Fact]
    public void ConvertAndValidate_RegexValidator_Success()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Users",
            Key = "Id",
            DisplayName = "Users",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Phone"] = new()
                {
                    Type = "string",
                    Validators = new List<ValidatorDefinition>
                    {
                        new() { Type = "regex", Pattern = @"^\d{3}-\d{4}$", ErrorMessage = "Phone format invalid." }
                    }
                }
            }
        };
        var form = new Dictionary<string, string?> { ["Phone"] = "123-4567" };

        var (_, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Empty(errors);
    }

    [Fact]
    public void ConvertAndValidate_RegexValidator_Failure()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Users",
            Key = "Id",
            DisplayName = "Users",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Phone"] = new()
                {
                    Type = "string",
                    Validators = new List<ValidatorDefinition>
                    {
                        new() { Type = "regex", Pattern = @"^\d{3}-\d{4}$", ErrorMessage = "Phone format invalid." }
                    }
                }
            }
        };
        var form = new Dictionary<string, string?> { ["Phone"] = "12-345" };

        var (_, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Single(errors);
        Assert.Equal("Phone format invalid.", errors["Phone"]);
    }

    [Fact]
    public void ConvertAndValidate_RangeValidator_Success()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Users",
            Key = "Id",
            DisplayName = "Users",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Age"] = new()
                {
                    Type = "int",
                    Validators = new List<ValidatorDefinition>
                    {
                        new() { Type = "range", Min = "18", Max = "100", ErrorMessage = "Age out of range." }
                    }
                }
            }
        };
        var form = new Dictionary<string, string?> { ["Age"] = "25" };

        var (_, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Empty(errors);
    }

    [Fact]
    public void ConvertAndValidate_RangeValidator_Failure()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Users",
            Key = "Id",
            DisplayName = "Users",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Age"] = new()
                {
                    Type = "int",
                    Validators = new List<ValidatorDefinition>
                    {
                        new() { Type = "range", Min = "18", Max = "100", ErrorMessage = "Age out of range." }
                    }
                }
            }
        };
        var form = new Dictionary<string, string?> { ["Age"] = "15" };

        var (_, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Single(errors);
        Assert.Equal("Age out of range.", errors["Age"]);
    }

    [Fact]
    public void ConvertAndValidate_ConditionalValidator_ConditionMet_EmptyValue()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "Id",
            DisplayName = "Orders",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Status"] = new() { Type = "string" },
                ["Rating"] = new()
                {
                    Type = "int",
                    Validators = new List<ValidatorDefinition>
                    {
                        new() { Type = "required_if", Condition = "Status == 'Active'", ErrorMessage = "Rating is required for active status." }
                    }
                }
            }
        };
        var form = new Dictionary<string, string?> { ["Status"] = "Active", ["Rating"] = "" };

        var (_, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Single(errors);
        Assert.Equal("Rating is required for active status.", errors["Rating"]);
    }

    [Fact]
    public void ConvertAndValidate_ConditionalValidator_ConditionNotMet()
    {
        var sut = new DynamicEntityFormValidationService(new FormValueValidationService(new ValueConverter()));
        var meta = new EntityDefinition
        {
            Table = "Orders",
            Key = "Id",
            DisplayName = "Orders",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["Status"] = new() { Type = "string" },
                ["Rating"] = new()
                {
                    Type = "int",
                    Validators = new List<ValidatorDefinition>
                    {
                        new() { Type = "required_if", Condition = "Status == 'Active'", ErrorMessage = "Rating is required for active status." }
                    }
                }
            }
        };
        var form = new Dictionary<string, string?> { ["Status"] = "Pending", ["Rating"] = "" };

        var (_, errors) = sut.ConvertAndValidate(meta, form);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ConvertAndValidateAsync_CustomHook_Success()
    {
        var registry = new ProjectValidatorHookRegistry();
        registry.Register("TestProject", new MockCustomHook());
        
        var formService = new FormValueValidationService(
            new ValueConverter(),
            new IFieldValidator[] { new RegexFieldValidator(), new RangeFieldValidator(), new ConditionalFieldValidator() },
            registry,
            new MockConnectionManager());

        var sut = new DynamicEntityFormValidationService(formService);
        var meta = new EntityDefinition
        {
            Table = "Accounts",
            Key = "Id",
            DisplayName = "Accounts",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["CreditCode"] = new()
                {
                    Type = "string",
                    Validators = new List<ValidatorDefinition>
                    {
                        new() { Type = "custom_hook", HookName = "ValidateCreditRating" }
                    }
                }
            }
        };
        var form = new Dictionary<string, string?> { ["CreditCode"] = "Valid" };

        var (_, errors) = await sut.ConvertAndValidateAsync(meta, form, "TestProject");

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ConvertAndValidateAsync_CustomHook_Failure()
    {
        var registry = new ProjectValidatorHookRegistry();
        registry.Register("TestProject", new MockCustomHook());
        
        var formService = new FormValueValidationService(
            new ValueConverter(),
            new IFieldValidator[] { new RegexFieldValidator(), new RangeFieldValidator(), new ConditionalFieldValidator() },
            registry,
            new MockConnectionManager());

        var sut = new DynamicEntityFormValidationService(formService);
        var meta = new EntityDefinition
        {
            Table = "Accounts",
            Key = "Id",
            DisplayName = "Accounts",
            Columns = new Dictionary<string, ColumnDefinition>
            {
                ["CreditCode"] = new()
                {
                    Type = "string",
                    Validators = new List<ValidatorDefinition>
                    {
                        new() { Type = "custom_hook", HookName = "ValidateCreditRating" }
                    }
                }
            }
        };
        var form = new Dictionary<string, string?> { ["CreditCode"] = "Invalid" };

        var (_, errors) = await sut.ConvertAndValidateAsync(meta, form, "TestProject");

        Assert.Single(errors);
        Assert.Equal("Credit rating too low.", errors["CreditCode"]);
    }
}

public class MockCustomHook : ICustomValidatorHook
{
    public string HookName => "ValidateCreditRating";

    public Task<ValidationResult> ValidateAsync(object? value, Dictionary<string, object?> rowContext, IDbConnection db, IDbTransaction? tx = null)
    {
        if (value?.ToString() == "Invalid")
        {
            return Task.FromResult(ValidationResult.Failure("Credit rating too low."));
        }
        return Task.FromResult(ValidationResult.Success());
    }
}

public class MockConnectionManager : IConnectionManager
{
    public Task<IDbConnection> GetConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IDbConnection>(null!);
    public void ReleaseConnection(IDbConnection connection) { }
    public Task<IDbConnection> GetConnectionAsync(string projectName, CancellationToken cancellationToken = default) => Task.FromResult<IDbConnection>(null!);
    public IDbConnection GetConnection() => null!;
    public IDbConnection GetConnection(string projectName) => null!;
    public void ReleaseConnection(string projectName, IDbConnection connection) { }
    public Dictionary<string, ConnectionPoolStats> GetAllPoolStats() => new();
    public ConnectionPoolStats GetPoolStats(string projectName) => null!;
    public void ResetPool(string projectName) { }
    public void ResetAllPools() { }
    public void Dispose() { }
}
