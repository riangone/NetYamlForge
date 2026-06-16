using NetYamlForge.Models;

namespace NetYamlForge.Services.DynamicEntity;

public static class TableDdlBuilder
{
    public static string BuildCreateTableSql(string tableName, EntityDefinition entity, string dbType)
    {
        var pkColumns = entity.GetPrimaryKeyColumns();
        var isSqlServer = string.Equals(dbType, "sqlserver", StringComparison.OrdinalIgnoreCase);
        var isPostgres = string.Equals(dbType, "postgresql", StringComparison.OrdinalIgnoreCase) || string.Equals(dbType, "postgres", StringComparison.OrdinalIgnoreCase);
        var isMySql = string.Equals(dbType, "mysql", StringComparison.OrdinalIgnoreCase) || string.Equals(dbType, "mariadb", StringComparison.OrdinalIgnoreCase);
        var isSqlite = string.Equals(dbType, "sqlite", StringComparison.OrdinalIgnoreCase);

        var colSqls = new List<string>();
        var hasSelfAddedPk = false;

        foreach (var colEntry in entity.Columns)
        {
            var colName = colEntry.Key;
            var colDef = colEntry.Value;

            if (!string.IsNullOrWhiteSpace(colDef.Expression)) continue;

            var sqlType = SqlTypeMapper.MapYamlTypeToSqlType(colDef.Type, dbType);
            var isPk = pkColumns.Contains(colName);

            if (isPk && pkColumns.Count == 1 && colDef.Identity)
            {
                hasSelfAddedPk = true;
                if (isSqlite)
                {
                    colSqls.Add($"\"{colName}\" INTEGER PRIMARY KEY AUTOINCREMENT");
                }
                else if (isSqlServer)
                {
                    colSqls.Add($"\"{colName}\" INT IDENTITY(1,1) PRIMARY KEY");
                }
                else if (isPostgres)
                {
                    colSqls.Add($"\"{colName}\" SERIAL PRIMARY KEY");
                }
                else if (isMySql)
                {
                    colSqls.Add($"\"{colName}\" INT AUTO_INCREMENT PRIMARY KEY");
                }
                else
                {
                    colSqls.Add($"\"{colName}\" INTEGER PRIMARY KEY AUTOINCREMENT");
                }
            }
            else
            {
                var nullableStr = colDef.Required ? "NOT NULL" : "NULL";
                colSqls.Add($"\"{colName}\" {sqlType} {nullableStr}");
            }
        }

        if (!hasSelfAddedPk && pkColumns.Count > 0)
        {
            var pkColsStr = string.Join(", ", pkColumns.Select(c => $"\"{c}\""));
            colSqls.Add($"PRIMARY KEY ({pkColsStr})");
        }

        var escapedTableName = tableName.Trim('"').Replace("\"", "\"\"");
        return $"CREATE TABLE \"{escapedTableName}\" (\n  {string.Join(",\n  ", colSqls)}\n)";
    }
}
