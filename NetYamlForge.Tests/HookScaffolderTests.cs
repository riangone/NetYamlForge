// 目的: HookScaffolder のユーティリティ関数（名前変換）の回帰テスト。

using NetYamlForge.Services;
using Xunit;

namespace NetYamlForge.Tests;

public class HookScaffolderTests
{
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
        Assert.Equal(expected, HookScaffolder.ToPascalCase(input));
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
        Assert.Equal(expected, HookScaffolder.ToSnakeCase(input));
    }
}
