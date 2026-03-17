using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Projects.SalesforceCrm.Hooks;
using NetYamlForge.Services;
using NetYamlForge.Services.Hooks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class PageRowMutationServiceTests
{
    private static PageRowMutationService CreateSut(SqliteConnection conn) =>
        new PageRowMutationService(conn, new SalesforceCrmValidatorRegistry(), new EmptyHookRegistry(), new EmptyProjectHookRegistry(), NullLogger<PageRowMutationService>.Instance);

    private sealed class EmptyHookRegistry : IEntityHookRegistry
    {
        public IEntityHook? Find(string hookName, EntityHookContext? context = null) => null;
    }

    private sealed class EmptyProjectHookRegistry : IProjectHookRegistry
    {
        public IEntityHook? Find(string projectName, string hookName, EntityHookContext? context = null) => null;
        public IEnumerable<string> GetHookNames(string projectName) => [];
        public IEnumerable<IEntityHook> GetAllHooks(string projectName) => [];
        public void Register(string projectName, IEntityHook hook) { }
    }

    private sealed class SalesforceCrmValidatorRegistry : IProjectPageMutationValidatorRegistry
    {
        private readonly SalesforceCrmPageMutationValidator _validator = new();
        public IProjectPageMutationValidator? Get(string projectName) =>
            string.Equals(projectName, "salesforce-crm", StringComparison.OrdinalIgnoreCase) ? _validator : null;
    }

    [Fact]
    public async Task UpdateRowAsync_SetsShippedDate_ForSalesforceOrderStatusShipped()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedCrmSchemaAsync(conn);
        await conn.ExecuteAsync("INSERT INTO Orders(OrderId, CustomerId, Status, ShippedDate) VALUES(1, 10, 'Open', NULL)");
        await conn.ExecuteAsync("INSERT INTO OrderDetails(OrderDetailId, OrderId, ProductId) VALUES(100, 1, 900)");

        var sut = CreateSut(conn);
        var section = new SectionDefinition { TargetTable = "Orders", TargetPrimaryKey = "OrderId" };

        var result = await sut.UpdateRowAsync("salesforce-crm", section, 1, "Status", "Shipped", "alice", "Alice");

        Assert.True(result.ok);
        var row = await conn.QuerySingleAsync<(string Status, string? ShippedDate)>(
            "SELECT Status, ShippedDate FROM Orders WHERE OrderId = 1");
        Assert.Equal("Shipped", row.Status);
        Assert.False(string.IsNullOrWhiteSpace(row.ShippedDate));
    }

    [Fact]
    public async Task UpdateRowAsync_RejectsInvalidTransition_ForSalesforceOrder()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedCrmSchemaAsync(conn);
        await conn.ExecuteAsync("INSERT INTO Orders(OrderId, CustomerId, Status, ShippedDate) VALUES(2, 20, 'Cancelled', NULL)");

        var sut = CreateSut(conn);
        var section = new SectionDefinition { TargetTable = "Orders", TargetPrimaryKey = "OrderId" };

        var result = await sut.UpdateRowAsync("salesforce-crm", section, 2, "Status", "Open", "bob", "Bob");

        Assert.False(result.ok);
        Assert.Contains("状態遷移", result.message);
    }

    [Fact]
    public async Task DeleteRowAsync_RejectsShippedOrder_ForSalesforceProject()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedCrmSchemaAsync(conn);
        await conn.ExecuteAsync("INSERT INTO Orders(OrderId, CustomerId, Status, ShippedDate) VALUES(3, 30, 'Shipped', '2026-01-01')");

        var sut = CreateSut(conn);
        var section = new SectionDefinition { TargetTable = "Orders", TargetPrimaryKey = "OrderId" };

        var result = await sut.DeleteRowAsync("salesforce-crm", section, 3);

        Assert.False(result.ok);
        Assert.Contains("削除できません", result.message);
    }

    [Fact]
    public async Task DeleteRowAsync_DeletesRow_ForNonSalesforceProject()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedCrmSchemaAsync(conn);
        await conn.ExecuteAsync("INSERT INTO Orders(OrderId, CustomerId, Status, ShippedDate) VALUES(4, 40, 'Cancelled', NULL)");

        var sut = CreateSut(conn);
        var section = new SectionDefinition { TargetTable = "Orders", TargetPrimaryKey = "OrderId" };

        var result = await sut.DeleteRowAsync("demo-ops", section, 4);

        Assert.True(result.ok);
        var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Orders WHERE OrderId = 4");
        Assert.Equal(0, count);
    }

    private static async Task SeedCrmSchemaAsync(SqliteConnection conn)
    {
        await conn.ExecuteAsync(
            """
            CREATE TABLE Orders (
                OrderId INTEGER PRIMARY KEY,
                CustomerId INTEGER NOT NULL,
                Status TEXT NOT NULL,
                ShippedDate TEXT NULL
            );

            CREATE TABLE OrderDetails (
                OrderDetailId INTEGER PRIMARY KEY,
                OrderId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL
            );

            CREATE TABLE Customers (
                CustomerId INTEGER PRIMARY KEY,
                Active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE Products (
                ProductId INTEGER PRIMARY KEY,
                Discontinued INTEGER NOT NULL DEFAULT 0
            );
            """);
    }
}

