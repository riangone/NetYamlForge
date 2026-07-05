using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services.DynamicEntity;

public class SqliteSchemaDdlBuilder : ISchemaDdlBuilder
{
    public bool Supports(string dbType) =>
        string.Equals(dbType, "sqlite", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ColumnSchemaInfo>> GetPhysicalColumnsAsync(IDbConnection conn, string tableName)
    {
#pragma warning disable DCS001
        var rows = await conn.QueryAsync<PragmaColumnRow>(
            $"SELECT cid AS \"Cid\", name AS \"Name\", type AS \"Type\", \"notnull\" AS \"NotNull\", pk AS \"Pk\" FROM pragma_table_info(\"{EscapeIdentifier(tableName)}\")");
#pragma warning restore DCS001
        return rows
            .OrderBy(r => r.Cid)
            .Select(r => new ColumnSchemaInfo(
                r.Name,
                DynamicEntitySchemaMigrationService.NormalizeSqlType(r.Type),
                r.NotNull != 0,
                r.Pk != 0))
            .ToList();
    }

    public (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName) GenerateSql(MigrationPlan plan, EntityDefinition entity)
    {
        var backupTableName = $"{plan.TableName}__bak_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        if (!plan.RequiresTableRebuild)
        {
            var up = plan.Operations
                .Where(o => o.OpType == MigrationOpType.AddColumn)
                .Select(o =>
                {
                    var colDef = entity.Columns[o.ColumnName];
                    var nullable = colDef.Required ? "NOT NULL" : "NULL";
                    return $"ALTER TABLE \"{EscapeIdentifier(plan.TableName)}\" ADD COLUMN \"{EscapeIdentifier(o.ColumnName)}\" {o.NewSqlType} {nullable}";
                })
                .ToList();

            var down = plan.Operations
                .Where(o => o.OpType == MigrationOpType.AddColumn)
                .Select(o => $"ALTER TABLE \"{EscapeIdentifier(plan.TableName)}\" DROP COLUMN \"{EscapeIdentifier(o.ColumnName)}\"")
                .ToList();

            return (up, down, string.Empty);
        }

        var createSql = TableDdlBuilder.BuildCreateTableSql(plan.TableName, entity, "sqlite");
        var yamlPhysicalColumns = entity.Columns
            .Where(c => string.IsNullOrWhiteSpace(c.Value.Expression))
            .Select(c => c.Key)
            .ToList();
        var droppedColumns = plan.Operations
            .Where(o => o.OpType == MigrationOpType.DropColumn)
            .Select(o => o.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedColumns = plan.Operations
            .Where(o => o.OpType == MigrationOpType.AddColumn)
            .Select(o => o.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copyColumns = yamlPhysicalColumns
            .Where(c => !droppedColumns.Contains(c) && !addedColumns.Contains(c))
            .ToList();
        var typeChangedColumns = plan.Operations
            .Where(o => o.OpType == MigrationOpType.AlterColumnType)
            .ToDictionary(o => o.ColumnName, o => o.NewSqlType ?? "TEXT", StringComparer.OrdinalIgnoreCase);
        var targetColumns = string.Join(", ", copyColumns.Select(c => $"\"{EscapeIdentifier(c)}\""));
        var sourceColumns = string.Join(", ", copyColumns.Select(c =>
            typeChangedColumns.TryGetValue(c, out var newType)
                ? $"CAST(\"{EscapeIdentifier(c)}\" AS {newType})"
                : $"\"{EscapeIdentifier(c)}\""));

        var upSql = new List<string>
        {
            $"ALTER TABLE \"{EscapeIdentifier(plan.TableName)}\" RENAME TO \"{EscapeIdentifier(backupTableName)}\"",
            createSql
        };

        if (copyColumns.Count > 0)
        {
            upSql.Add($"INSERT INTO \"{EscapeIdentifier(plan.TableName)}\" ({targetColumns}) SELECT {sourceColumns} FROM \"{EscapeIdentifier(backupTableName)}\"");
        }

        var downSql = new List<string>
        {
            $"DROP TABLE \"{EscapeIdentifier(plan.TableName)}\"",
            $"ALTER TABLE \"{EscapeIdentifier(backupTableName)}\" RENAME TO \"{EscapeIdentifier(plan.TableName)}\"",
        };

        return (upSql, downSql, backupTableName);
    }

    private static string EscapeIdentifier(string name) => name.Replace("\"", "\"\"");

    private sealed class PragmaColumnRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int NotNull { get; set; }
        public int Pk { get; set; }
    }
}
