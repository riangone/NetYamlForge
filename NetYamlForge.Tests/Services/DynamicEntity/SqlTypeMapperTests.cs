using NetYamlForge.Services.DynamicEntity;
using Xunit;

namespace NetYamlForge.Tests.Services.DynamicEntity;

public class SqlTypeMapperTests
{
    [Theory]
    [InlineData("int", "INTEGER")]
    [InlineData("integer", "INTEGER")]
    [InlineData("long", "BIGINT")]
    [InlineData("bool", "BOOLEAN")]
    [InlineData("boolean", "BOOLEAN")]
    [InlineData("decimal", "NUMERIC(18,2)")]
    [InlineData("double", "DOUBLE PRECISION")]
    [InlineData("float", "DOUBLE PRECISION")]
    [InlineData("number", "DOUBLE PRECISION")]
    [InlineData("datetime", "TIMESTAMP")]
    [InlineData("date", "TIMESTAMP")]
    [InlineData("string", "TEXT")]
    public void MapYamlTypeToSqlType_Postgres_ReturnsPostgreSqlTypes(string yamlType, string expected)
    {
        var sqlType = SqlTypeMapper.MapYamlTypeToSqlType(yamlType, "postgresql");

        Assert.Equal(expected, sqlType);
    }
}
