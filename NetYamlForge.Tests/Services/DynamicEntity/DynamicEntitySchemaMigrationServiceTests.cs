using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NetYamlForge.Models;
using NetYamlForge.Services.DynamicEntity;
using Xunit;

namespace NetYamlForge.Tests.Services.DynamicEntity;

public class DynamicEntitySchemaMigrationServiceTests
{
    [Fact]
    public void BuildPlan_DetectsAddDropAndTypeChange()
    {
        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Name"] = new ColumnDefinition { Type = "int" };
            columns["NewCode"] = new ColumnDefinition { Type = "string" };
        });
        var physical = new[]
        {
            new ColumnSchemaInfo("Id", "INTEGER", true, true),
            new ColumnSchemaInfo("Name", "TEXT", false, false),
            new ColumnSchemaInfo("Legacy", "TEXT", false, false),
        };

        var plan = sut.BuildPlan("customer", entity, physical, "sqlite");

        Assert.Contains(plan.Operations, o => o.OpType == MigrationOpType.AddColumn && o.ColumnName == "NewCode");
        Assert.Contains(plan.Operations, o => o.OpType == MigrationOpType.DropColumn && o.ColumnName == "Legacy");
        Assert.Contains(plan.Operations, o => o.OpType == MigrationOpType.AlterColumnType && o.ColumnName == "Name");
    }

    [Fact]
    public void GenerateSql_AddColumnOnly_ReturnsAlterTable()
    {
        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Name"] = new ColumnDefinition { Type = "string" };
        });
        var plan = new MigrationPlan(
            "customer",
            "Customer",
            new[] { new MigrationOperation(MigrationOpType.AddColumn, "Name", null, "TEXT", false) });

        var (upSql, downSql, backupTableName) = sut.GenerateSql(plan, entity, "sqlite");

        Assert.Single(upSql);
        Assert.Contains("ALTER TABLE \"Customer\" ADD COLUMN \"Name\" TEXT NULL", upSql[0]);
        Assert.Single(downSql);
        Assert.Equal(string.Empty, backupTableName);
    }

    [Fact]
    public void GenerateSql_DestructivePlan_ReturnsRebuildSteps()
    {
        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Name"] = new ColumnDefinition { Type = "int" };
        });
        var plan = new MigrationPlan(
            "customer",
            "Customer",
            new[]
            {
                new MigrationOperation(MigrationOpType.DropColumn, "Legacy", "TEXT", null, null),
                new MigrationOperation(MigrationOpType.AlterColumnType, "Name", "TEXT", "INTEGER", null),
            });

        var (upSql, downSql, backupTableName) = sut.GenerateSql(plan, entity, "sqlite");

        Assert.NotEmpty(backupTableName);
        Assert.Contains(upSql, s => s.Contains("ALTER TABLE \"Customer\" RENAME TO \"Customer__bak_", StringComparison.Ordinal));
        Assert.Contains(upSql, s => s.Contains("CREATE TABLE \"Customer\"", StringComparison.Ordinal));
        Assert.Contains(upSql, s => s.Contains("INSERT INTO \"Customer\"", StringComparison.Ordinal)
            && s.Contains("CAST(\"Name\" AS INTEGER)", StringComparison.Ordinal));
        Assert.Equal(2, downSql.Count);
    }

    [Fact]
    public async Task ApplyAndRollback_RebuildsTableAndRestoresDroppedColumnData()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
CREATE TABLE "Customer" (
  "Id" INTEGER PRIMARY KEY AUTOINCREMENT,
  "Name" TEXT NULL,
  "Legacy" TEXT NULL
);
INSERT INTO "Customer" ("Name", "Legacy") VALUES ('Alice', 'keep-me');
""");

        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Name"] = new ColumnDefinition { Type = "string" };
        });
        var physical = await sut.GetPhysicalColumnsAsync(conn, "Customer");
        var plan = sut.BuildPlan("customer", entity, physical, "sqlite");

        var result = await sut.ApplyAsync(conn, "test-project", plan, entity, "sqlite", dryRun: false);

        Assert.True(result.Applied);
        var columnsAfterApply = await sut.GetPhysicalColumnsAsync(conn, "Customer");
        Assert.DoesNotContain(columnsAfterApply, c => c.Name == "Legacy");
        var preserved = await conn.QuerySingleAsync<(int Id, string Name)>("SELECT Id, Name FROM \"Customer\"");
        Assert.Equal(1, preserved.Id);
        Assert.Equal("Alice", preserved.Name);
        var migration = await conn.QuerySingleAsync<dynamic>(
            "SELECT up_sql, down_sql, applied_at FROM _nyf_migrations WHERE id = @Id",
            new { Id = result.MigrationId });
        Assert.False(string.IsNullOrWhiteSpace((string)migration.up_sql));
        Assert.False(string.IsNullOrWhiteSpace((string)migration.down_sql));
        Assert.False(string.IsNullOrWhiteSpace((string)migration.applied_at));

        await sut.RollbackAsync(conn, result.MigrationId);

        var columnsAfterRollback = await sut.GetPhysicalColumnsAsync(conn, "Customer");
        Assert.Contains(columnsAfterRollback, c => c.Name == "Legacy");
        var restored = await conn.QuerySingleAsync<(int Id, string Name, string Legacy)>(
            "SELECT Id, Name, Legacy FROM \"Customer\"");
        Assert.Equal(1, restored.Id);
        Assert.Equal("Alice", restored.Name);
        Assert.Equal("keep-me", restored.Legacy);
        var rolledBackAt = await conn.QuerySingleAsync<string>(
            "SELECT rolled_back_at FROM _nyf_migrations WHERE id = @Id",
            new { Id = result.MigrationId });
        Assert.False(string.IsNullOrWhiteSpace(rolledBackAt));
    }

    [Fact]
    public async Task ApplyAsync_DryRun_ReturnsSqlWithoutExecuting()
    {
        await using var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        await conn.ExecuteAsync("CREATE TABLE \"Customer\" (\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT)");

        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Name"] = new ColumnDefinition { Type = "string" };
        });
        var physical = await sut.GetPhysicalColumnsAsync(conn, "Customer");
        var plan = sut.BuildPlan("customer", entity, physical, "sqlite");

        var result = await sut.ApplyAsync(conn, "test-project", plan, entity, "sqlite", dryRun: true);

        Assert.False(result.Applied);
        Assert.Single(result.ExecutedSql);
        var columns = await sut.GetPhysicalColumnsAsync(conn, "Customer");
        Assert.DoesNotContain(columns, c => c.Name == "Name");
    }

    [Fact]
    public void BuildPlan_Postgres_PreservesPostgreSqlTargetTypes()
    {
        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["IsActive"] = new ColumnDefinition { Type = "bool", Required = true };
            columns["CreatedAt"] = new ColumnDefinition { Type = "datetime" };
        });
        var physical = new[]
        {
            new ColumnSchemaInfo("Id", "integer", true, true),
        };

        var plan = sut.BuildPlan("customer", entity, physical, "postgresql");

        Assert.Contains(plan.Operations, o => o.OpType == MigrationOpType.AddColumn
            && o.ColumnName == "IsActive"
            && o.NewSqlType == "BOOLEAN"
            && o.NewNotNull == true);
        Assert.Contains(plan.Operations, o => o.OpType == MigrationOpType.AddColumn
            && o.ColumnName == "CreatedAt"
            && o.NewSqlType == "TIMESTAMP");
    }

    [Fact]
    public void GenerateSql_Postgres_AddRequiredColumn_ReturnsBackfillSequence()
    {
        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["IsActive"] = new ColumnDefinition { Type = "bool", Required = true };
        });
        var plan = new MigrationPlan(
            "customer",
            "Customer",
            new[] { new MigrationOperation(MigrationOpType.AddColumn, "IsActive", null, "BOOLEAN", true) });

        var (upSql, downSql, backupTableName) = sut.GenerateSql(plan, entity, "postgresql");

        Assert.Equal(string.Empty, backupTableName);
        Assert.False(plan.RequiresTableRebuildFor("postgresql"));
        Assert.Equal(new[]
        {
            "ALTER TABLE \"Customer\" ADD COLUMN \"IsActive\" BOOLEAN",
            "UPDATE \"Customer\" SET \"IsActive\" = false WHERE \"IsActive\" IS NULL",
            "ALTER TABLE \"Customer\" ALTER COLUMN \"IsActive\" SET NOT NULL"
        }, upSql);
        Assert.Equal(new[] { "ALTER TABLE \"Customer\" DROP COLUMN \"IsActive\"" }, downSql);
    }

    [Fact]
    public void GenerateSql_Postgres_DropColumn_ReturnsInPlaceAlter()
    {
        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
        });
        var plan = new MigrationPlan(
            "customer",
            "Customer",
            new[] { new MigrationOperation(MigrationOpType.DropColumn, "Legacy", "TEXT", null, null) });

        var (upSql, downSql, backupTableName) = sut.GenerateSql(plan, entity, "postgresql");

        Assert.Equal(string.Empty, backupTableName);
        Assert.False(plan.RequiresTableRebuildFor("postgresql"));
        Assert.Equal(new[] { "ALTER TABLE \"Customer\" DROP COLUMN \"Legacy\"" }, upSql);
        Assert.Equal(new[] { "ALTER TABLE \"Customer\" ADD COLUMN \"Legacy\" TEXT" }, downSql);
    }

    [Fact]
    public void GenerateSql_Postgres_AlterColumnType_ReturnsUsingCast()
    {
        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Amount"] = new ColumnDefinition { Type = "decimal" };
        });
        var plan = new MigrationPlan(
            "customer",
            "Customer",
            new[] { new MigrationOperation(MigrationOpType.AlterColumnType, "Amount", "TEXT", "NUMERIC(18,2)", null) });

        var (upSql, downSql, backupTableName) = sut.GenerateSql(plan, entity, "postgresql");

        Assert.Equal(string.Empty, backupTableName);
        Assert.False(plan.RequiresTableRebuildFor("postgresql"));
        Assert.Equal(new[] { "ALTER TABLE \"Customer\" ALTER COLUMN \"Amount\" TYPE NUMERIC(18,2) USING \"Amount\"::NUMERIC(18,2)" }, upSql);
        Assert.Equal(new[] { "ALTER TABLE \"Customer\" ALTER COLUMN \"Amount\" TYPE TEXT USING \"Amount\"::TEXT" }, downSql);
    }

    [Fact]
    public void GenerateSql_Postgres_AlterNullability_ReturnsSetAndDropNotNull()
    {
        var sut = CreateService();
        var entity = CreateEntity(columns =>
        {
            columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
            columns["Name"] = new ColumnDefinition { Type = "string", Required = true };
        });
        var plan = new MigrationPlan(
            "customer",
            "Customer",
            new[] { new MigrationOperation(MigrationOpType.AlterNullability, "Name", "TEXT", "TEXT", true) });

        var (upSql, downSql, backupTableName) = sut.GenerateSql(plan, entity, "postgresql");

        Assert.Equal(string.Empty, backupTableName);
        Assert.False(plan.RequiresTableRebuildFor("postgresql"));
        Assert.Equal(new[] { "ALTER TABLE \"Customer\" ALTER COLUMN \"Name\" SET NOT NULL" }, upSql);
        Assert.Equal(new[] { "ALTER TABLE \"Customer\" ALTER COLUMN \"Name\" DROP NOT NULL" }, downSql);
    }

    [Theory]
    [InlineData("character varying", "TEXT")]
    [InlineData("character varying(255)", "TEXT")]
    [InlineData("double precision", "NUMERIC")]
    [InlineData("timestamp without time zone", "TIMESTAMP")]
    [InlineData("timestamp with time zone", "TIMESTAMP")]
    [InlineData("boolean", "INTEGER")]
    [InlineData("numeric", "NUMERIC")]
    [InlineData("integer", "INTEGER")]
    public void NormalizeSqlType_PostgresInformationSchemaTypes_ReturnsCanonicalType(string dataType, string expected)
    {
        var normalized = DynamicEntitySchemaMigrationService.NormalizeSqlType(dataType);

        Assert.Equal(expected, normalized);
    }

    private static DynamicEntitySchemaMigrationService CreateService() =>
        new(NullLogger<DynamicEntitySchemaMigrationService>.Instance);

    private static EntityDefinition CreateEntity(Action<Dictionary<string, ColumnDefinition>> configure)
    {
        var entity = new EntityDefinition
        {
            Table = "Customer",
            Key = "Id",
            DisplayName = "Customer"
        };
        configure(entity.Columns);
        return entity;
    }
}
