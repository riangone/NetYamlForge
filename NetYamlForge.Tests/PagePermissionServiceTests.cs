using Dapper;
using NetYamlForge.Services.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NetYamlForge.Tests;

public class PagePermissionServiceTests
{
    [Fact]
    public async Task CanWritePageAsync_ReturnsTrue_WhenMatchingRoleHasWritePermission()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);

        await conn.ExecuteAsync(@"
INSERT INTO AppUserRole(UserName, RoleName, CreatedAt) VALUES('u1', 'SalesRep', datetime('now'));
INSERT INTO AppRolePermission(ProjectName, RoleName, ResourceType, ResourceName, CanRead, CanWrite, CreatedAt, UpdatedAt)
VALUES('salesforce-crm', 'SalesRep', 'page', 'LeadInbox', 1, 1, datetime('now'), datetime('now'));");

        var sut = new PagePermissionService(conn, NullLogger<PagePermissionService>.Instance);

        var canWrite = await sut.CanWritePageAsync("salesforce-crm", "LeadInbox", "u1", isAdmin: false);

        Assert.True(canWrite);
    }

    [Fact]
    public async Task CanWritePageAsync_ReturnsFalse_WhenOnlyReadPermissionExists()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);

        await conn.ExecuteAsync(@"
INSERT INTO AppUserRole(UserName, RoleName, CreatedAt) VALUES('u2', 'ReadOnly', datetime('now'));
INSERT INTO AppRolePermission(ProjectName, RoleName, ResourceType, ResourceName, CanRead, CanWrite, CreatedAt, UpdatedAt)
VALUES('salesforce-crm', 'ReadOnly', 'page', 'LeadInbox', 1, 0, datetime('now'), datetime('now'));");

        var sut = new PagePermissionService(conn, NullLogger<PagePermissionService>.Instance);

        var canWrite = await sut.CanWritePageAsync("salesforce-crm", "LeadInbox", "u2", isAdmin: false);

        Assert.False(canWrite);
    }

    [Fact]
    public async Task CanWriteFieldAsync_UsesWildcardFallback_WhenExactFieldMissing()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await SeedSchemaAsync(conn);

        await conn.ExecuteAsync(@"
INSERT INTO AppUserRole(UserName, RoleName, CreatedAt) VALUES('u3', 'SalesRep', datetime('now'));
INSERT INTO AppRolePermission(ProjectName, RoleName, ResourceType, ResourceName, CanRead, CanWrite, CreatedAt, UpdatedAt)
VALUES('salesforce-crm', 'SalesRep', 'field', '*', 1, 1, datetime('now'), datetime('now'));");

        var sut = new PagePermissionService(conn, NullLogger<PagePermissionService>.Instance);

        var canWrite = await sut.CanWriteFieldAsync("salesforce-crm", "LeadInbox", "OwnerUserName", "u3", isAdmin: false);

        Assert.True(canWrite);
    }

    [Fact]
    public async Task CanReadPageAsync_ReturnsTrue_WhenPermissionTablesDoNotExist()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();

        var sut = new PagePermissionService(conn, NullLogger<PagePermissionService>.Instance);

        var canRead = await sut.CanReadPageAsync("salesforce-crm", "LeadInbox", "u4", isAdmin: false);

        Assert.True(canRead);
    }

    private static async Task SeedSchemaAsync(SqliteConnection conn)
    {
        await conn.ExecuteAsync(@"
CREATE TABLE AppUserRole (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserName TEXT NOT NULL,
    RoleName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE AppRolePermission (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectName TEXT NOT NULL,
    RoleName TEXT NOT NULL,
    ResourceType TEXT NOT NULL,
    ResourceName TEXT NOT NULL,
    CanRead INTEGER NOT NULL DEFAULT 1,
    CanWrite INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);");
    }
}
