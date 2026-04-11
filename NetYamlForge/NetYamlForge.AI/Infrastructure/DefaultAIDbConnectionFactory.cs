// AI 模块内部适配器 - 默认实现（独立进程模式）

using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// 默认数据库连接工厂实现（独立 AI 进程使用）
/// </summary>
public class DefaultAIDbConnectionFactory : IAIDbConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly string? _connectionString;

    public DefaultAIDbConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? _configuration["Database:ConnectionString"];
    }

    public IDbConnection CreateConnection(string? projectName = null)
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            throw new InvalidOperationException(
                "Database connection string is not configured. Please set 'ConnectionStrings:DefaultConnection' or 'Database:ConnectionString' in configuration.");
        }

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
