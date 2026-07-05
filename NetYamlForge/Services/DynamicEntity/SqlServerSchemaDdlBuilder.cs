using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services.DynamicEntity;

public class SqlServerSchemaDdlBuilder : ISchemaDdlBuilder
{
    public bool Supports(string dbType) =>
        string.Equals(dbType, "sqlserver", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(dbType, "mssql", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ColumnSchemaInfo>> GetPhysicalColumnsAsync(IDbConnection conn, string tableName)
    {
        var mssqlRows = await conn.QueryAsync<SqlServerColumnRow>(
            """
SELECT
    c.ORDINAL_POSITION AS Cid,
    c.COLUMN_NAME AS Name,
    c.DATA_TYPE AS Type,
    CASE WHEN c.IS_NULLABLE = 'NO' THEN 1 ELSE 0 END AS NotNullBool,
    CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS Pk
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN (
    SELECT ku.COLUMN_NAME, ku.TABLE_NAME
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
      ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
) pk ON pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
WHERE c.TABLE_NAME = @TableName
ORDER BY c.ORDINAL_POSITION
""",
            new { TableName = tableName });

        return mssqlRows
            .OrderBy(r => r.Cid)
            .Select(r => new ColumnSchemaInfo(
                r.Name,
                DynamicEntitySchemaMigrationService.NormalizeSqlType(r.Type),
                r.NotNullBool != 0,
                r.Pk != 0))
            .ToList();
    }

    public (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName) GenerateSql(MigrationPlan plan, EntityDefinition entity)
    {
        var up = new List<string>();
        var down = new List<string>();
        var tableName = plan.TableName.Replace("]", "]]");

        foreach (var operation in plan.Operations)
        {
            var columnName = operation.ColumnName.Replace("]", "]]");
            switch (operation.OpType)
            {
                case MigrationOpType.AddColumn:
                {
                    var colDef = entity.Columns[operation.ColumnName];
                    var sqlType = operation.NewSqlType ?? "NVARCHAR(MAX)";
                    if (colDef.Required)
                    {
                        up.Add($"ALTER TABLE [{tableName}] ADD [{columnName}] {sqlType} NULL");
                        up.Add($"UPDATE [{tableName}] SET [{columnName}] = {GetSqlServerDefaultLiteral(sqlType)} WHERE [{columnName}] IS NULL");
                        up.Add($"ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] {sqlType} NOT NULL");
                    }
                    else
                    {
                        up.Add($"ALTER TABLE [{tableName}] ADD [{columnName}] {sqlType} NULL");
                    }

                    down.Add($"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}]");
                    break;
                }
                case MigrationOpType.DropColumn:
                {
                    up.Add($"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}]");
                    if (!string.IsNullOrWhiteSpace(operation.OldSqlType))
                    {
                        down.Add($"ALTER TABLE [{tableName}] ADD [{columnName}] {operation.OldSqlType} NULL");
                    }
                    break;
                }
                case MigrationOpType.AlterColumnType:
                {
                    var newType = operation.NewSqlType ?? "NVARCHAR(MAX)";
                    var oldType = operation.OldSqlType ?? "NVARCHAR(MAX)";
                    up.Add($"ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] {newType}");
                    down.Add($"ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] {oldType}");
                    break;
                }
                case MigrationOpType.AlterNullability:
                {
                    var type = operation.NewSqlType ?? "NVARCHAR(MAX)";
                    var upAction = operation.NewNotNull == true ? "NOT NULL" : "NULL";
                    var downAction = operation.NewNotNull == true ? "NULL" : "NOT NULL";
                    up.Add($"ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] {type} {upAction}");
                    down.Add($"ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] {type} {downAction}");
                    break;
                }
            }
        }

        return (up, down, string.Empty);
    }

    private static string GetSqlServerDefaultLiteral(string sqlType)
    {
        var upper = sqlType.Trim().ToUpperInvariant();
        if (upper is "BOOLEAN" or "BOOL" or "BIT")
        {
            return "0";
        }

        return DynamicEntitySchemaMigrationService.NormalizeSqlType(sqlType) switch
        {
            "INTEGER" or "BIGINT" or "NUMERIC" => "0",
            "TIMESTAMP" or "DATETIME" => "GETDATE()",
            _ => "''"
        };
    }

    private sealed class SqlServerColumnRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int NotNullBool { get; set; }
        public int Pk { get; set; }
    }
}
