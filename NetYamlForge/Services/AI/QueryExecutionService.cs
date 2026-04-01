using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Models.AI;
using System.Linq;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI 查询执行服务
/// 执行解析后的查询参数并返回结果
/// </summary>
public class QueryExecutionService
{
    private readonly IDynamicCrudRepository _repo;
    private readonly IEntityMetadataProvider _metadata;
    private readonly ILogger<QueryExecutionService> _logger;

    public QueryExecutionService(
        IDynamicCrudRepository repo,
        IEntityMetadataProvider metadata,
        ILogger<QueryExecutionService> logger)
    {
        _repo = repo;
        _metadata = metadata;
        _logger = logger;
    }

    /// <summary>
    /// 执行查询
    /// </summary>
    public async Task<(List<IDictionary<string, object?>> Data, int Total)> ExecuteAsync(
        ParsedQueryParams query,
        string project,
        CancellationToken ct = default)
    {
        _logger.LogInformation("执行查询：实体={Entity}, 动作={Action}", query.Entity, query.Action);

        // 根据动作类型执行不同的查询
        return query.Action.ToLower() switch
        {
            "count" => await ExecuteCountAsync(query, project, ct),
            "aggregate" => await ExecuteAggregateAsync(query, project, ct),
            _ => await ExecuteListAsync(query, project, ct)
        };
    }

    /// <summary>
    /// 执行列表查询
    /// </summary>
    private async Task<(List<IDictionary<string, object?>> Data, int Total)> ExecuteListAsync(
        ParsedQueryParams query,
        string project,
        CancellationToken ct = default)
    {
        var meta = _metadata.Get(query.Entity);

        // 构建过滤条件
        var filters = BuildFilters(query.Filters);

        // 执行查询
        var items = await _repo.GetAllAsync(
            entity: query.Entity,
            search: null,
            sort: query.OrderBy?.Field,
            dir: query.OrderBy?.Dir ?? "asc",
            filters: filters,
            page: 1,
            pageSize: query.Top ?? 100);  // 默认最大 100 条

        // 获取总数
        var total = await _repo.CountAsync(query.Entity, null, filters);

        // 将 dynamic 转换为 IDictionary<string, object?>
        var itemsList = items.Select(x => (IDictionary<string, object?>)x).ToList();

        // 选择指定字段
        if (query.Select.Count > 0)
        {
            itemsList = itemsList.Select(item =>
            {
                var filtered = item.Where(kv => query.Select.Contains(kv.Key, StringComparer.OrdinalIgnoreCase));
                return (IDictionary<string, object?>)filtered.ToDictionary(kv => kv.Key, kv => kv.Value);
            }).ToList();
        }

        return (itemsList, total);
    }

    /// <summary>
    /// 执行计数查询
    /// </summary>
    private async Task<(List<IDictionary<string, object?>> Data, int Total)> ExecuteCountAsync(
        ParsedQueryParams query,
        string project,
        CancellationToken ct = default)
    {
        var filters = BuildFilters(query.Filters);

        var count = await _repo.CountAsync(query.Entity, null, filters);

        var result = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["count"] = count
            }
        };

        return (result, count);
    }

    /// <summary>
    /// 执行聚合查询
    /// </summary>
    private async Task<(List<IDictionary<string, object?>> Data, int Total)> ExecuteAggregateAsync(
        ParsedQueryParams query,
        string project,
        CancellationToken ct = default)
    {
        // TODO: 实现聚合查询支持
        // 目前返回一个占位结果
        _logger.LogWarning("聚合查询尚未完全实现");

        var result = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["note"] = "聚合查询功能开发中"
            }
        };

        return (result, 1);
    }

    /// <summary>
    /// 构建过滤条件字典
    /// </summary>
    private Dictionary<string, string?> BuildFilters(List<FilterClause> filters)
    {
        var filterDict = new Dictionary<string, string?>();

        foreach (var filter in filters)
        {
            var key = filter.Op.ToLower() switch
            {
                "eq" => filter.Field,
                "ne" => $"{filter.Field}!=",
                "gt" => $"{filter.Field}>",
                "lt" => $"{filter.Field}<",
                "gte" => $"{filter.Field}>=",
                "lte" => $"{filter.Field}<=",
                "like" => $"{filter.Field}:",
                "in" => $"{filter.Field}[]",
                "between" => $"{filter.Field}[]",
                "is_null" => $"{filter.Field}__null",
                _ => filter.Field
            };

            var value = filter.Op.ToLower() switch
            {
                "between" => FormatBetweenValue(filter.Value, filter.Value2),
                "in" => FormatInValue(filter.Value),
                "is_null" => "true",
                "like" => $"%{filter.Value}%",
                "lt" or "lte" or "gt" or "gte" or "eq" or "ne" => FormatDateValue(filter.Value),
                _ => filter.Value?.ToString()
            };

            filterDict[key] = value;
        }

        return filterDict;
    }

    /// <summary>
    /// 格式化日期值（处理相对日期如 today、yesterday 等）
    /// </summary>
    private string? FormatDateValue(object? value)
    {
        if (value is string strValue)
        {
            // 检查是否是相对日期
            try
            {
                var (start, end) = RelativeDateRanges.GetDateRange(strValue);
                // 对于 lt/lte 操作，使用当天结束时间；对于 gt/gte 操作，使用当天开始时间
                return start.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                // 不是相对日期，按原样处理
            }
        }

        return value?.ToString();
    }

    /// <summary>
    /// 格式化 Between 值
    /// </summary>
    private string? FormatBetweenValue(object? value1, object? value2)
    {
        if (value1 is string strValue)
        {
            // 检查是否是相对日期
            try
            {
                var (start, end) = RelativeDateRanges.GetDateRange(strValue);
                return $"{start:yyyy-MM-dd HH:mm:ss},{end:yyyy-MM-dd HH:mm:ss}";
            }
            catch
            {
                // 不是相对日期，按原样处理
            }
        }

        return value1 != null ? $"{value1},{value2}" : null;
    }

    /// <summary>
    /// 格式化 In 值
    /// </summary>
    private string? FormatInValue(object? value)
    {
        return value switch
        {
            List<object> list => string.Join(",", list),
            JsonElement elem when elem.ValueKind == JsonValueKind.Array =>
                string.Join(",", elem.EnumerateArray().Select(e => e.ToString())),
            _ => value?.ToString()
        };
    }
}
