// ファイル概要: DBスキーマから entities.generated/*.yml を自動生成します。
// DCS003 抑制理由: CLIスキャフォールドツールはDI外で実行されるため、接続を直接生成します。
#pragma warning disable DCS003

using System.Data;
using System.Text;
using NetYamlForge.Models;
using NetYamlForge.Services.Cli;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NetYamlForge.Services;

public static class EntityYamlScaffolder
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private sealed record ColumnSchema(
        string Name,
        string DbType,
        bool IsNotNull,
        bool IsPrimaryKey,
        int PrimaryKeyOrder,
        bool IsIdentity,
        bool HasDefault);

    private sealed record ForeignKeySchema(string ColumnName, string RefTable, string RefColumn);

    private sealed record TableSchema(string Name, List<ColumnSchema> Columns, List<ForeignKeySchema> ForeignKeys);

    public static int Run(
        string currentDir,
        string? projectFilter = null,
        bool forceOverwrite = true,
        string outputDirName = "entities.generated",
        bool includeLabelKeys = false,
        CliScaffoldResult? result = null)
    {
        var contentRoot = ResolveContentRoot(currentDir);
        var projectsRoot = Path.Combine(contentRoot, "projects");
        if (!Directory.Exists(projectsRoot))
        {
            var msg = $"projects ディレクトリが見つかりません: {projectsRoot}";
            Console.Error.WriteLine(msg);
            if (result != null) { result.Success = false; result.ExitCode = 1; result.Errors.Add(msg); }
            return 1;
        }

        var projectDirs = Directory.GetDirectories(projectsRoot)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(projectFilter))
        {
            projectDirs = projectDirs
                .Where(d => string.Equals(Path.GetFileName(d), projectFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (projectDirs.Count == 0)
        {
            var msg = "対象プロジェクトが見つかりません。";
            Console.Error.WriteLine(msg);
            if (result != null) { result.Success = false; result.ExitCode = 1; result.Errors.Add(msg); }
            return 1;
        }

        if (result != null && !string.IsNullOrWhiteSpace(projectFilter))
        {
            result.Project = projectFilter;
        }

        var totalFiles = 0;
        foreach (var projectDir in projectDirs)
        {
            var yamlPath = Path.Combine(projectDir, "project.yaml");
            if (!File.Exists(yamlPath))
            {
                Console.WriteLine($"[skip] project.yaml not found: {projectDir}");
                continue;
            }

            var config = LoadProjectConfig(yamlPath);
            var dbType = (config.Database.Type ?? "sqlite").ToLowerInvariant();
            var connectionString = BuildConnectionString(config, projectDir, dbType);
            var tables = LoadTables(dbType, connectionString);
            if (tables.Count == 0)
            {
                Console.WriteLine($"[skip] no tables: {config.Name}");
                continue;
            }

            var outDir = Path.Combine(projectDir, outputDirName);
            Directory.CreateDirectory(outDir);
            var generated = 0;
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tableToEntity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entityName = ToEntityName(table.Name, usedNames);
                tableToEntity[NormalizeTableName(table.Name)] = entityName;
            }
            var tableLookup = tables.ToDictionary(t => NormalizeTableName(t.Name), t => t, StringComparer.OrdinalIgnoreCase);

            foreach (var table in tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entityName = tableToEntity[NormalizeTableName(table.Name)];
                var yaml = BuildEntityYaml(entityName, table, tableToEntity, tableLookup, includeLabelKeys);
                var filePath = Path.Combine(outDir, $"{entityName}.yml");

                if (!forceOverwrite && File.Exists(filePath))
                {
                    result?.SkippedFiles.Add(filePath);
                    continue;
                }

                File.WriteAllText(filePath, yaml, Utf8NoBom);
                result?.GeneratedFiles.Add(filePath);
                generated++;
            }

            totalFiles += generated;
            Console.WriteLine($"[ok] {config.Name}: generated {generated} files -> {outDir}");
        }

        Console.WriteLine($"done. generated files: {totalFiles}");
        result?.NextSteps.Add("entities.generated/ 内の YAMLを entities/ にコピー・調整してください");
        return 0;
    }

    private static string ResolveContentRoot(string currentDir)
    {
        if (Directory.Exists(Path.Combine(currentDir, "projects")))
        {
            return currentDir;
        }

        var sub = Path.Combine(currentDir, "NetYamlForge");
        if (Directory.Exists(Path.Combine(sub, "projects")))
        {
            return sub;
        }

        throw new DirectoryNotFoundException($"content root を解決できません: {currentDir}");
    }

    private static ProjectConfig LoadProjectConfig(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        YamlSchemaValidator.ValidateProjectYaml(yaml, yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<ProjectConfig>(yaml);
    }

    private static string BuildConnectionString(ProjectConfig config, string projectDir, string dbType)
    {
        if (dbType == "sqlserver"
            || dbType == "postgresql"
            || dbType == "postgres"
            || dbType == "mysql"
            || dbType == "mariadb")
        {
            return config.Database.ConnectionString
                ?? throw new InvalidOperationException(
                    $"プロジェクト '{config.Name}' の database.connectionString が未設定です。");
        }

        if (!string.IsNullOrWhiteSpace(config.Database.Path))
        {
            var dbPath = Path.IsPathRooted(config.Database.Path)
                ? config.Database.Path
                : Path.GetFullPath(Path.Combine(projectDir, config.Database.Path));
            return $"Data Source={dbPath}";
        }

        var defaultPath = Path.Combine(projectDir, "database", $"{config.Name}.db");
        return $"Data Source={defaultPath}";
    }

    private static List<TableSchema> LoadTables(string dbType, string connectionString)
    {
        return dbType switch
        {
            "sqlserver" => LoadSqlServerTables(connectionString),
            "postgresql" or "postgres" => LoadPostgreSqlTables(connectionString),
            "mysql" or "mariadb" => LoadMySqlTables(connectionString),
            _ => LoadSqliteTables(connectionString)
        };
    }

    private static List<TableSchema> LoadSqliteTables(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = """
SELECT name
FROM sqlite_master
WHERE type = 'table'
  AND name NOT LIKE 'sqlite_%'
  AND name NOT LIKE '__EFMigrationsHistory%'
ORDER BY name;
""";

        using var tableReader = tablesCmd.ExecuteReader();
        var tables = new List<TableSchema>();
        while (tableReader.Read())
        {
            var tableName = tableReader.GetString(0);
            var cols = new List<ColumnSchema>();
            var fks = new List<ForeignKeySchema>();

            using var pragmaCmd = conn.CreateCommand();
            pragmaCmd.CommandText = $"PRAGMA table_info([{tableName.Replace("]", "]]", StringComparison.Ordinal)}]);";
            using var colReader = pragmaCmd.ExecuteReader();
            while (colReader.Read())
            {
                var name = colReader["name"]?.ToString() ?? string.Empty;
                var dbType = colReader["type"]?.ToString() ?? "TEXT";
                var notNull = Convert.ToInt32(colReader["notnull"]) == 1;
                var pkOrder = Convert.ToInt32(colReader["pk"]);
                var dflt = colReader["dflt_value"]?.ToString();
                var hasDefault = !string.IsNullOrWhiteSpace(dflt);
                var isIdentity = pkOrder > 0 && dbType.Contains("INT", StringComparison.OrdinalIgnoreCase);
                cols.Add(new ColumnSchema(name, dbType, notNull, pkOrder > 0, pkOrder, isIdentity, hasDefault));
            }
            colReader.Close();

            using var fkCmd = conn.CreateCommand();
            fkCmd.CommandText = $"PRAGMA foreign_key_list([{tableName.Replace("]", "]]", StringComparison.Ordinal)}]);";
            using var fkReader = fkCmd.ExecuteReader();
            while (fkReader.Read())
            {
                var from = fkReader["from"]?.ToString();
                var refTable = fkReader["table"]?.ToString();
                var to = fkReader["to"]?.ToString();
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(refTable))
                {
                    continue;
                }
                fks.Add(new ForeignKeySchema(from, refTable, string.IsNullOrWhiteSpace(to) ? "Id" : to));
            }

            if (cols.Count > 0)
            {
                tables.Add(new TableSchema(tableName, cols, fks));
            }
        }

        return tables;
    }

    private static List<TableSchema> LoadSqlServerTables(string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        conn.Open();

        using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = """
SELECT s.name AS SchemaName, t.name AS TableName
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
ORDER BY s.name, t.name;
""";

        using var tableReader = tablesCmd.ExecuteReader();
        var tables = new List<TableSchema>();
        while (tableReader.Read())
        {
            var schema = tableReader["SchemaName"]?.ToString() ?? "dbo";
            var tableName = tableReader["TableName"]?.ToString() ?? string.Empty;
            var fullTable = $"{schema}.{tableName}";
            var cols = new List<ColumnSchema>();
            var fks = new List<ForeignKeySchema>();

            using var colCmd = conn.CreateCommand();
            colCmd.CommandText = """
SELECT c.name AS ColumnName,
       typ.name AS DbType,
       c.is_nullable AS IsNullable,
       c.is_identity AS IsIdentity,
       CASE WHEN dc.definition IS NULL THEN 0 ELSE 1 END AS HasDefault,
       ISNULL(ic.key_ordinal, 0) AS KeyOrdinal
FROM sys.columns c
JOIN sys.tables t ON c.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.types typ ON c.user_type_id = typ.user_type_id
LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
LEFT JOIN (
    SELECT ic.object_id, ic.column_id, ic.key_ordinal
    FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    WHERE i.is_primary_key = 1
) ic ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE s.name = @schema AND t.name = @table
ORDER BY c.column_id;
""";
            var pSchema = colCmd.CreateParameter();
            pSchema.ParameterName = "@schema";
            pSchema.Value = schema;
            colCmd.Parameters.Add(pSchema);
            var pTable = colCmd.CreateParameter();
            pTable.ParameterName = "@table";
            pTable.Value = tableName;
            colCmd.Parameters.Add(pTable);

            using var colReader = colCmd.ExecuteReader();
            while (colReader.Read())
            {
                var name = colReader["ColumnName"]?.ToString() ?? string.Empty;
                var dbType = colReader["DbType"]?.ToString() ?? "nvarchar";
                var isNullable = Convert.ToBoolean(colReader["IsNullable"]);
                var isIdentity = Convert.ToBoolean(colReader["IsIdentity"]);
                var hasDefault = Convert.ToInt32(colReader["HasDefault"]) == 1;
                var keyOrdinal = Convert.ToInt32(colReader["KeyOrdinal"]);
                cols.Add(new ColumnSchema(name, dbType, !isNullable, keyOrdinal > 0, keyOrdinal, isIdentity, hasDefault));
            }
            colReader.Close();

            using var fkCmd = conn.CreateCommand();
            fkCmd.CommandText = """
SELECT pc.name AS ParentColumn,
       rt.name AS RefTable,
       rc.name AS RefColumn
FROM sys.foreign_key_columns fkc
JOIN sys.tables pt ON fkc.parent_object_id = pt.object_id
JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id
JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
WHERE ps.name = @schema AND pt.name = @table
ORDER BY fkc.constraint_column_id;
""";
            var pfkSchema = fkCmd.CreateParameter();
            pfkSchema.ParameterName = "@schema";
            pfkSchema.Value = schema;
            fkCmd.Parameters.Add(pfkSchema);
            var pfkTable = fkCmd.CreateParameter();
            pfkTable.ParameterName = "@table";
            pfkTable.Value = tableName;
            fkCmd.Parameters.Add(pfkTable);
            using var fkReader = fkCmd.ExecuteReader();
            while (fkReader.Read())
            {
                var from = fkReader["ParentColumn"]?.ToString();
                var refTable = fkReader["RefTable"]?.ToString();
                var to = fkReader["RefColumn"]?.ToString();
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(refTable) || string.IsNullOrWhiteSpace(to))
                {
                    continue;
                }
                fks.Add(new ForeignKeySchema(from, refTable, to));
            }

            if (cols.Count > 0)
            {
                tables.Add(new TableSchema(fullTable, cols, fks));
            }
        }

        return tables;
    }

    private static List<TableSchema> LoadPostgreSqlTables(string connectionString)
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();

        using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = """
SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_type = 'BASE TABLE'
  AND table_schema NOT IN ('pg_catalog', 'information_schema')
ORDER BY table_schema, table_name;
""";

        using var tableReader = tablesCmd.ExecuteReader();
        var tables = new List<TableSchema>();
        while (tableReader.Read())
        {
            var schema = tableReader["table_schema"]?.ToString() ?? "public";
            var tableName = tableReader["table_name"]?.ToString() ?? string.Empty;
            var fullTable = $"{schema}.{tableName}";
            var cols = new List<ColumnSchema>();
            var fks = new List<ForeignKeySchema>();

            using var colCmd = conn.CreateCommand();
            colCmd.CommandText = """
SELECT c.column_name AS ColumnName,
       c.data_type AS DbType,
       c.is_nullable AS IsNullable,
       c.column_default AS ColumnDefault,
       (pg_get_serial_sequence(quote_ident(c.table_schema) || '.' || quote_ident(c.table_name), c.column_name) IS NOT NULL) AS IsIdentity,
       COALESCE(pk.key_ordinal, 0) AS KeyOrdinal
FROM information_schema.columns c
LEFT JOIN (
  SELECT ku.table_schema, ku.table_name, ku.column_name, ku.ordinal_position AS key_ordinal
  FROM information_schema.table_constraints tc
  JOIN information_schema.key_column_usage ku
    ON tc.constraint_name = ku.constraint_name
   AND tc.table_schema = ku.table_schema
   AND tc.table_name = ku.table_name
  WHERE tc.constraint_type = 'PRIMARY KEY'
) pk
  ON pk.table_schema = c.table_schema
 AND pk.table_name = c.table_name
 AND pk.column_name = c.column_name
WHERE c.table_schema = @schema AND c.table_name = @table
ORDER BY c.ordinal_position;
""";
            colCmd.Parameters.AddWithValue("schema", schema);
            colCmd.Parameters.AddWithValue("table", tableName);
            using var colReader = colCmd.ExecuteReader();
            while (colReader.Read())
            {
                var name = colReader["ColumnName"]?.ToString() ?? string.Empty;
                var dbType = colReader["DbType"]?.ToString() ?? "text";
                var isNullable = string.Equals(colReader["IsNullable"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase);
                var isIdentity = Convert.ToBoolean(colReader["IsIdentity"]);
                var hasDefault = colReader["ColumnDefault"] is not DBNull && !string.IsNullOrWhiteSpace(colReader["ColumnDefault"]?.ToString());
                var keyOrdinal = Convert.ToInt32(colReader["KeyOrdinal"]);
                cols.Add(new ColumnSchema(name, dbType, !isNullable, keyOrdinal > 0, keyOrdinal, isIdentity, hasDefault));
            }
            colReader.Close();

            using var fkCmd = conn.CreateCommand();
            fkCmd.CommandText = """
SELECT kcu.column_name AS ParentColumn,
       ccu.table_name AS RefTable,
       ccu.column_name AS RefColumn
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu
  ON tc.constraint_name = kcu.constraint_name
 AND tc.table_schema = kcu.table_schema
JOIN information_schema.constraint_column_usage ccu
  ON ccu.constraint_name = tc.constraint_name
 AND ccu.table_schema = tc.table_schema
WHERE tc.constraint_type = 'FOREIGN KEY'
  AND tc.table_schema = @schema
  AND tc.table_name = @table
ORDER BY kcu.ordinal_position;
""";
            fkCmd.Parameters.AddWithValue("schema", schema);
            fkCmd.Parameters.AddWithValue("table", tableName);
            using var fkReader = fkCmd.ExecuteReader();
            while (fkReader.Read())
            {
                var from = fkReader["ParentColumn"]?.ToString();
                var refTable = fkReader["RefTable"]?.ToString();
                var to = fkReader["RefColumn"]?.ToString();
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(refTable) || string.IsNullOrWhiteSpace(to))
                {
                    continue;
                }
                fks.Add(new ForeignKeySchema(from, refTable, to));
            }

            if (cols.Count > 0)
            {
                tables.Add(new TableSchema(fullTable, cols, fks));
            }
        }

        return tables;
    }

    private static List<TableSchema> LoadMySqlTables(string connectionString)
    {
        using var conn = new MySqlConnection(connectionString);
        conn.Open();

        using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = """
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
  AND TABLE_SCHEMA = DATABASE()
ORDER BY TABLE_NAME;
""";

        using var tableReader = tablesCmd.ExecuteReader();
        var tables = new List<TableSchema>();
        while (tableReader.Read())
        {
            var schema = tableReader["TABLE_SCHEMA"]?.ToString() ?? string.Empty;
            var tableName = tableReader["TABLE_NAME"]?.ToString() ?? string.Empty;
            var fullTable = string.IsNullOrWhiteSpace(schema) ? tableName : $"{schema}.{tableName}";
            var cols = new List<ColumnSchema>();
            var fks = new List<ForeignKeySchema>();

            using var colCmd = conn.CreateCommand();
            colCmd.CommandText = """
SELECT c.COLUMN_NAME AS ColumnName,
       c.DATA_TYPE AS DbType,
       c.IS_NULLABLE AS IsNullable,
       c.COLUMN_DEFAULT AS ColumnDefault,
       (c.EXTRA LIKE '%auto_increment%') AS IsIdentity,
       COALESCE(tc.ORDINAL_POSITION, 0) AS KeyOrdinal
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE tc
  ON tc.TABLE_SCHEMA = c.TABLE_SCHEMA
 AND tc.TABLE_NAME = c.TABLE_NAME
 AND tc.COLUMN_NAME = c.COLUMN_NAME
 AND tc.CONSTRAINT_NAME = 'PRIMARY'
WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
ORDER BY c.ORDINAL_POSITION;
""";
            colCmd.Parameters.AddWithValue("schema", schema);
            colCmd.Parameters.AddWithValue("table", tableName);
            using var colReader = colCmd.ExecuteReader();
            while (colReader.Read())
            {
                var name = colReader["ColumnName"]?.ToString() ?? string.Empty;
                var dbType = colReader["DbType"]?.ToString() ?? "varchar";
                var isNullable = string.Equals(colReader["IsNullable"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase);
                var hasDefault = colReader["ColumnDefault"] is not DBNull && !string.IsNullOrWhiteSpace(colReader["ColumnDefault"]?.ToString());
                var isIdentity = Convert.ToBoolean(colReader["IsIdentity"]);
                var keyOrdinal = Convert.ToInt32(colReader["KeyOrdinal"]);
                cols.Add(new ColumnSchema(name, dbType, !isNullable, keyOrdinal > 0, keyOrdinal, isIdentity, hasDefault));
            }
            colReader.Close();

            using var fkCmd = conn.CreateCommand();
            fkCmd.CommandText = """
SELECT k.COLUMN_NAME AS ParentColumn,
       k.REFERENCED_TABLE_NAME AS RefTable,
       k.REFERENCED_COLUMN_NAME AS RefColumn
FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
WHERE k.TABLE_SCHEMA = @schema
  AND k.TABLE_NAME = @table
  AND k.REFERENCED_TABLE_NAME IS NOT NULL
ORDER BY k.ORDINAL_POSITION;
""";
            fkCmd.Parameters.AddWithValue("schema", schema);
            fkCmd.Parameters.AddWithValue("table", tableName);
            using var fkReader = fkCmd.ExecuteReader();
            while (fkReader.Read())
            {
                var from = fkReader["ParentColumn"]?.ToString();
                var refTable = fkReader["RefTable"]?.ToString();
                var to = fkReader["RefColumn"]?.ToString();
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(refTable) || string.IsNullOrWhiteSpace(to))
                {
                    continue;
                }
                fks.Add(new ForeignKeySchema(from, refTable, to));
            }

            if (cols.Count > 0)
            {
                tables.Add(new TableSchema(fullTable, cols, fks));
            }
        }

        return tables;
    }

    private static string BuildEntityYaml(
        string entityName,
        TableSchema table,
        IReadOnlyDictionary<string, string> tableToEntity,
        IReadOnlyDictionary<string, TableSchema> tableLookup,
        bool includeLabelKeys)
    {
        var keyColumns = table.Columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => c.PrimaryKeyOrder)
            .ToList();
        var primaryKey = keyColumns.FirstOrDefault()?.Name
            ?? table.Columns.First().Name;
        var formOrder = table.Columns.Where(c => !c.IsIdentity).Select(c => c.Name).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("entities:");
        sb.AppendLine($"  {entityName}:");
        sb.AppendLine($"    table: {Quote(table.Name)}");
        sb.AppendLine($"    key: {Quote(primaryKey)}");
        if (keyColumns.Count > 1)
        {
            sb.AppendLine("    keys:");
            foreach (var key in keyColumns)
            {
                sb.AppendLine($"      - {Quote(key.Name)}");
            }
        }
        sb.AppendLine($"    displayName: {Quote(ToDisplayName(entityName))}");
        if (includeLabelKeys)
        {
            sb.AppendLine($"    displayNameKey: {Quote($"entities.{entityName}.displayName")}");
        }
        sb.AppendLine("    softDelete: false");
        sb.AppendLine("    paging:");
        sb.AppendLine("      pageSize: 20");
        sb.AppendLine("      mode: numbered");
        sb.AppendLine("    layout:");
        sb.AppendLine("      forms:");
        sb.AppendLine("        columns: 2");
        sb.AppendLine($"        order: [{string.Join(", ", formOrder.Select(Quote))}]");
        sb.AppendLine("      filters:");
        sb.AppendLine("        columns: 4");
        sb.AppendLine("        order: []");
        sb.AppendLine("    columns:");
        foreach (var col in table.Columns)
        {
            var yamlType = MapDbTypeToYamlType(col.DbType);
            var required = col.IsNotNull && !col.IsIdentity && !col.HasDefault;
            var label = ToDisplayName(col.Name);
            var searchable = yamlType == "string";
            sb.Append($"      {col.Name}: {{ type: {yamlType}");
            if (col.IsIdentity)
            {
                sb.Append(", identity: true");
            }
            if (required)
            {
                sb.Append(", required: true");
            }
            sb.Append($", label: {Quote(label)}");
            if (includeLabelKeys)
            {
                sb.Append($", labelKey: {Quote($"entities.{entityName}.columns.{col.Name}.label")}");
            }
            if (searchable)
            {
                sb.Append(", searchable: true");
            }
            sb.Append(", sortable: true }");
            sb.AppendLine();
        }

        sb.AppendLine("    forms:");
        foreach (var col in table.Columns.Where(c => !c.IsIdentity))
        {
            var yamlType = MapDbTypeToYamlType(col.DbType);
            var required = col.IsNotNull && !col.HasDefault;
            var label = ToDisplayName(col.Name);
            var fk = table.ForeignKeys.FirstOrDefault(f => f.ColumnName.Equals(col.Name, StringComparison.OrdinalIgnoreCase));
            if (fk == null)
            {
                sb.Append($"      {col.Name}: {{ type: {yamlType}");
                if (required)
                {
                    sb.Append(", required: true");
                }
                sb.Append($", label: {Quote(label)}");
                if (includeLabelKeys)
                {
                    sb.Append($", labelKey: {Quote($"entities.{entityName}.forms.{col.Name}.label")}");
                }
                sb.Append(", editable: true }");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"      {col.Name}:");
            sb.AppendLine($"        type: {yamlType}");
            if (required)
            {
                sb.AppendLine("        required: true");
            }
            sb.AppendLine($"        label: {Quote(label)}");
            if (includeLabelKeys)
            {
                sb.AppendLine($"        labelKey: {Quote($"entities.{entityName}.forms.{col.Name}.label")}");
            }
            sb.AppendLine("        editable: true");

            var normalizedRefTable = NormalizeTableName(fk.RefTable);
            if (!tableToEntity.TryGetValue(normalizedRefTable, out var refEntity))
            {
                refEntity = normalizedRefTable.ToLowerInvariant();
            }
            var displayColumn = ResolveForeignKeyDisplayColumn(normalizedRefTable, fk.RefColumn, tableLookup);
            sb.AppendLine("        foreignKey:");
            sb.AppendLine($"          entity: {refEntity}");
            sb.AppendLine($"          displayColumn: {Quote(displayColumn)}");
        }

        sb.AppendLine("    filters: {}");
        sb.AppendLine("    links: {}");
        return sb.ToString();
    }

    private static string NormalizeTableName(string tableName)
    {
        return tableName
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? tableName;
    }

    private static string ResolveForeignKeyDisplayColumn(
        string normalizedRefTable,
        string refColumn,
        IReadOnlyDictionary<string, TableSchema> tableLookup)
    {
        if (!tableLookup.TryGetValue(normalizedRefTable, out var refTable))
        {
            return refColumn;
        }

        var keySet = refTable.Columns
            .Where(c => c.IsPrimaryKey)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preferred = refTable.Columns
            .Where(c => !keySet.Contains(c.Name) && MapDbTypeToYamlType(c.DbType) == "string")
            .ToList();
        var nameLike = preferred.FirstOrDefault(c =>
            c.Name.Contains("name", StringComparison.OrdinalIgnoreCase)
            || c.Name.Contains("title", StringComparison.OrdinalIgnoreCase));
        if (nameLike != null)
        {
            return nameLike.Name;
        }

        var firstText = preferred.FirstOrDefault();
        if (firstText != null)
        {
            return firstText.Name;
        }

        var fallback = refTable.Columns.FirstOrDefault(c => !keySet.Contains(c.Name));
        if (fallback != null)
        {
            return fallback.Name;
        }

        return refColumn;
    }

    private static string ToEntityName(string tableName, ISet<string> used)
    {
        var raw = NormalizeTableName(tableName);
        var lower = raw.ToLowerInvariant();
        var baseName = lower switch
        {
            _ when lower.EndsWith("ies", StringComparison.Ordinal) && lower.Length > 3 => lower[..^3] + "y",
            _ when lower.EndsWith("s", StringComparison.Ordinal) && lower.Length > 1 => lower[..^1],
            _ => lower
        };

        var candidate = baseName;
        var idx = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{baseName}{idx}";
            idx++;
        }

        return candidate;
    }

    private static string ToDisplayName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var normalized = raw.Replace("_", " ", StringComparison.Ordinal);
        var chars = normalized.ToCharArray();
        var result = new StringBuilder(chars.Length + 8);
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(chars[i - 1]))
            {
                result.Append(' ');
            }
            result.Append(c);
        }

        var words = result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(" ", words);
    }

    private static string MapDbTypeToYamlType(string dbType)
    {
        var t = dbType.ToLowerInvariant();
        if (t.Contains("bigint", StringComparison.Ordinal)) return "long";
        if (t.Contains("int", StringComparison.Ordinal)) return "int";
        if (t.Contains("decimal", StringComparison.Ordinal)
            || t.Contains("numeric", StringComparison.Ordinal)
            || t.Contains("money", StringComparison.Ordinal)
            || t.Contains("real", StringComparison.Ordinal)
            || t.Contains("float", StringComparison.Ordinal)
            || t.Contains("double", StringComparison.Ordinal))
        {
            return "decimal";
        }
        if (t.Contains("date", StringComparison.Ordinal) || t.Contains("time", StringComparison.Ordinal))
        {
            return "datetime";
        }
        if (t.Contains("bool", StringComparison.Ordinal) || t == "bit")
        {
            return "bool";
        }
        return "string";
    }

    private static string Quote(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }
}
