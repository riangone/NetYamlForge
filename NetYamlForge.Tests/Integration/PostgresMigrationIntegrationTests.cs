// ファイル概要：PostgreSQL 実DB を使ったスキーママイグレーション統合テスト。
// POSTGRES_TEST_CONNSTR 環境変数が設定されていない場合はすべてのテストをスキップします。
// 設定されている場合は一時スキーマを作成してテストを実行し、終了後にクリーンアップします。

#pragma warning disable DCS001 // test-only DDL uses schema-qualified identifiers that require string interpolation

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Models;
using NetYamlForge.Services.DynamicEntity;
using Xunit;

namespace NetYamlForge.Tests.Integration;

/// <summary>
/// PostgreSQL 実DB を使ったスキーママイグレーションの統合テスト。
/// <c>POSTGRES_TEST_CONNSTR</c> 環境変数が未設定の場合はすべてスキップされます。
/// </summary>
public class PostgresMigrationIntegrationTests : IAsyncLifetime
{
    private const string EnvVar = "POSTGRES_TEST_CONNSTR";

    private readonly string? _connStr;
    private readonly string _schema;
    private IDbConnection? _conn;

    public PostgresMigrationIntegrationTests()
    {
        _connStr = Environment.GetEnvironmentVariable(EnvVar);
        _schema = $"nyf_test_{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_connStr))
            return;

        _conn = CreateConnection(_connStr);
        _conn.Open();

        await _conn.ExecuteAsync($"CREATE SCHEMA \"{_schema}\"");
        await _conn.ExecuteAsync($"SET search_path TO \"{_schema}\"");
    }

    public async Task DisposeAsync()
    {
        if (_conn != null && !string.IsNullOrEmpty(_connStr))
        {
            try
            {
                await _conn.ExecuteAsync($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE");
            }
            catch
            {
                // best-effort cleanup
            }
            finally
            {
                _conn.Dispose();
                _conn = null;
            }
        }
    }

    [Fact]
    public async Task Schema_Drift_Detection_Postgres()
    {
        if (string.IsNullOrEmpty(_connStr))
        {
            return;
        }

        var conn = _conn!;

        // Create base table with 2 columns
        await conn.ExecuteAsync($@"
            CREATE TABLE ""{_schema}"".""Customer"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""Name"" TEXT NULL
            )");

        var sut = CreateService();

        // YAML definition adds a 3rd column
        var entity = CreateEntity(columns =>
        {
            columns["Id"]   = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Name"] = new ColumnDefinition { Type = "string" };
            columns["Code"] = new ColumnDefinition { Type = "string" };
        });

        // GetPhysicalColumnsAsync accepts a schema-qualified table name via "schema"."table"
        var physical = await sut.GetPhysicalColumnsAsync(conn, $"{_schema}\".\"Customer", "postgresql");
        var plan = sut.BuildPlan("customer", entity, physical, "postgresql");

        Xunit.Assert.True(plan.Operations.Count > 0, "Expected drift to be detected (Code column missing from DB).");
        Xunit.Assert.Contains(plan.Operations, o =>
            o.OpType == MigrationOpType.AddColumn && o.ColumnName == "Code");
    }

    [Fact]
    public async Task Apply_Migration_AddColumn_Postgres()
    {
        if (string.IsNullOrEmpty(_connStr))
        {
            return;
        }

        var conn = _conn!;

        await conn.ExecuteAsync($@"
            CREATE TABLE ""{_schema}"".""Product"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""Name"" TEXT NULL
            )");

        var sut = CreateService();

        // YAML adds a new column
        var entity = CreateEntity(columns =>
        {
            columns["Id"]    = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Name"]  = new ColumnDefinition { Type = "string" };
            columns["Price"] = new ColumnDefinition { Type = "decimal" };
        });

        entity.Table = "Product";

        var physical = await sut.GetPhysicalColumnsAsync(conn, $"{_schema}\".\"Product", "postgresql");
        var plan = sut.BuildPlan("product", entity, physical, "postgresql");
        Xunit.Assert.True(plan.Operations.Count > 0);

        var result = await sut.ApplyAsync(conn, "test-project", plan, entity, "postgresql", dryRun: false);

        Xunit.Assert.True(result.Applied);
        var columnsAfter = await sut.GetPhysicalColumnsAsync(conn, $"{_schema}\".\"Product", "postgresql");
        Xunit.Assert.Contains(columnsAfter, c => string.Equals(c.Name, "Price", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Migration_Rollback_Postgres()
    {
        if (string.IsNullOrEmpty(_connStr))
        {
            return;
        }

        var conn = _conn!;

        await conn.ExecuteAsync($@"
            CREATE TABLE ""{_schema}"".""Order"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""Amount"" TEXT NULL
            )");

        var sut = CreateService();

        // Add a column then roll it back
        var entity = CreateEntity(columns =>
        {
            columns["Id"]     = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Amount"] = new ColumnDefinition { Type = "string" };
            columns["Status"] = new ColumnDefinition { Type = "string" };
        });

        entity.Table = "Order";

        var physical = await sut.GetPhysicalColumnsAsync(conn, $"{_schema}\".\"Order", "postgresql");
        var plan = sut.BuildPlan("order", entity, physical, "postgresql");
        Xunit.Assert.True(plan.Operations.Count > 0);

        var result = await sut.ApplyAsync(conn, "test-project", plan, entity, "postgresql", dryRun: false);
        Xunit.Assert.True(result.Applied);

        var columnsAfterApply = await sut.GetPhysicalColumnsAsync(conn, $"{_schema}\".\"Order", "postgresql");
        Xunit.Assert.Contains(columnsAfterApply, c => string.Equals(c.Name, "Status", StringComparison.OrdinalIgnoreCase));

        // Rollback
        await sut.RollbackAsync(conn, result.MigrationId);

        var columnsAfterRollback = await sut.GetPhysicalColumnsAsync(conn, $"{_schema}\".\"Order", "postgresql");
        Xunit.Assert.DoesNotContain(columnsAfterRollback, c => string.Equals(c.Name, "Status", StringComparison.OrdinalIgnoreCase));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DynamicEntitySchemaMigrationService CreateService() =>
        new(NullLogger<DynamicEntitySchemaMigrationService>.Instance);

    private static EntityDefinition CreateEntity(Action<Dictionary<string, ColumnDefinition>> configure)
    {
        var entity = new EntityDefinition
        {
            Table       = "Customer",
            Key         = "Id",
            DisplayName = "Customer"
        };
        configure(entity.Columns);
        return entity;
    }

    private static IDbConnection CreateConnection(string connStr)
    {
        // Use Npgsql via reflection to avoid a hard compile-time dependency on Npgsql
        // in the test project when no Postgres NuGet package is referenced.
        // If Npgsql is not available at runtime the test will fail with a clear exception.
        try
        {
            var npgsqlType = Type.GetType("Npgsql.NpgsqlConnection, Npgsql")
                ?? throw new InvalidOperationException(
                    "Npgsql assembly not found. Add <PackageReference Include=\"Npgsql\" /> to the test project.");

            return (IDbConnection)Activator.CreateInstance(npgsqlType, connStr)!;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Failed to create NpgsqlConnection. Ensure Npgsql is referenced in the test project.", ex);
        }
    }
}

#pragma warning restore DCS001
