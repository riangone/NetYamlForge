using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
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

    public DynamicEntitySchemaMigrationService(ILogger<DynamicEntitySchemaMigrationService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<ColumnSchemaInfo>> GetPhysicalColumnsAsync(IDbConnection conn, string tableName, string dbType = "sqlite")
    {
        EnsureOpen(conn);
        if (IsPostgres(dbType))
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
                    NormalizeSqlType(r.Type),
                    r.NotNullBool,
                    r.Pk != 0))
                .ToList();
        }

        if (IsMySql(dbType))
        {
            var mysqlRows = await conn.QueryAsync<PostgresColumnRow>(
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
                    NormalizeSqlType(r.Type),
                    r.NotNullBool,
                    r.Pk != 0))
                .ToList();
        }

        if (IsSqlServer(dbType))
        {
            var mssqlRows = await conn.QueryAsync<PostgresColumnRow>(
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
                    NormalizeSqlType(r.Type),
                    r.NotNullBool,
                    r.Pk != 0))
                .ToList();
        }

        if (!string.Equals(dbType, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Schema migration column inspection supports SQLite, PostgreSQL, MySQL and SQL Server only.");
        }

#pragma warning disable DCS001
        var rows = await conn.QueryAsync<PragmaColumnRow>(
            $"SELECT cid AS \"Cid\", name AS \"Name\", type AS \"Type\", \"notnull\" AS \"NotNull\", pk AS \"Pk\" FROM pragma_table_info(\"{EscapeIdentifier(tableName)}\")");
#pragma warning restore DCS001
        return rows
            .OrderBy(r => r.Cid)
            .Select(r => new ColumnSchemaInfo(
                r.Name,
                NormalizeSqlType(r.Type),
                r.NotNull != 0,
                r.Pk != 0))
            .ToList();
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

        if (IsPostgres(dbType))
        {
            return GeneratePostgresSql(plan, entity);
        }

        if (IsMySql(dbType))
        {
            return GenerateMySqlSql(plan, entity);
        }

        if (IsSqlServer(dbType))
        {
            return GenerateSqlServerSql(plan, entity);
        }

        if (!string.Equals(dbType, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Schema migration supports SQLite, PostgreSQL, MySQL and SQL Server only.");
        }

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

        var createSql = TableDdlBuilder.BuildCreateTableSql(plan.TableName, entity, dbType);
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
            createSql,
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

    private static (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName) GeneratePostgresSql(
        MigrationPlan plan,
        EntityDefinition entity)
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

    private static string EscapeIdentifier(string name) => name.Replace("\"", "\"\"");

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

    private static bool IsPostgres(string dbType) =>
        string.Equals(dbType, "postgresql", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dbType, "postgres", StringComparison.OrdinalIgnoreCase);

    private static bool IsMySql(string dbType) =>
        string.Equals(dbType, "mysql", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dbType, "mariadb", StringComparison.OrdinalIgnoreCase);

    private static bool IsSqlServer(string dbType) =>
        string.Equals(dbType, "sqlserver", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dbType, "mssql", StringComparison.OrdinalIgnoreCase);

    private static string GetPostgresDefaultLiteral(string sqlType)
    {
        var upper = sqlType.Trim().ToUpperInvariant();
        if (upper is "BOOLEAN" or "BOOL")
        {
            return "false";
        }

        return NormalizeSqlType(sqlType) switch
        {
            "INTEGER" or "BIGINT" or "NUMERIC" => "0",
            "TIMESTAMP" => "now()",
            _ => "''"
        };
    }

    private static string GetMySqlDefaultLiteral(string sqlType)
    {
        var upper = sqlType.Trim().ToUpperInvariant();
        if (upper is "BOOLEAN" or "BOOL" or "BIT")
        {
            return "0";
        }

        return NormalizeSqlType(sqlType) switch
        {
            "INTEGER" or "BIGINT" or "NUMERIC" => "0",
            "TIMESTAMP" or "DATETIME" => "CURRENT_TIMESTAMP",
            _ => "''"
        };
    }

    private static string GetSqlServerDefaultLiteral(string sqlType)
    {
        var upper = sqlType.Trim().ToUpperInvariant();
        if (upper is "BOOLEAN" or "BOOL" or "BIT")
        {
            return "0";
        }

        return NormalizeSqlType(sqlType) switch
        {
            "INTEGER" or "BIGINT" or "NUMERIC" => "0",
            "TIMESTAMP" or "DATETIME" => "GETDATE()",
            _ => "''"
        };
    }

    private static (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName) GenerateMySqlSql(
        MigrationPlan plan,
        EntityDefinition entity)
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

    private static (IReadOnlyList<string> UpSql, IReadOnlyList<string> DownSql, string BackupTableName) GenerateSqlServerSql(
        MigrationPlan plan,
        EntityDefinition entity)
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

    private sealed class PragmaColumnRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int NotNull { get; set; }
        public int Pk { get; set; }
    }

    private sealed class PostgresColumnRow
    {
        public int Cid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool NotNullBool { get; set; }
        public int Pk { get; set; }
    }

    private sealed class MigrationRow
    {
        public string DownSql { get; set; } = string.Empty;
        public string? RolledBackAt { get; set; }
    }
}
