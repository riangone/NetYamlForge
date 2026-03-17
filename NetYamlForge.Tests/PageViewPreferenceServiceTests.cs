using System.Data;
using Dapper;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class PageViewPreferenceServiceTests
{
    [Fact]
    public async Task SaveViewAsync_SetsOnlyOneDefault_ForSamePageAndUser()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);

        var service = new PageViewPreferenceService(
            conn,
            new NoopAuditLogService(),
            NullLogger<PageViewPreferenceService>.Instance);

        var first = await service.SaveViewAsync(
            "p1", "Dashboard", "alice", "View-A",
            new Dictionary<string, string> { ["status"] = "Open" },
            makeDefault: true);
        var second = await service.SaveViewAsync(
            "p1", "Dashboard", "alice", "View-B",
            new Dictionary<string, string> { ["status"] = "Closed" },
            makeDefault: true);

        Assert.True(first.ok);
        Assert.True(second.ok);

        var rows = (await conn.QueryAsync<(string ViewName, int IsDefault)>(
            @"SELECT ViewName, IsDefault FROM AppUserSavedView
              WHERE ProjectName = 'p1' AND PageName = 'Dashboard' AND UserName = 'alice'
              ORDER BY ViewName ASC")).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(0, rows.Single(r => r.ViewName == "View-A").IsDefault);
        Assert.Equal(1, rows.Single(r => r.ViewName == "View-B").IsDefault);
    }

    [Fact]
    public async Task LoadSavedViewsAsync_ReturnsParsedFilters()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);
        await conn.ExecuteAsync(
            @"INSERT INTO AppUserSavedView
              (ProjectName, PageName, UserName, ViewName, FiltersJson, IsDefault, CreatedAt, UpdatedAt)
              VALUES('p1','Dashboard','alice','DefaultView','{""status"":""Open""}',1,datetime('now'),datetime('now'))");

        var service = new PageViewPreferenceService(
            conn,
            new NoopAuditLogService(),
            NullLogger<PageViewPreferenceService>.Instance);

        var rows = await service.LoadSavedViewsAsync("p1", "Dashboard", "alice");

        Assert.Single(rows);
        Assert.Equal("DefaultView", rows[0].ViewName);
        Assert.True(rows[0].IsDefault);
        Assert.Equal("Open", rows[0].Filters["status"]);
    }

    [Fact]
    public async Task DeleteViewAsync_RemovesTargetViewOnly()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);
        await conn.ExecuteAsync(
            """
            INSERT INTO AppUserSavedView (ProjectName, PageName, UserName, ViewName, FiltersJson, IsDefault, CreatedAt, UpdatedAt)
            VALUES('p1','Dashboard','alice','KeepMe','{}',0,datetime('now'),datetime('now'));
            INSERT INTO AppUserSavedView (ProjectName, PageName, UserName, ViewName, FiltersJson, IsDefault, CreatedAt, UpdatedAt)
            VALUES('p1','Dashboard','alice','DeleteMe','{}',0,datetime('now'),datetime('now'));
            """);

        var service = new PageViewPreferenceService(
            conn,
            new NoopAuditLogService(),
            NullLogger<PageViewPreferenceService>.Instance);

        await service.DeleteViewAsync("p1", "Dashboard", "alice", "DeleteMe");

        var names = (await conn.QueryAsync<string>(
            @"SELECT ViewName FROM AppUserSavedView
              WHERE ProjectName='p1' AND PageName='Dashboard' AND UserName='alice'
              ORDER BY ViewName")).ToList();

        Assert.Single(names);
        Assert.Equal("KeepMe", names[0]);
    }

    private static async Task SeedSchemaAsync(SqliteConnection conn)
    {
        await conn.ExecuteAsync(
            """
            CREATE TABLE AppUserSavedView (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectName TEXT NOT NULL,
                PageName TEXT NOT NULL,
                UserName TEXT NOT NULL,
                ViewName TEXT NOT NULL,
                FiltersJson TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UNIQUE(ProjectName, PageName, UserName, ViewName)
            );
            """);
    }

    private sealed class NoopAuditLogService : IAuditLogService
    {
        public Task WriteAsync(
            string action,
            string? entity = null,
            string? detail = null,
            string? userName = null,
            IDbConnection? connection = null,
            IDbTransaction? transaction = null) => Task.CompletedTask;
    }
}

