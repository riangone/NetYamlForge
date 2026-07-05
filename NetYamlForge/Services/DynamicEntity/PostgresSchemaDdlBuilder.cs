using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services.DynamicEntity;

public class PostgresSchemaDdlBuilder : ISchemaDdlBuilder
{
    public bool Supports(string dbType) =>
        string.Equals(dbType, "postgresql", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(dbType, "postgres", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ColumnSchemaInfo>> GetPhysicalColumnsAsync(IDbConnection conn, string tableName)
    {
        var pgRows = await conn.QueryAsync<PostgresColumnRow>(
            """
SELECT
    c.ordinal_position AS "Cid",
    c.column_name AS "Name",
    c.data_type AS "Type",
    (c.is_nullable = 'NO') AS "NotNullBool",
    CASE WHEN pk.column_name IS NOT NULL THEN 1 ELSE 0 END AS "Pk"
FROM information_schema.columns c
LEFT JOIN (
    SELECT kcu.column_name
    FROM information_schema.table_constraints tc
    JOIN information_schema.key_column_usage kcu
      ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
    WHERE tc.constraint_type = 'PRIMARY KEY'
      AND tc.table_name = @TableName
      AND tc.table_schema = current_schema()
) pk ON pk.column_name = c.column_name
WHERE c.table_name = @TableName
  AND c.table_schema = current_schema()
ORDER BY c.ordinal_position
""",
            new { TableName = tableName });

        return pgRows
            .OrderBy(r => r.Cid)
            .Select(r => new ColumnSchemaInfo(
                r.Name,
                DynamicEntitySchemaMigrationService.NormalizeSqlType(r.Type),
                r.NotNullBool,
                r.Pk != 0))
            .ToList();
    }

    public (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName) GenerateSql(MigrationPlan plan, EntityDefinition entity)
    {
        var up = new List<string>();
        var down = new List<string>();
        var tableName = EscapeIdentifier(plan.TableName);

        foreach (var operation in plan.Operations)
        {
            var columnName = EscapeIdentifier(operation.ColumnName);
            switch (operation.OpType)
            {
                case MigrationOpType.AddColumn:
                {
                    var colDef = entity.Columns[operation.ColumnName];
                    var sqlType = operation.NewSqlType ?? "TEXT";
                    if (colDef.Required)
                    {
                        up.Add($"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {sqlType}");
                        up.Add($"UPDATE \"{tableName}\" SET \"{columnName}\" = {GetPostgresDefaultLiteral(sqlType)} WHERE \"{columnName}\" IS NULL");
                        up.Add($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" SET NOT NULL");
                    }
                    else
                    {
                        up.Add($"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {sqlType} NULL");
                    }

                    down.Add($"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\"");
                    break;
                }
                case MigrationOpType.DropColumn:
                {
                    up.Add($"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\"");
                    if (!string.IsNullOrWhiteSpace(operation.OldSqlType))
                    {
                        down.Add($"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {operation.OldSqlType}");
                    }
                    break;
                }
                case MigrationOpType.AlterColumnType:
                {
                    var newType = operation.NewSqlType ?? "TEXT";
                    var oldType = operation.OldSqlType ?? "TEXT";
                    up.Add($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" TYPE {newType} USING \"{columnName}\"::{newType}");
                    down.Add($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" TYPE {oldType} USING \"{columnName}\"::{oldType}");
                    break;
                }
                case MigrationOpType.AlterNullability:
                {
                    var upAction = operation.NewNotNull == true ? "SET NOT NULL" : "DROP NOT NULL";
                    var downAction = operation.NewNotNull == true ? "DROP NOT NULL" : "SET NOT NULL";
                    up.Add($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" {upAction}");
                    down.Add($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" {downAction}");
                    break;
                }
            }
        }

        return (up, down, string.Empty);
    }

    private static string EscapeIdentifier(string name) => name.Replace("\"", "\"\"");

    private static string GetPostgresDefaultLiteral(string sqlType)
    {
        var upper = sqlType.Trim().ToUpperInvariant();
        if (upper is "BOOLEAN" or "BOOL")
        {
            return "false";
        }

        return DynamicEntitySchemaMigrationService.NormalizeSqlType(sqlType) switch
        {
            "INTEGER" or "BIGINT" or "NUMERIC" => "0",
            "TIMESTAMP" => "now()",
            _ => "''"
        };
    }

    private sealed class PostgresColumnRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool NotNullBool { get; set; }
        public int Pk { get; set; }
    }
}
