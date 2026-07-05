using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services.DynamicEntity;

public class MySqlSchemaDdlBuilder : ISchemaDdlBuilder
{
    public bool Supports(string dbType) =>
        string.Equals(dbType, "mysql", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(dbType, "mariadb", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ColumnSchemaInfo>> GetPhysicalColumnsAsync(IDbConnection conn, string tableName)
    {
        var mysqlRows = await conn.QueryAsync<MySqlColumnRow>(
            """
SELECT
    ordinal_position AS Cid,
    column_name AS Name,
    data_type AS Type,
    CASE WHEN is_nullable = 'NO' THEN 1 ELSE 0 END AS NotNullBool,
    CASE WHEN column_key = 'PRI' THEN 1 ELSE 0 END AS Pk
FROM information_schema.columns
WHERE table_name = @TableName
  AND table_schema = DATABASE()
ORDER BY ordinal_position
""",
            new { TableName = tableName });

        return mysqlRows
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
        var tableName = plan.TableName.Replace("`", "``");

        foreach (var operation in plan.Operations)
        {
            var columnName = operation.ColumnName.Replace("`", "``");
            switch (operation.OpType)
            {
                case MigrationOpType.AddColumn:
                {
                    var colDef = entity.Columns[operation.ColumnName];
                    var sqlType = operation.NewSqlType ?? "VARCHAR(255)";
                    if (colDef.Required)
                    {
                        up.Add($"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {sqlType} NULL");
                        up.Add($"UPDATE `{tableName}` SET `{columnName}` = {GetMySqlDefaultLiteral(sqlType)} WHERE `{columnName}` IS NULL");
                        up.Add($"ALTER TABLE `{tableName}` MODIFY COLUMN `{columnName}` {sqlType} NOT NULL");
                    }
                    else
                    {
                        up.Add($"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {sqlType} NULL");
                    }

                    down.Add($"ALTER TABLE `{tableName}` DROP COLUMN `{columnName}`");
                    break;
                }
                case MigrationOpType.DropColumn:
                {
                    up.Add($"ALTER TABLE `{tableName}` DROP COLUMN `{columnName}`");
                    if (!string.IsNullOrWhiteSpace(operation.OldSqlType))
                    {
                        down.Add($"ALTER TABLE `{tableName}` ADD COLUMN `{columnName}` {operation.OldSqlType} NULL");
                    }
                    break;
                }
                case MigrationOpType.AlterColumnType:
                {
                    var newType = operation.NewSqlType ?? "VARCHAR(255)";
                    var oldType = operation.OldSqlType ?? "VARCHAR(255)";
                    up.Add($"ALTER TABLE `{tableName}` MODIFY COLUMN `{columnName}` {newType}");
                    down.Add($"ALTER TABLE `{tableName}` MODIFY COLUMN `{columnName}` {oldType}");
                    break;
                }
                case MigrationOpType.AlterNullability:
                {
                    var type = operation.NewSqlType ?? "VARCHAR(255)";
                    var upAction = operation.NewNotNull == true ? "NOT NULL" : "NULL";
                    var downAction = operation.NewNotNull == true ? "NULL" : "NOT NULL";
                    up.Add($"ALTER TABLE `{tableName}` MODIFY COLUMN `{columnName}` {type} {upAction}");
                    down.Add($"ALTER TABLE `{tableName}` MODIFY COLUMN `{columnName}` {type} {downAction}");
                    break;
                }
            }
        }

        return (up, down, string.Empty);
    }

    private static string GetMySqlDefaultLiteral(string sqlType)
    {
        var upper = sqlType.Trim().ToUpperInvariant();
        if (upper is "BOOLEAN" or "BOOL" or "BIT")
        {
            return "0";
        }

        return DynamicEntitySchemaMigrationService.NormalizeSqlType(sqlType) switch
        {
            "INTEGER" or "BIGINT" or "NUMERIC" => "0",
            "TIMESTAMP" or "DATETIME" => "CURRENT_TIMESTAMP",
            _ => "''"
        };
    }

    private sealed class MySqlColumnRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int NotNullBool { get; set; }
        public int Pk { get; set; }
    }
}
