// ファイル概要: Entity YAML 定義と実DBスキーマの整合性（NOT NULL 等）を検証します。
// DCS003 抑制理由: スキーマ検証ツールはDI外で実行されるため、接続を直接生成します。
#pragma warning disable DCS003

using System.Data;
using NetYamlForge.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

namespace NetYamlForge.Services;

public static class EntityDbSchemaConsistencyValidator
{
    private sealed record DbColumnInfo(string Name, bool IsNotNull, bool IsIdentity, bool HasDefault);

    public static void ValidateOrThrow(
        string projectName,
        string databaseType,
        string connectionString,
        IEntityMetadataProvider metadataProvider)
    {
        try
        {
            using var conn = CreateConnection(databaseType, connectionString);
            conn.Open();
            ValidateSchema(projectName, databaseType, conn, metadataProvider);
        }
        catch (System.Data.Common.DbException dbEx)
        {
            // DB ファイルが存在しない場合や、DB が初期化されていない場合はスキップ
            // （DbInitializer が起動時に初期化するため）
            if (dbEx.Message.Contains("unable to open database file") ||
                dbEx.Message.Contains("no such table") ||
                dbEx.Message.Contains("not a database"))
            {
                return;
            }
            throw;
        }
    }

    private static void ValidateSchema(
        string projectName,
        string databaseType,
        IDbConnection conn,
        IEntityMetadataProvider metadataProvider)
    {
        var errors = new List<string>();
        foreach (var entry in metadataProvider.GetAll())
        {
            var entityName = entry.Key;
            var entity = entry.Value;
            var dbColumns = LoadRequiredInputColumns(conn, databaseType, entity.Table);
            if (dbColumns.Count == 0)
            {
                continue;
            }

            var yamlColumns = new Dictionary<string, ColumnDefinition>(
                entity.Columns ?? new Dictionary<string, ColumnDefinition>(),
                StringComparer.OrdinalIgnoreCase);
            var yamlForms = new Dictionary<string, FormDefinition>(
                entity.Forms ?? new Dictionary<string, FormDefinition>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var dbCol in dbColumns)
            {
                if (!yamlColumns.TryGetValue(dbCol.Name, out var colDef))
                {
                    errors.Add(
                        $"entities.{entityName}.columns.{dbCol.Name} が未定義です。DB列 {entity.Table}.{dbCol.Name} は NOT NULL かつ既定値なしのため、定義が必要です。");
                    continue;
                }

                if (!colDef.Required)
                {
                    errors.Add(
                        $"entities.{entityName}.columns.{dbCol.Name}.required を true にしてください。DB列 {entity.Table}.{dbCol.Name} は NOT NULL かつ既定値なしです。");
                }

                if (yamlForms.TryGetValue(dbCol.Name, out var formDef) && formDef.Editable && !formDef.Required)
                {
                    errors.Add(
                        $"entities.{entityName}.forms.{dbCol.Name}.required を true にしてください。編集可能で DB列 {entity.Table}.{dbCol.Name} は NOT NULL です。");
                }
            }
        }

        if (errors.Count > 0)
        {
            var details = string.Join(Environment.NewLine, errors.Select(e => "- " + e));
            throw new InvalidOperationException(
                $"プロジェクト '{projectName}' の Entity YAML とDBスキーマが不整合です。{Environment.NewLine}{details}");
        }
    }

    private static IDbConnection CreateConnection(string databaseType, string connectionString)
    {
        var dbType = databaseType.ToLowerInvariant();
        return dbType switch
        {
            "sqlserver" => new SqlConnection(connectionString),
            "postgresql" or "postgres" => new NpgsqlConnection(connectionString),
            "mysql" or "mariadb" => new MySqlConnection(connectionString),
            _ => new SqliteConnection(connectionString)
        };
    }

    private static List<DbColumnInfo> LoadRequiredInputColumns(IDbConnection conn, string databaseType, string table)
    {
        var dbType = databaseType.ToLowerInvariant();
        return dbType switch
        {
            "sqlserver" => LoadRequiredInputColumnsSqlServer(conn, table),
            "postgresql" or "postgres" => LoadRequiredInputColumnsPostgreSql(conn, table),
            "mysql" or "mariadb" => LoadRequiredInputColumnsMySql(conn, table),
            _ => LoadRequiredInputColumnsSqlite(conn, table)
        };
    }

    private static List<DbColumnInfo> LoadRequiredInputColumnsSqlite(IDbConnection conn, string table)
    {
        var safeTable = table.Replace("]", "]]", StringComparison.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{safeTable}]);";
        using var reader = cmd.ExecuteReader();
        var columns = new List<DbColumnInfo>();
        while (reader.Read())
        {
            var name = reader["name"]?.ToString() ?? string.Empty;
            var isNotNull = Convert.ToInt32(reader["notnull"]) == 1;
            // SQLite の PRAGMA table_info.pk は複合主鍵時に 1,2,... を返す
            // そのため「主鍵かどうか」は > 0 で判定する
            var pk = Convert.ToInt32(reader["pk"]) > 0;
            var dflt = reader["dflt_value"]?.ToString();
            var hasDefault = !string.IsNullOrWhiteSpace(dflt);
            columns.Add(new DbColumnInfo(name, isNotNull, pk, hasDefault));
        }

        return columns
            .Where(c => c.IsNotNull && !c.IsIdentity && !c.HasDefault)
            .ToList();
    }

    private static List<DbColumnInfo> LoadRequiredInputColumnsSqlServer(IDbConnection conn, string table)
    {
        var (schema, tableName) = ParseSchemaTable(table, "dbo");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT c.name AS ColumnName,
       c.is_nullable AS IsNullable,
       c.is_identity AS IsIdentity,
       CASE WHEN dc.definition IS NULL THEN 0 ELSE 1 END AS HasDefault
FROM sys.columns c
JOIN sys.tables t ON c.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
WHERE s.name = @schema AND t.name = @table;
""";
        var pSchema = cmd.CreateParameter();
        pSchema.ParameterName = "@schema";
        pSchema.Value = schema;
        cmd.Parameters.Add(pSchema);
        var pTable = cmd.CreateParameter();
        pTable.ParameterName = "@table";
        pTable.Value = tableName;
        cmd.Parameters.Add(pTable);

        using var reader = cmd.ExecuteReader();
        var columns = new List<DbColumnInfo>();
        while (reader.Read())
        {
            var name = reader["ColumnName"]?.ToString() ?? string.Empty;
            var isNullable = Convert.ToBoolean(reader["IsNullable"]);
            var isIdentity = Convert.ToBoolean(reader["IsIdentity"]);
            var hasDefault = Convert.ToInt32(reader["HasDefault"]) == 1;
            columns.Add(new DbColumnInfo(name, !isNullable, isIdentity, hasDefault));
        }

        return columns
            .Where(c => c.IsNotNull && !c.IsIdentity && !c.HasDefault)
            .ToList();
    }

    private static List<DbColumnInfo> LoadRequiredInputColumnsPostgreSql(IDbConnection conn, string table)
    {
        var (schema, tableName) = ParseSchemaTable(table, "public");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT c.column_name AS ColumnName,
       (c.is_nullable = 'NO') AS IsNotNull,
       (pg_get_serial_sequence(quote_ident(c.table_schema) || '.' || quote_ident(c.table_name), c.column_name) IS NOT NULL) AS IsIdentity,
       (c.column_default IS NOT NULL) AS HasDefault
FROM information_schema.columns c
WHERE c.table_schema = @schema AND c.table_name = @table;
""";
        var pSchema = cmd.CreateParameter();
        pSchema.ParameterName = "@schema";
        pSchema.Value = schema;
        cmd.Parameters.Add(pSchema);
        var pTable = cmd.CreateParameter();
        pTable.ParameterName = "@table";
        pTable.Value = tableName;
        cmd.Parameters.Add(pTable);

        using var reader = cmd.ExecuteReader();
        var columns = new List<DbColumnInfo>();
        while (reader.Read())
        {
            var name = reader["ColumnName"]?.ToString() ?? string.Empty;
            var isNotNull = Convert.ToBoolean(reader["IsNotNull"]);
            var isIdentity = Convert.ToBoolean(reader["IsIdentity"]);
            var hasDefault = Convert.ToBoolean(reader["HasDefault"]);
            columns.Add(new DbColumnInfo(name, isNotNull, isIdentity, hasDefault));
        }

        return columns
            .Where(c => c.IsNotNull && !c.IsIdentity && !c.HasDefault)
            .ToList();
    }

    private static List<DbColumnInfo> LoadRequiredInputColumnsMySql(IDbConnection conn, string table)
    {
        var normalized = table.Replace("`", string.Empty, StringComparison.Ordinal);
        var parts = normalized.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var tableName = parts[^1];
        var schema = parts.Length >= 2 ? parts[^2] : null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT c.COLUMN_NAME AS ColumnName,
       (c.IS_NULLABLE = 'NO') AS IsNotNull,
       (c.EXTRA LIKE '%auto_increment%') AS IsIdentity,
       (c.COLUMN_DEFAULT IS NOT NULL) AS HasDefault
FROM INFORMATION_SCHEMA.COLUMNS c
WHERE c.TABLE_NAME = @table
  AND (@schema IS NULL OR c.TABLE_SCHEMA = @schema);
""";
        var pTable = cmd.CreateParameter();
        pTable.ParameterName = "@table";
        pTable.Value = tableName;
        cmd.Parameters.Add(pTable);
        var pSchema = cmd.CreateParameter();
        pSchema.ParameterName = "@schema";
        pSchema.Value = schema == null ? DBNull.Value : schema;
        cmd.Parameters.Add(pSchema);

        using var reader = cmd.ExecuteReader();
        var columns = new List<DbColumnInfo>();
        while (reader.Read())
        {
            var name = reader["ColumnName"]?.ToString() ?? string.Empty;
            var isNotNull = Convert.ToBoolean(reader["IsNotNull"]);
            var isIdentity = Convert.ToBoolean(reader["IsIdentity"]);
            var hasDefault = Convert.ToBoolean(reader["HasDefault"]);
            columns.Add(new DbColumnInfo(name, isNotNull, isIdentity, hasDefault));
        }

        return columns
            .Where(c => c.IsNotNull && !c.IsIdentity && !c.HasDefault)
            .ToList();
    }

    private static (string Schema, string Table) ParseSchemaTable(string table, string defaultSchema)
    {
        var normalized = table.Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);
        var parts = normalized.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (parts[^2], parts[^1]);
        }

        return (defaultSchema, normalized);
    }
}
