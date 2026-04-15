// AI 查询执行器默认实现
// 提供基本的 CRUD 查询功能

using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// 默认 AI 查询执行器实现
/// </summary>
public class DefaultAIQueryExecutor : IAIQueryExecutor
{
    private readonly IAIDbConnectionFactory _dbConnectionFactory;
    private readonly IAIProjectContext _projectContext;
    private readonly ILogger<DefaultAIQueryExecutor> _logger;

    public DefaultAIQueryExecutor(
        IAIDbConnectionFactory dbConnectionFactory,
        IAIProjectContext projectContext,
        ILogger<DefaultAIQueryExecutor> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _projectContext = projectContext;
        _logger = logger;
    }

    /// <summary>
    /// 执行查询
    /// </summary>
    public async Task<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(
        string entity,
        QueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection(_projectContext.ProjectName);
        
        var tableName = SanitizeIdentifier(entity);
        var sql = $"SELECT * FROM {tableName}";
        
        var whereClause = BuildWhereClause(parameters.Filters, out var dynamicParams);
        if (!string.IsNullOrEmpty(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }

        if (parameters.OrderBy.Count > 0)
        {
            var orderByClauses = parameters.OrderBy.Select(o =>
            {
                var parts = o.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var column = SanitizeIdentifier(parts[0]);
                var direction = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
                return $"{column} {direction}";
            });
            sql += $" ORDER BY {string.Join(", ", orderByClauses)}";
        }

        if (parameters.Limit.HasValue)
        {
            sql += $" LIMIT {parameters.Limit.Value}";
        }

        if (parameters.Offset.HasValue)
        {
            sql += $" OFFSET {parameters.Offset.Value}";
        }

        _logger.LogDebug("执行查询: {Sql}", sql);
        
        var results = await connection.QueryAsync(sql, dynamicParams);
        return results.Select(row =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in row.GetType().GetProperties())
            {
                dict[prop.Name] = prop.GetValue(row);
            }
            return dict;
        });
    }

    /// <summary>
    /// 计数查询
    /// </summary>
    public async Task<long> CountAsync(
        string entity,
        QueryParameters parameters,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection(_projectContext.ProjectName);
        
        var tableName = SanitizeIdentifier(entity);
        var sql = $"SELECT COUNT(*) FROM {tableName}";
        
        var whereClause = BuildWhereClause(parameters.Filters, out var dynamicParams);
        if (!string.IsNullOrEmpty(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }

        return await connection.ExecuteScalarAsync<long>(sql, dynamicParams);
    }

    /// <summary>
    /// 获取实体元数据
    /// </summary>
    public async Task<IEnumerable<AIEntityMetadata>> GetEntityMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection(_projectContext.ProjectName);
        
        var sql = @"
            SELECT name as TableName 
            FROM sqlite_master 
            WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__%'
            ORDER BY name";

        var tables = await connection.QueryAsync<string>(sql);
        
        var metadataList = new List<AIEntityMetadata>();
        foreach (var table in tables)
        {
            var metadata = await GetEntityMetadataAsync(table, cancellationToken);
            if (metadata != null)
            {
                metadataList.Add(metadata);
            }
        }

        return metadataList;
    }

    /// <summary>
    /// 获取单个实体元数据
    /// </summary>
    public async Task<AIEntityMetadata?> GetEntityMetadataAsync(
        string entityName,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection(_projectContext.ProjectName);
        
        var sql = "PRAGMA table_info(@tableName)";
        
        try
        {
            var columns = await connection.QueryAsync(sql, new { tableName = entityName });
            
            var columnList = new List<AIEntityColumn>();
            string? primaryKey = null;
            
            foreach (var col in columns)
            {
                var columnName = (string)col.name;
                var columnType = (string)col.type;
                var isPk = Convert.ToBoolean(col.pk);
                
                if (isPk)
                {
                    primaryKey = columnName;
                }
                
                columnList.Add(new AIEntityColumn
                {
                    Name = columnName,
                    DisplayName = columnName,
                    Type = columnType,
                    IsPrimaryKey = isPk
                });
            }

            return new AIEntityMetadata
            {
                Name = entityName,
                TableName = entityName,
                DisplayName = entityName,
                Columns = columnList
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取实体元数据失败: {Entity}", entityName);
            return null;
        }
    }

    /// <summary>
    /// 执行原生 SQL 查询
    /// </summary>
    public async Task<IEnumerable<Dictionary<string, object?>>> ExecuteSqlAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection(_projectContext.ProjectName);
        
        _logger.LogDebug("执行原生 SQL: {Sql}", sql);
        
        var results = await connection.QueryAsync(sql, parameters);
        return results.Select(row =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in row.GetType().GetProperties())
            {
                dict[prop.Name] = prop.GetValue(row);
            }
            return dict;
        });
    }

    /// <summary>
    /// 构建 WHERE 子句
    /// </summary>
    private string BuildWhereClause(Dictionary<string, object?> filters, out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();
        var clauses = new List<string>();

        foreach (var filter in filters)
        {
            if (filter.Value != null)
            {
                var paramName = filter.Key;
                parameters.Add(paramName, filter.Value);
                clauses.Add($"{SanitizeIdentifier(filter.Key)} = @{paramName}");
            }
        }

        return string.Join(" AND ", clauses);
    }

    /// <summary>
    /// 清理 SQL 标识符（防止注入）
    /// </summary>
    private string SanitizeIdentifier(string identifier)
    {
        // 只允许字母、数字和下划线
        var cleaned = new string(identifier.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return $"\"{cleaned}\"";
    }
}
