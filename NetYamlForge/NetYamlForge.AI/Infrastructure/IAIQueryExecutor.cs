namespace NetYamlForge.AI.Infrastructure;

/// <summary>
/// AI 查询执行接口（适配层）
/// 用于解耦 AI 服务对主框架 IDynamicCrudRepository 和 IEntityMetadataProvider 的依赖
/// </summary>
public interface IAIQueryExecutor
{
    /// <summary>
    /// 执行查询
    /// </summary>
    Task<IEnumerable<Dictionary<string, object?>>> ExecuteQueryAsync(string entity, QueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计数查询
    /// </summary>
    Task<long> CountAsync(string entity, QueryParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取实体元数据
    /// </summary>
    Task<IEnumerable<AIEntityMetadata>> GetEntityMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取单个实体元数据
    /// </summary>
    Task<AIEntityMetadata?> GetEntityMetadataAsync(string entityName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行原生 SQL 查询
    /// </summary>
    Task<IEnumerable<Dictionary<string, object?>>> ExecuteSqlAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// 查询参数
/// </summary>
public class QueryParameters
{
    public Dictionary<string, object?> Filters { get; set; } = new();
    public List<string> OrderBy { get; set; } = new();
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public List<string> Columns { get; set; } = new();
}

/// <summary>
/// AI 实体元数据（简化版）
/// </summary>
public class AIEntityMetadata
{
    public string Name { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public List<AIEntityColumn> Columns { get; set; } = new();
}

/// <summary>
/// AI 实体列元数据
/// </summary>
public class AIEntityColumn
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool IsPrimaryKey { get; set; }
}
