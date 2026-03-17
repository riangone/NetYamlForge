// ファイル概要: 監査ログをDBへ記録するサービス実装です。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。
//
// DCS003 抑制理由: 監査ログは独立した接続で書き込む必要があるため、OpenConnection() で
// 直接接続を生成します（メイントランザクションとは別のスコープで動作するため）。
#pragma warning disable DCS003

using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

namespace NetYamlForge.Services.Auth;

public class AuditLogService : IAuditLogService
{
    private readonly string _connectionString;
    private readonly string _dbType;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ProjectScope scope, ILogger<AuditLogService> logger)
    {
        _connectionString = scope.Current.ConnectionString;
        _dbType = scope.Current.DatabaseType;
        _logger = logger;
    }

    public async Task WriteAsync(
        string action,
        string? entity = null,
        string? detail = null,
        string? userName = null,
        IDbConnection? connection = null,
        IDbTransaction? transaction = null)
    {
        const string sql = @"
INSERT INTO AuditLog (UserName, Action, Entity, Detail, CreatedAt)
VALUES (@UserName, @Action, @Entity, @Detail, @CreatedAt)";

        var param = new
        {
            UserName = userName,
            Action = action,
            Entity = entity,
            Detail = detail,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };

        if (connection != null)
        {
            await connection.ExecuteAsync(sql, param, transaction);
            _logger.LogInformation("AUDIT action={Action} entity={Entity} user={UserName} detail={Detail}", action, entity, userName, detail);
            return;
        }

        // 外部接続がない場合は新規接続を生成します
        await using var conn = OpenConnection();
        await conn.ExecuteAsync(sql, param);
        _logger.LogInformation("AUDIT action={Action} entity={Entity} user={UserName} detail={Detail}", action, entity, userName, detail);
    }

    private DbConnection OpenConnection()
    {
        // DbConnection は IAsyncDisposable を実装するため await using が使えます。
        var dbType = _dbType.ToLowerInvariant();
        if (dbType == "sqlserver")
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
        else if (dbType == "postgresql" || dbType == "postgres")
        {
            var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
        else if (dbType == "mysql" || dbType == "mariadb")
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
        else
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
