// ファイル概要: 監査ログをDBへ記録するサービス実装です。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。
//
// Phase 2 改进：使用 IConnectionManager 替代直接创建连接，实现连接复用
#pragma warning disable DCS003

using System.Data;
using System.Data.Common;
using Dapper;
using NetYamlForge.Services.Connection;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;
using Npgsql;

namespace NetYamlForge.Services.Auth;

public class AuditLogService : IAuditLogService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ProjectScope _scope;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IConnectionManager connectionManager,
        ProjectScope scope,
        ILogger<AuditLogService> logger)
    {
        _connectionManager = connectionManager;
        _scope = scope;
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

        // Phase 2: 使用连接管理器获取连接（复用连接池）
        var newConn = await _connectionManager.GetConnectionAsync(_scope.Current.Name);
        try
        {
            await newConn.ExecuteAsync(sql, param);
            _logger.LogInformation("AUDIT action={Action} entity={Entity} user={UserName} detail={Detail}", action, entity, userName, detail);
        }
        finally
        {
            // 释放连接回池
            _connectionManager.ReleaseConnection(newConn);
        }
    }
}
