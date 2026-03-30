using System.Text;
using NetYamlForge.Models.AI;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 查询结果格式化服务
/// 将查询结果格式化为 Markdown 或其他展示格式
/// </summary>
public class QueryResultFormatter
{
    /// <summary>
    /// 格式化为 Markdown 表格
    /// </summary>
    public string FormatAsMarkdown(
        List<IDictionary<string, object?>> data,
        ParsedQueryParams? query = null,
        int? total = null)
    {
        if (data.Count == 0)
        {
            return "### 查询结果\n\n没有找到符合条件的数据。";
        }

        var sb = new StringBuilder();

        // 标题
        sb.AppendLine("### 查询结果");
        if (total.HasValue && total > 0)
        {
            sb.AppendLine($"共 {total} 条记录，显示 {data.Count} 条");
        }
        sb.AppendLine();

        // 表头
        var columns = data.First().Keys.ToList();
        sb.AppendLine("| " + string.Join(" | ", columns) + " |");
        sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

        // 数据行
        foreach (var row in data)
        {
            var values = columns.Select(col =>
            {
                var value = row.TryGetValue(col, out var v) ? v : null;
                return FormatCellValue(value);
            });
            sb.AppendLine("| " + string.Join(" | ", values) + " |");
        }

        // 查询摘要
        if (query != null)
        {
            sb.AppendLine();
            sb.AppendLine("#### 查询条件");
            sb.AppendLine($"- **实体**: {query.Entity}");
            
            if (query.Filters.Count > 0)
            {
                sb.AppendLine($"- **过滤**: {FormatFilters(query.Filters)}");
            }
            
            if (query.OrderBy != null)
            {
                sb.AppendLine($"- **排序**: {query.OrderBy.Field} {query.OrderBy.Dir.ToUpper()}");
            }
            
            if (query.Top.HasValue)
            {
                sb.AppendLine($"- **限制**: {query.Top} 条");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 格式化为简洁文本
    /// </summary>
    public string FormatAsText(
        List<IDictionary<string, object?>> data,
        string? title = null)
    {
        if (data.Count == 0)
        {
            return "没有找到符合条件的数据。";
        }

        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(title))
        {
            sb.AppendLine(title);
            sb.AppendLine(new string('=', title.Length));
        }

        foreach (var row in data)
        {
            foreach (var kv in row)
            {
                sb.AppendLine($"{kv.Key}: {FormatCellValue(kv.Value)}");
            }
            sb.AppendLine(new string('-', 40));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 格式化为 JSON（用于 API 响应）
    /// </summary>
    public IDictionary<string, object> FormatAsJson(
        List<IDictionary<string, object?>> data,
        ParsedQueryParams? query = null,
        int? total = null)
    {
        return new Dictionary<string, object>
        {
            ["data"] = data,
            ["total"] = total ?? data.Count,
            ["query"] = query ?? new ParsedQueryParams()
        };
    }

    /// <summary>
    /// 格式化单元格值
    /// </summary>
    private string FormatCellValue(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "-";
        }

        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm");
        }

        if (value is decimal d)
        {
            return d.ToString("N2");
        }

        if (value is double dbl)
        {
            return dbl.ToString("N2");
        }

        if (value is bool b)
        {
            return b ? "✓" : "✗";
        }

        var str = value.ToString();
        if (str != null && str.Length > 50)
        {
            return str[..47] + "...";
        }

        return str ?? "-";
    }

    /// <summary>
    /// 格式化过滤器描述
    /// </summary>
    private string FormatFilters(List<FilterClause> filters)
    {
        var parts = filters.Select(f =>
        {
            var opDesc = f.Op switch
            {
                "eq" => "=",
                "ne" => "≠",
                "gt" => ">",
                "lt" => "<",
                "gte" => "≥",
                "lte" => "≤",
                "like" => "包含",
                "between" => "在...之间",
                "is_null" => "为空",
                "in" => "属于",
                _ => f.Op
            };

            var valueDesc = f.Op switch
            {
                "between" => $"{f.Value} ~ {f.Value2}",
                "is_null" => "",
                _ => $"{f.Value}"
            };

            return $"{f.Field} {opDesc} {valueDesc}";
        });

        return string.Join(" AND ", parts);
    }
}
