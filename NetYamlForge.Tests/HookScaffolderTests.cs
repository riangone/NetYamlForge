using System.Reflection;
using Xunit;

namespace NetYamlForge.Tests;

public class HookScaffolderTests
{
    private static object InvokeMethod(string methodName, string input)
    {
        var assembly = Assembly.Load("NetYamlForge.Tooling");
        var type = assembly.GetType("NetYamlForge.Services.HookScaffolder");
        if (type == null)
        {
            throw new Exception("Type 'NetYamlForge.Services.HookScaffolder' not found in NetYamlForge.Tooling assembly.");
        }
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null)
        {
            throw new Exception($"Method '{methodName}' not found in HookScaffolder.");
        }
        return method.Invoke(null, new object[] { input })!;
    }

    // ─── ToPascalCase ────────────────────────────────────────────

    [Theory]
    [InlineData("blog",             "Blog")]
    [InlineData("salesforce-crm",   "SalesforceCrm")]
    [InlineData("northwind-sqlite3","NorthwindSqlite3")]
    [InlineData("my_project",       "MyProject")]
    [InlineData("todo",             "Todo")]
    [InlineData("b2b-order-ops",    "B2bOrderOps")]
    public void ToPascalCase_ConvertsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, InvokeMethod("ToPascalCase", input));
    }

    // ─── ToSnakeCase ─────────────────────────────────────────────

    [Theory]
    [InlineData("ValidateInventory",    "validate_inventory")]
    [InlineData("SendNotification",     "send_notification")]
    [InlineData("AuditLog",             "audit_log")]
    [InlineData("Trim",                 "trim")]
    [InlineData("ValidateEmailFormat",  "validate_email_format")]
    public void ToSnakeCase_ConvertsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, InvokeMethod("ToSnakeCase", input));
    }
}
