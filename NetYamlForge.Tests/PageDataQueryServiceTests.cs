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

