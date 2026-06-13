using NetYamlForge.Models;
using NetYamlForge.Services.DynamicEntity;
using Xunit;

namespace NetYamlForge.Tests.Services.DynamicEntity;

public class TableDdlBuilderTests
{
    [Fact]
    public void BuildCreateTableSql_Postgres_UsesPostgreSqlTypes()
    {
        var entity = new EntityDefinition
        {
            Table = "orders",
            Key = "Id"
        };
        entity.Columns["Id"] = new ColumnDefinition { Type = "int", Identity = true, Required = true };
        entity.Columns["IsActive"] = new ColumnDefinition { Type = "bool", Required = true };
        entity.Columns["CreatedAt"] = new ColumnDefinition { Type = "datetime", Required = true };
        entity.Columns["Total"] = new ColumnDefinition { Type = "decimal" };
        entity.Columns["Ratio"] = new ColumnDefinition { Type = "double" };
        entity.Columns["Notes"] = new ColumnDefinition { Type = "string" };

        var sql = TableDdlBuilder.BuildCreateTableSql("orders", entity, "postgresql");

        Assert.Contains("\"Id\" SERIAL PRIMARY KEY", sql);
        Assert.Contains("\"IsActive\" BOOLEAN NOT NULL", sql);
        Assert.Contains("\"CreatedAt\" TIMESTAMP NOT NULL", sql);
        Assert.Contains("\"Total\" NUMERIC(18,2) NULL", sql);
        Assert.Contains("\"Ratio\" DOUBLE PRECISION NULL", sql);
        Assert.Contains("\"Notes\" TEXT NULL", sql);
        Assert.DoesNotContain("NVARCHAR", sql);
        Assert.DoesNotContain("BIT", sql);
        Assert.DoesNotContain("DATETIME", sql);
    }
}
