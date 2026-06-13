namespace NetYamlForge.Services.DynamicEntity;

public static class SqlTypeMapper
{
    public static string MapYamlTypeToSqlType(string yamlType, string dbType)
    {
        var isSqlite = string.Equals(dbType, "sqlite", StringComparison.OrdinalIgnoreCase);
        if (isSqlite)
        {
            return yamlType.ToLowerInvariant() switch
            {
                "int" or "integer" or "long" or "bool" or "boolean" => "INTEGER",
                "decimal" or "double" or "float" or "number" => "NUMERIC",
                _ => "TEXT"
            };
        }

        return yamlType.ToLowerInvariant() switch
        {
            "int" or "integer" => "INT",
            "long" => "BIGINT",
            "bool" or "boolean" => "BIT",
            "decimal" => "DECIMAL(18,2)",
            "double" or "float" or "number" => "DOUBLE PRECISION",
            "datetime" or "date" => "DATETIME",
            _ => "NVARCHAR(MAX)"
        };
    }
}
