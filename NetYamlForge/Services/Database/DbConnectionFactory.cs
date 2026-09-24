using System.Data;
using NetYamlForge.Services;
using NetYamlForge.Services.Connection;

namespace NetYamlForge.Services.Database;

/// <summary>
/// 数据库连接门面实现（Thin Facade）
/// 彻底剥离自研双层连接池，直连底层 ADO.NET 原生驱动连接池（SqlClient, Npgsql, MySql, SQLite）
/// </summary>
public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly IConnectionManager _connectionManager;
    private readonly ProjectManager _projectManager;

    public DbConnectionFactory(
        IConnectionManager connectionManager,
        ProjectManager projectManager)
    {
        _connectionManager = connectionManager;
        _projectManager = projectManager;
    }

    public IDbConnection CreateConnection(string? projectName)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            throw new InvalidOperationException("Project name is required");
        }

        if (!_projectManager.TryGet(projectName, out var project) || project == null)
        {
            throw new InvalidOperationException($"Project not found: {projectName}");
        }

        return _connectionManager.GetConnection(projectName);
    }

    public async Task<IDbConnection> CreateConnectionAsync(string? projectName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            throw new InvalidOperationException("Project name is required");
        }

        if (!_projectManager.TryGet(projectName, out var project) || project == null)
        {
            throw new InvalidOperationException($"Project not found: {projectName}");
        }

        return await _connectionManager.GetConnectionAsync(projectName, cancellationToken);
    }
}
