using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Models.AI;
using System.Linq;
using System.Data;

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
    private readonly QueryTemplateService? _templateService;
    private readonly IDbConnection? _dbConnection;

    public QueryExecutionService(
        IDynamicCrudRepository repo,
        IEntityMetadataProvider metadata,
        ILogger<QueryExecutionService> logger,
        QueryTemplateService? templateService = null,
        IDbConnection? dbConnection = null)
    {
        _repo = repo;
        _metadata = metadata;
        _logger = logger;
        _templateService = templateService;
        _dbConnection = dbConnection;
    }

    /// <summary>
    /// 执行查询
    /// </summary>
    public async Task<(List<IDictionary<string, object?>> Data, int Total)> ExecuteAsync(
        ParsedQueryParams query,
        string project,
        CancellationToken ct = default)
    {
        _logger.LogInformation("执行查询：实体={Entity}, 动作={Action}, 模式={Mode}",
            query.Entity, query.Action, query.Mode);

        // 根据查询模式执行不同的查询
        return query.Mode.ToLower() switch
        {
            "raw_sql" => await ExecuteRawSqlAsync(query, project, ct),
            "template" => await ExecuteTemplateAsync(query, project, ct),
            _ => await ExecuteStructuredAsync(query, project, ct)
        };
    }

    /// <summary>
    /// 执行结构化查询（默认模式）
    /// </summary>
    private async Task<(List<IDictionary<string, object?>> Data, int Total)> ExecuteStructuredAsync(
        ParsedQueryParams query,
        string project,
        CancellationToken ct = default)
    {
        // 根据动作类型执行不同的查询
        return query.Action.ToLower() switch
        {
            "count" => await ExecuteCountAsync(query, project, ct),
            "aggregate" => await ExecuteAggregateAsync(query, project, ct),
            _ => await ExecuteListAsync(query, project, ct)
        };
    }

    /// <summary>
    /// 执行模板查询
    /// </summary>
    private async Task<(List<IDictionary<string, object?>> Data, int Total)> ExecuteTemplateAsync(
        ParsedQueryParams query,
        string project,
        CancellationToken ct = default)
    {
        if (_templateService == null)
        {
            throw new InvalidOperationException("查询模板服务未初始化");
        }

        if (string.IsNullOrEmpty(query.TemplateName))
        {
            throw new ArgumentException("模板查询需要指定 templateName");
        }

        _logger.LogInformation("执行模板查询：模板={Template}, 参数={Params}",
            query.TemplateName, query.TemplateParams != null ? string.Join(",", query.TemplateParams.Keys) : "无");

        // 合并模板和参数
        var mergedQuery = _templateService.MergeTemplateWithParams(query.TemplateName, query.TemplateParams);
        
        // 执行合并后的查询
        return await ExecuteStructuredAsync(mergedQuery, project, ct);
    }

    /// <summary>
    /// 执行原始 SQL 查询（需要特殊权限）
    /// </summary>
    private async Task<(List<IDictionary<string, object?>> Data, int Total)> ExecuteRawSqlAsync(
        ParsedQueryParams query,
        string project,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(query.RawSql))
        {
            throw new ArgumentException("原始 SQL 查询需要指定 raw_sql");
        }

        _logger.LogWarning("执行原始 SQL 查询：SQL={Sql}", query.RawSql);

        // TODO: 在这里实现 SQL 安全验证
        // 1. 验证 SQL 不包含危险操作（DROP, DELETE, UPDATE, INSERT 等）
        // 2. 验证只能访问允许的表
        // 3. 使用 SqlSafetyGuard 进行验证

        if (_dbConnection == null)
        {
            throw new InvalidOperationException("数据库连接未初始化");
        }

        // 执行 SQL 查询
        var data = new List<IDictionary<string, object?>>();
        using (var cmd = _dbConnection.CreateCommand())
        {
            cmd.CommandText = query.RawSql;
            cmd.CommandTimeout = 30;

            // 添加参数
            if (query.SqlParams != null)
            {
                foreach (var param in query.SqlParams)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = param.Key;
                    p.Value = param.Value ?? DBNull.Value;
                    cmd.Parameters.Add(p);
                }
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i);
                }
                data.Add(row);
            }
        }

        return (data, data.Count);
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
        _logger.LogInformation("执行聚合查询：实体={Entity}, 分组={GroupBy}, 聚合={Aggregations}",
            query.Entity, string.Join(",", query.GroupBy), string.Join(",", query.Aggregations.Select(a => $"{a.Function}({a.Field})")));

        // 构建过滤条件
        var filters = BuildFilters(query.Filters);

        // 获取基础数据
        var items = await _repo.GetAllAsync(
            entity: query.Entity,
            search: null,
            sort: null,
            dir: "asc",
            filters: filters,
            page: 1,
            pageSize: 10000);  // 聚合查询需要更多数据

        var itemsList = items.Select(x => (IDictionary<string, object?>)x).ToList();

        // 执行内存聚合（适用于中小数据集）
        var aggregatedData = PerformInMemoryAggregation(itemsList, query.GroupBy, query.Aggregations);

        return (aggregatedData, aggregatedData.Count);
    }

    /// <summary>
    /// 在内存中执行聚合
    /// </summary>
    private List<IDictionary<string, object?>> PerformInMemoryAggregation(
        List<IDictionary<string, object?>> data,
        List<string> groupByFields,
        List<AggregationClause> aggregations)
    {
        // 如果没有分组字段，返回单行聚合结果
        if (groupByFields.Count == 0)
        {
            var result = new Dictionary<string, object?>();
            foreach (var agg in aggregations)
            {
                var value = CalculateAggregate(data, agg.Function, agg.Field);
                result[agg.Alias ?? $"{agg.Function}_{agg.Field}"] = value;
            }
            return new List<IDictionary<string, object?>> { result };
        }

        // 按指定字段分组
        var grouped = data.GroupBy(item =>
        {
            var key = new StringBuilder();
            foreach (var field in groupByFields)
            {
                key.Append(item.TryGetValue(field, out var v) ? v?.ToString() : "null");
                key.Append("|");
            }
            return key.ToString();
        });

        var results = new List<IDictionary<string, object?>>();
        foreach (var group in grouped)
        {
            var row = new Dictionary<string, object?>();
            var groupItems = group.ToList();

            // 添加分组字段值
            foreach (var field in groupByFields)
            {
                if (groupItems.Count > 0 && groupItems[0].TryGetValue(field, out var value))
                {
                    row[field] = value;
                }
            }

            // 添加聚合值
            foreach (var agg in aggregations)
            {
                var value = CalculateAggregate(groupItems, agg.Function, agg.Field);
                row[agg.Alias ?? $"{agg.Function}_{agg.Field}"] = value;
            }

            results.Add(row);
        }

        return results;
    }

    /// <summary>
    /// 计算单个聚合值
    /// </summary>
    private object? CalculateAggregate(List<IDictionary<string, object?>> data, string function, string field)
    {
        var values = data
            .Where(item => item.TryGetValue(field, out var v) && v != null && v != DBNull.Value)
            .Select(item =>
            {
                item.TryGetValue(field, out var v);
                return v;
            })
            .ToList();

        return function.ToLower() switch
        {
            "count" => values.Count,
            "sum" => values.Sum(v => ConvertToDecimal(v)),
            "avg" => values.Count > 0 ? values.Average(v => ConvertToDecimal(v)) : 0,
            "min" => values.Min(v => ConvertToDecimal(v)),
            "max" => values.Max(v => ConvertToDecimal(v)),
            "distinct_count" => values.Select(v => v?.ToString()).Distinct().Count(),
            _ => values.Count
        };
    }

    /// <summary>
    /// 转换为 decimal 用于数值聚合
    /// </summary>
    private static decimal ConvertToDecimal(object? value)
    {
        if (value == null || value == DBNull.Value) return 0;
        if (value is decimal d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is double db) return (decimal)db;
        if (value is float f) return (decimal)f;
        if (decimal.TryParse(value?.ToString(), out var result)) return result;
        return 0;
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
