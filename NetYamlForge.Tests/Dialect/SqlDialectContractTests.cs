using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NetYamlForge.Services.Dialect;
using Xunit;

namespace NetYamlForge.Tests.Dialect;

public class SqlDialectContractTests
{
    public static IEnumerable<object[]> AllDialects => new[]
    {
        new object[] { new SqliteDialect(), "SQLite" },
        new object[] { new MySqlDialect(), "MySQL" },
        new object[] { new PostgreSqlDialect(), "PostgreSQL" },
        new object[] { new SqlServerDialect(), "SQLServer" }
    };

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void DialectProperties_ShouldNotBeNullOrEmpty(ISqlDialect dialect, string dialectName)
    {
        Assert.False(string.IsNullOrWhiteSpace(dialect.ConcatOperator), $"{dialectName} ConcatOperator should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(dialect.LastInsertIdExpression), $"{dialectName} LastInsertIdExpression should not be empty");
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void AppendNumberedPagination_ShouldAppendLimitOffsetAndParameters(ISqlDialect dialect, string dialectName)
    {
        // Arrange
        var sqlParts = new List<string> { "SELECT * FROM Users" };
        var parameters = new DynamicParameters();
        int pageSize = 20;
        int offset = 40;
        string defaultOrderBy = "Id ASC";

        // Act
        dialect.AppendNumberedPagination(sqlParts, parameters, pageSize, offset, defaultOrderBy);

        // Assert
        Assert.True(sqlParts.Count > 1, $"{dialectName} should append pagination sql clauses");
        
        var sqlCombined = string.Join("", sqlParts);
        
        // Parameter validation
        var paramNames = parameters.ParameterNames.ToList();
        Assert.Contains("PageSize", paramNames);
        Assert.Equal(pageSize, parameters.Get<int>("PageSize"));

        Assert.Contains("Offset", paramNames);
        Assert.Equal(offset, parameters.Get<int>("Offset"));

        // Syntax verification
        if (dialectName == "SQLite" || dialectName == "MySQL" || dialectName == "PostgreSQL")
        {
            Assert.Contains("LIMIT", sqlCombined, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OFFSET", sqlCombined, StringComparison.OrdinalIgnoreCase);
        }
        else if (dialectName == "SQLServer")
        {
            Assert.Contains("ORDER BY", sqlCombined, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OFFSET", sqlCombined, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FETCH NEXT", sqlCombined, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROWS ONLY", sqlCombined, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [MemberData(nameof(AllDialects))]
    public void AppendKeysetPagination_ShouldAppendPageSizeParameterAndLimit(ISqlDialect dialect, string dialectName)
    {
        // Arrange
        var sqlParts = new List<string> { "SELECT * FROM Users WHERE Id > @LastId ORDER BY Id ASC" };
        var parameters = new DynamicParameters();
        int pageSize = 20;

        // Act
        dialect.AppendKeysetPagination(sqlParts, parameters, pageSize);

        // Assert
        Assert.True(sqlParts.Count > 1, $"{dialectName} should append keyset pagination sql clauses");
        
        var sqlCombined = string.Join("", sqlParts);
        
        // Parameter validation
        var paramNames = parameters.ParameterNames.ToList();
        Assert.Contains("PageSize", paramNames);
        Assert.Equal(pageSize, parameters.Get<int>("PageSize"));

        // Keyset pagination should NOT contain OFFSET (except for SQLServer which uses OFFSET 0 ROWS for limit)
        if (dialectName != "SQLServer")
        {
            Assert.DoesNotContain("OFFSET", sqlCombined, StringComparison.OrdinalIgnoreCase);
        }

        // Syntax verification
        if (dialectName == "SQLite" || dialectName == "MySQL" || dialectName == "PostgreSQL")
        {
            Assert.Contains("LIMIT", sqlCombined, StringComparison.OrdinalIgnoreCase);
        }
        else if (dialectName == "SQLServer")
        {
            Assert.Contains("OFFSET 0 ROWS FETCH NEXT", sqlCombined, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROWS ONLY", sqlCombined, StringComparison.OrdinalIgnoreCase);
        }
    }
}
