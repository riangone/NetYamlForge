using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class PageDataQueryServiceTests
{
    [Fact]
    public async Task LoadPageDataAsync_AppliesLikeFilter_ForTableSource()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);

        await conn.ExecuteAsync(
            """
            INSERT INTO DemoItem(Id, Name, CategoryId, CreatedAt) VALUES(1, 'Alpha', 10, '2026-03-01 10:00:00');
            INSERT INTO DemoItem(Id, Name, CategoryId, CreatedAt) VALUES(2, 'Beta', 10, '2026-03-02 10:00:00');
            INSERT INTO DemoItem(Id, Name, CategoryId, CreatedAt) VALUES(3, 'Gamma', 20, '2026-03-03 10:00:00');
            """);

        var service = new PageDataQueryService(conn, NullLogger<PageDataQueryService>.Instance);
        var page = new PageDefinition
        {
            Sections = new List<SectionDefinition>
            {
                new()
                {
                    Id = "items",
                    SourceType = "table",
                    Source = "DemoItem",
                    Columns = new Dictionary<string, SectionColumnDef> { ["Id"] = new(), ["Name"] = new() },
                    PageSize = 20,
                    Filters = new Dictionary<string, PageFilterDefinition>
                    {
                        ["Name"] = new() { Type = "like" }
                    }
                }
            }
        };

        var result = await service.LoadPageDataAsync(page, new Dictionary<string, string>
        {
            ["items_Name"] = "a"
        });

        Assert.True(result.ContainsKey("items"));
        Assert.Equal(3, result["items"].Total);
        Assert.Equal(3, result["items"].Rows.Count());
    }

    [Fact]
    public async Task LoadPageDataAsync_AppliesDateRangeAndForeignKey_ForTableSource()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);

        await conn.ExecuteAsync(
            """
            INSERT INTO DemoItem(Id, Name, CategoryId, CreatedAt) VALUES(1, 'A', 10, '2026-03-01 10:00:00');
            INSERT INTO DemoItem(Id, Name, CategoryId, CreatedAt) VALUES(2, 'B', 10, '2026-03-05 11:00:00');
            INSERT INTO DemoItem(Id, Name, CategoryId, CreatedAt) VALUES(3, 'C', 20, '2026-03-06 11:00:00');
            """);

        var service = new PageDataQueryService(conn, NullLogger<PageDataQueryService>.Instance);
        var page = new PageDefinition
        {
            Sections = new List<SectionDefinition>
            {
                new()
                {
                    Id = "items",
                    SourceType = "table",
                    Source = "DemoItem",
                    Columns = new Dictionary<string, SectionColumnDef> { ["Id"] = new(), ["Name"] = new(), ["CategoryId"] = new(), ["CreatedAt"] = new() },
                    PageSize = 20,
                    ForeignKey = "CategoryId",
                    LocalForeignKey = "CategoryId",
                    Filters = new Dictionary<string, PageFilterDefinition>
                    {
                        ["CreatedAt"] = new() { Type = "gte" }
                    }
                }
            }
        };

        var result = await service.LoadPageDataAsync(page, new Dictionary<string, string>
        {
            ["items_CreatedAt"] = "2026-03-01",
            ["items_CreatedAt_to"] = "2026-03-05",
            ["CategoryId"] = "10"
        });

        Assert.Equal(2, result["items"].Total);
        Assert.Equal(2, result["items"].Rows.Count());
    }

    [Fact]
    public async Task LoadPageDataAsync_InjectsCurrentUser_ForCustomSource()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);

        await conn.ExecuteAsync(
            """
            INSERT INTO DemoItem(Id, Name, CategoryId, CreatedAt) VALUES(1, 'Alpha', 10, '2026-03-01 10:00:00');
            INSERT INTO DemoItem(Id, Name, CategoryId, CreatedAt) VALUES(2, 'Beta', 10, '2026-03-02 10:00:00');
            """);

        var service = new PageDataQueryService(conn, NullLogger<PageDataQueryService>.Instance);
        var page = new PageDefinition
        {
            Sections = new List<SectionDefinition>
            {
                new()
                {
                    Id = "my_items",
                    SourceType = "custom",
                    Source = "SELECT * FROM DemoItem WHERE Name = @currentUser",
                    Columns = new Dictionary<string, SectionColumnDef> { ["Id"] = new(), ["Name"] = new() }
                }
            }
        };

        var userCtx = new PageUserContext("Alpha", "Alpha User", "1", new[] { "User" }, false, true);
        var result = await service.LoadPageDataAsync(page, new Dictionary<string, string>(), userCtx);

        Assert.Equal(1, result["my_items"].Total);
        var row = Assert.Single(result["my_items"].Rows);
        Assert.Equal("Alpha", row["Name"]);
    }

    [Fact]
    public async Task LoadPageDataAsync_HandlesIsAdminFlag()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);

        var service = new PageDataQueryService(conn, NullLogger<PageDataQueryService>.Instance);
        var page = new PageDefinition
        {
            Sections = new List<SectionDefinition>
            {
                new()
                {
                    Id = "admin_check",
                    SourceType = "custom",
                    Source = "SELECT @isAdmin AS IsAdminValue",
                    Columns = new Dictionary<string, SectionColumnDef> { ["IsAdminValue"] = new() }
                }
            }
        };

        var adminCtx = new PageUserContext("admin", "Admin", "1", new[] { "Admin" }, true, true);
        var adminResult = await service.LoadPageDataAsync(page, new Dictionary<string, string>(), adminCtx);
        var adminRow = Assert.Single(adminResult["admin_check"].Rows);
        Assert.Equal(1L, adminRow["IsAdminValue"]); // SQLite returns 1 as long

        var userCtx = new PageUserContext("user", "User", "2", new[] { "User" }, false, true);
        var userResult = await service.LoadPageDataAsync(page, new Dictionary<string, string>(), userCtx);
        var userRow = Assert.Single(userResult["admin_check"].Rows);
        Assert.Equal(0L, userRow["IsAdminValue"]);
    }

    private static async Task SeedSchemaAsync(SqliteConnection conn)
    {
        await conn.ExecuteAsync(
            """
            CREATE TABLE DemoItem (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                CategoryId INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """);
    }
}

