using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;

namespace NetYamlForge.Services.DynamicEntity;

public sealed class DynamicEntitySchemaMigrationService
{
    private const string MigrationTableSql = """
CREATE TABLE IF NOT EXISTS _nyf_migrations (
    id TEXT PRIMARY KEY,
    project_name TEXT NOT NULL,
    entity_name TEXT NOT NULL,
    table_name TEXT NOT NULL,
    description TEXT NOT NULL,
    up_sql TEXT NOT NULL,
    down_sql TEXT NOT NULL,
    applied_at TEXT NOT NULL,
    rolled_back_at TEXT
)
""";

    private readonly ILogger<DynamicEntitySchemaMigrationService> _logger;
    private readonly IEnumerable<ISchemaDdlBuilder> _ddlBuilders;

    public DynamicEntitySchemaMigrationService(
        ILogger<DynamicEntitySchemaMigrationService> logger,
        IEnumerable<ISchemaDdlBuilder>? ddlBuilders = null)
    {
        _logger = logger;
        _ddlBuilders = ddlBuilders ?? new ISchemaDdlBuilder[]
        {
            new SqliteSchemaDdlBuilder(),
            new PostgresSchemaDdlBuilder(),
            new MySqlSchemaDdlBuilder(),
            new SqlServerSchemaDdlBuilder()
        };
    }

    private ISchemaDdlBuilder GetBuilder(string dbType)
    {
        var builder = _ddlBuilders.FirstOrDefault(b => b.Supports(dbType));
        if (builder == null)
        {
            throw new NotSupportedException($"Schema migration column inspection / generation is not supported for dbType '{dbType}'.");
        }
        return builder;
    }

    public async Task<IReadOnlyList<ColumnSchemaInfo>> GetPhysicalColumnsAsync(IDbConnection conn, string tableName, string dbType = "sqlite")
    {
        EnsureOpen(conn);
        var builder = GetBuilder(dbType);
        return await builder.GetPhysicalColumnsAsync(conn, tableName);
    }

    public MigrationPlan BuildPlan(
        string entityName,
        EntityDefinition entity,
        IReadOnlyList<ColumnSchemaInfo> physicalColumns,
        string dbType)
    {
        var operations = new List<MigrationOperation>();
        var pkColumns = new HashSet<string>(entity.GetPrimaryKeyColumns(), StringComparer.OrdinalIgnoreCase);
        var yamlColumns = entity.Columns
            .Where(c => string.IsNullOrWhiteSpace(c.Value.Expression))
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        var physicalByName = physicalColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var yamlColumn in yamlColumns)
        {
            if (pkColumns.Contains(yamlColumn.Key))
            {
                continue;
            }

            var newSqlType = SqlTypeMapper.MapYamlTypeToSqlType(yamlColumn.Value.Type, dbType);
            var normalizedNewSqlType = NormalizeSqlType(newSqlType);
            var newNotNull = yamlColumn.Value.Required;
            if (!physicalByName.TryGetValue(yamlColumn.Key, out var physical))
            {
                operations.Add(new MigrationOperation(MigrationOpType.AddColumn, yamlColumn.Key, null, newSqlType, newNotNull));
                continue;
            }

            if (physical.IsPrimaryKey)
            {
                _logger.LogWarning("Schema migration ignores primary key column '{Column}' on table '{Table}'.", physical.Name, entity.Table);
                continue;
            }

            var oldSqlType = NormalizeSqlType(physical.SqlType);
            if (!SqlTypesEqual(oldSqlType, normalizedNewSqlType))
            {
                operations.Add(new MigrationOperation(MigrationOpType.AlterColumnType, yamlColumn.Key, oldSqlType, newSqlType, null));
            }

            if (physical.NotNull != newNotNull)
            {
                operations.Add(new MigrationOperation(MigrationOpType.AlterNullability, yamlColumn.Key, oldSqlType, newSqlType, newNotNull));
            }
        }

        foreach (var physical in physicalColumns)
        {
            if (physical.IsPrimaryKey || pkColumns.Contains(physical.Name))
            {
                continue;
            }

            if (!yamlColumns.ContainsKey(physical.Name))
            {
                operations.Add(new MigrationOperation(
                    MigrationOpType.DropColumn,
                    physical.Name,
                    NormalizeSqlType(physical.SqlType),
                    null,
                    null));
            }
        }

        if (entity.SoftDelete && !physicalByName.ContainsKey("IsDeleted"))
            operations.Add(new MigrationOperation(MigrationOpType.AddColumn, "IsDeleted", null, "INTEGER", false));

        return new MigrationPlan(entityName, entity.Table, operations);
    }

    public (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName) GenerateSql(
        MigrationPlan plan,
        EntityDefinition entity,
        string dbType)
    {
        if (plan.Operations.Count == 0)
        {
            return (Array.Empty<string>(), Array.Empty<string>(), string.Empty);
        }

        var builder = GetBuilder(dbType);
        return builder.GenerateSql(plan, entity);
    }

    public async Task<MigrationApplyResult> ApplyAsync(
        IDbConnection conn,
        string projectName,
        MigrationPlan plan,
        EntityDefinition entity,
        string dbType,
        bool dryRun)
    {
        var (upSql, downSql, _) = GenerateSql(plan, entity, dbType);
        var migrationId = $"nyf_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        if (dryRun || upSql.Count == 0)
        {
            return new MigrationApplyResult(false, migrationId, upSql);
        }

        EnsureOpen(conn);
        using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(MigrationTableSql, transaction: tx);

            foreach (var sql in upSql)
            {
#pragma warning disable DCS001
                await conn.ExecuteAsync(sql, transaction: tx);
#pragma warning restore DCS001
            }

            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            await conn.ExecuteAsync(
                """
INSERT INTO _nyf_migrations (id, project_name, entity_name, table_name, description, up_sql, down_sql, applied_at, rolled_back_at)
VALUES (@Id, @ProjectName, @EntityName, @TableName, @Description, @UpSql, @DownSql, @AppliedAt, NULL)
""",
                new
                {
                    Id = migrationId,
                    ProjectName = projectName,
                    plan.EntityName,
                    plan.TableName,
                    Description = BuildDescription(plan),
                    UpSql = JsonSerializer.Serialize(upSql),
                    DownSql = JsonSerializer.Serialize(downSql),
                    AppliedAt = now
                },
                tx);

            tx.Commit();
            return new MigrationApplyResult(true, migrationId, upSql);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task RollbackAsync(IDbConnection conn, string migrationId)
    {
        EnsureOpen(conn);
        using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(MigrationTableSql, transaction: tx);
            var row = await conn.QuerySingleOrDefaultAsync<MigrationRow>(
                "SELECT id, down_sql AS DownSql, rolled_back_at AS RolledBackAt FROM _nyf_migrations WHERE id = @Id",
                new { Id = migrationId },
                tx);
            if (row == null)
            {
                throw new InvalidOperationException($"Migration '{migrationId}' was not found.");
            }

            if (!string.IsNullOrWhiteSpace(row.RolledBackAt))
            {
                throw new InvalidOperationException($"Migration '{migrationId}' was already rolled back.");
            }

            var downSql = JsonSerializer.Deserialize<List<string>>(row.DownSql) ?? [];
            foreach (var sql in downSql)
            {
#pragma warning disable DCS001
                await conn.ExecuteAsync(sql, transaction: tx);
#pragma warning restore DCS001
            }

            await conn.ExecuteAsync(
                "UPDATE _nyf_migrations SET rolled_back_at = @RolledBackAt WHERE id = @Id",
                new
                {
                    Id = migrationId,
                    RolledBackAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                },
                tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<MigrationRecord>> GetHistoryAsync(IDbConnection conn, string? projectName = null)
    {
        EnsureOpen(conn);
        await conn.ExecuteAsync(MigrationTableSql);
        var sql = string.IsNullOrWhiteSpace(projectName)
            ? """
SELECT id AS Id, project_name AS ProjectName, entity_name AS EntityName, table_name AS TableName, description AS Description,
       up_sql AS UpSql, down_sql AS DownSql, applied_at AS AppliedAt, rolled_back_at AS RolledBackAt
FROM _nyf_migrations
ORDER BY applied_at DESC
"""
            : """
SELECT id AS Id, project_name AS ProjectName, entity_name AS EntityName, table_name AS TableName, description AS Description,
       up_sql AS UpSql, down_sql AS DownSql, applied_at AS AppliedAt, rolled_back_at AS RolledBackAt
FROM _nyf_migrations
WHERE project_name = @ProjectName
ORDER BY applied_at DESC
""";

        var rows = await conn.QueryAsync<MigrationRecord>(sql, new { ProjectName = projectName });
        return rows.ToList();
    }

    private static string BuildDescription(MigrationPlan plan) =>
        string.Join(", ", plan.Operations.Select(o => $"{o.OpType}:{o.ColumnName}"));

    private static void EnsureOpen(IDbConnection conn)
    {
        if (conn.State != ConnectionState.Open)
        {
            conn.Open();
        }
    }

    public static string NormalizeSqlType(string? sqlType)
    {
        var text = (sqlType ?? string.Empty).Trim();
        var paren = text.IndexOf('(');
        if (paren >= 0)
        {
            text = text[..paren];
        }

        return text.ToUpperInvariant() switch
        {
            "INT" => "INTEGER",
            "BIGINT" => "BIGINT",
            "SMALLINT" => "INTEGER",
            "TINYINT" => "INTEGER",
            "BIT" => "INTEGER",
            "BOOLEAN" => "INTEGER",
            "BOOL" => "INTEGER",
            "REAL" => "NUMERIC",
            "DOUBLE" => "NUMERIC",
            "DOUBLE PRECISION" => "NUMERIC",
            "FLOAT" => "NUMERIC",
            "NUMERIC" => "NUMERIC",
            "DECIMAL" => "NUMERIC",
            "VARCHAR" => "TEXT",
            "CHARACTER VARYING" => "TEXT",
            "NVARCHAR" => "TEXT",
            "CHAR" => "TEXT",
            "NCHAR" => "TEXT",
            "TEXT" => "TEXT",
            "LONGTEXT" => "TEXT",
            "MEDIUMTEXT" => "TEXT",
            "DATETIME" => "TIMESTAMP",
            "TIMESTAMP WITHOUT TIME ZONE" => "TIMESTAMP",
            "TIMESTAMP WITH TIME ZONE" => "TIMESTAMP",
            "" => "TEXT",
            var normalized => normalized
        };
    }

    private static bool SqlTypesEqual(string oldSqlType, string newSqlType) =>
        string.Equals(NormalizeSqlType(oldSqlType), NormalizeSqlType(newSqlType), StringComparison.OrdinalIgnoreCase);

    private sealed class MigrationRow
    {
        public string DownSql { get; set; } = string.Empty;
        public string? RolledBackAt { get; set; }
    }
}
