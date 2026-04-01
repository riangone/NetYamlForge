using System.Text.Json.Serialization;

namespace NetYamlForge.Models.AI;

/// <summary>
/// 自然语言查询请求
/// </summary>
public class NaturalLanguageQueryRequest
{
    /// <summary>
    /// 用户输入的自然语言
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 目标项目名
    /// </summary>
    public string? Project { get; set; }

    /// <summary>
    /// 指定的实体（可选，AI 可自动推断）
    /// </summary>
    public string? Entity { get; set; }
}

/// <summary>
/// 自然语言查询响应
/// </summary>
public class NaturalLanguageQueryResponse
{
    /// <summary>
    /// 查询结果数据
    /// </summary>
    public List<IDictionary<string, object?>> Data { get; set; } = new();

    /// <summary>
    /// Markdown 格式的展示文本
    /// </summary>
    public string Markdown { get; set; } = string.Empty;

    /// <summary>
    /// 解析后的查询参数
    /// </summary>
    public ParsedQueryParams? ParsedQuery { get; set; }

    /// <summary>
    /// 总数
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// AI 使用的模型
    /// </summary>
    public string? AiModel { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }
}

/// <summary>
/// 解析后的查询参数
/// </summary>
public class ParsedQueryParams
{
    /// <summary>
    /// 实体名称
    /// </summary>
    [JsonPropertyName("entity")]
    public string Entity { get; set; } = string.Empty;

    /// <summary>
    /// 操作类型 (list, count, aggregate)
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "list";

    /// <summary>
    /// 查询模式 (structured, raw_sql, template)
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "structured";

    /// <summary>
    /// 模板名称（当 mode=template 时使用）
    /// </summary>
    [JsonPropertyName("template")]
    public string? TemplateName { get; set; }

    /// <summary>
    /// 模板参数（当 mode=template 时使用）
    /// </summary>
    [JsonPropertyName("templateParams")]
    public Dictionary<string, object?>? TemplateParams { get; set; }

    /// <summary>
    /// 原始 SQL（当 mode=raw_sql 时使用，需要特殊权限）
    /// </summary>
    [JsonPropertyName("raw_sql")]
    public string? RawSql { get; set; }

    /// <summary>
    /// SQL 参数（当 mode=raw_sql 时使用）
    /// </summary>
    [JsonPropertyName("sql_params")]
    public Dictionary<string, object?>? SqlParams { get; set; }

    /// <summary>
    /// 过滤条件
    /// </summary>
    [JsonPropertyName("filters")]
    public List<FilterClause> Filters { get; set; } = new();

    /// <summary>
    /// 排序条件
    /// </summary>
    [JsonPropertyName("orderBy")]
    public OrderClause? OrderBy { get; set; }

    /// <summary>
    /// 限制返回数量
    /// </summary>
    [JsonPropertyName("top")]
    public int? Top { get; set; }

    /// <summary>
    /// 选择的字段
    /// </summary>
    [JsonPropertyName("select")]
    public List<string> Select { get; set; } = new();

    /// <summary>
    /// 分组字段
    /// </summary>
    [JsonPropertyName("groupBy")]
    public List<string> GroupBy { get; set; } = new();

    /// <summary>
    /// 聚合函数
    /// </summary>
    [JsonPropertyName("aggregations")]
    public List<AggregationClause> Aggregations { get; set; } = new();
}

/// <summary>
/// 过滤条件子句
/// </summary>
public class FilterClause
{
    /// <summary>
    /// 字段名
    /// </summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// 操作符 (eq, ne, gt, lt, gte, lte, like, in, between, is_null)
    /// </summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = "eq";

    /// <summary>
    /// 值
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// 值 2（用于 between）
    /// </summary>
    [JsonPropertyName("value2")]
    public object? Value2 { get; set; }

    /// <summary>
    /// 逻辑运算符 (AND, OR)
    /// </summary>
    [JsonPropertyName("logic")]
    public string Logic { get; set; } = "AND";
}

/// <summary>
/// 排序子句
/// </summary>
public class OrderClause
{
    /// <summary>
    /// 字段名
    /// </summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// 方向 (asc, desc)
    /// </summary>
    [JsonPropertyName("dir")]
    public string Dir { get; set; } = "asc";
}

/// <summary>
/// 聚合子句
/// </summary>
public class AggregationClause
{
    /// <summary>
    /// 聚合函数 (sum, avg, count, min, max)
    /// </summary>
    [JsonPropertyName("function")]
    public string Function { get; set; } = "count";

    /// <summary>
    /// 字段名
    /// </summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// 别名
    /// </summary>
    [JsonPropertyName("alias")]
    public string? Alias { get; set; }
}

/// <summary>
/// 相对日期范围
/// </summary>
public static class RelativeDateRanges
{
    /// <summary>
    /// 获取相对日期范围
    /// </summary>
    public static (DateTime Start, DateTime End) GetDateRange(string range)
    {
        var now = DateTime.Now;
        return range.ToLower() switch
        {
            "today" => (DateTime.Today, DateTime.Today.AddDays(1).AddTicks(-1)),
            "yesterday" => (DateTime.Today.AddDays(-1), DateTime.Today.AddTicks(-1)),
            "this_week" => (GetWeekStart(now), GetWeekEnd(now)),
            "last_week" => (GetWeekStart(now.AddDays(-7)), GetWeekEnd(now.AddDays(-7))),
            "this_month" => (new DateTime(now.Year, now.Month, 1), now.EndOfMonth()),
            "last_month" => (new DateTime(now.Year, now.Month, 1).AddMonths(-1).StartOfMonth(), 
                            new DateTime(now.Year, now.Month, 1).AddTicks(-1)),
            "this_quarter" => (GetQuarterStart(now), GetQuarterEnd(now)),
            "last_quarter" => (GetQuarterStart(now.AddMonths(-3)), GetQuarterEnd(now.AddMonths(-3))),
            "this_year" => (new DateTime(now.Year, 1, 1), new DateTime(now.Year, 12, 31).AddTicks(-1)),
            "last_year" => (new DateTime(now.Year - 1, 1, 1), new DateTime(now.Year, 1, 1).AddTicks(-1)),
            _ => throw new ArgumentException($"未知的日期范围：{range}")
        };
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    private static DateTime GetWeekEnd(DateTime date)
    {
        return GetWeekStart(date).AddDays(7).AddTicks(-1);
    }

    private static DateTime GetQuarterStart(DateTime date)
    {
        int quarter = (date.Month - 1) / 3 + 1;
        return new DateTime(date.Year, (quarter - 1) * 3 + 1, 1);
    }

    private static DateTime GetQuarterEnd(DateTime date)
    {
        return GetQuarterStart(date).AddMonths(3).AddTicks(-1);
    }

    private static DateTime StartOfMonth(this DateTime date)
    {
        return new DateTime(date.Year, date.Month, 1);
    }

    private static DateTime EndOfMonth(this DateTime date)
    {
        return date.StartOfMonth().AddMonths(1).AddTicks(-1);
    }
}
