using System.Text;
using NetYamlForge.Models.AI;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.AI;

/// <summary>
/// 查询结果格式化服务
/// 将查询结果格式化为 Markdown 或其他展示格式
/// </summary>
public class QueryResultFormatter
{
    private readonly ILogger<QueryResultFormatter> _logger;

    public QueryResultFormatter(ILogger<QueryResultFormatter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 格式化为 Markdown（新ルール：件数と概要のみ、詳細リンク付き）
    /// </summary>
    public string FormatAsMarkdown(
        List<IDictionary<string, object?>> data,
        ParsedQueryParams? query = null,
        int? total = null,
        string? project = null)
    {
        if (data.Count == 0)
        {
            return "該当件数：0 件\n\n条件に一致するデータはありませんでした。";
        }

        var sb = new StringBuilder();

        // 件数表示
        var totalCount = total ?? data.Count;
        sb.AppendLine($"該当件数：{totalCount} 件");
        sb.AppendLine();

        // 各レコードの概要表示（詳細リンク付き）
        var entity = query?.Entity ?? "unknown";
        var keyField = InferKeyField(entity, data.First().Keys);
        
        _logger.LogInformation("[FormatAsMarkdown] entity={Entity}, keyField={KeyField}, data.Count={Count}", entity, keyField, data.Count);

        foreach (var row in data)
        {
            var summary = FormatRecordSummary(entity, row, keyField, project);
            _logger.LogDebug("[FormatAsMarkdown] row summary: {Summary}", summary);
            sb.AppendLine(summary);
        }

        // 制限表示
        if (data.Count < totalCount)
        {
            sb.AppendLine();
            sb.AppendLine($"※ 最初の {data.Count} 件のみ表示しています。");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// レコードの概要をフォーマット（新ルール：簡潔な情報のみ）
    /// </summary>
    private string FormatRecordSummary(string entity, IDictionary<string, object?> row, string? keyField, string? project)
    {
        var parts = new List<string>();
        var projectName = project ?? "auto-dealer-demo";

        // メイン情報（名前・タイトルなど）
        var mainInfo = ExtractMainInfo(entity, row);
        if (!string.IsNullOrEmpty(mainInfo))
        {
            parts.Add($"**{mainInfo}**");
        }

        // ステータス
        var status = ExtractStatus(entity, row);
        if (!string.IsNullOrEmpty(status))
        {
            parts.Add($"({status})");
        }

        // 日付・金額などの追加情報
        var extraInfo = ExtractExtraInfo(entity, row);
        if (!string.IsNullOrEmpty(extraInfo))
        {
            parts.Add($"— {extraInfo}");
        }

        // 詳細リンク
        var linkText = "";
        if (!string.IsNullOrEmpty(keyField) && row.TryGetValue(keyField, out var idVal) && idVal != null)
        {
            var id = idVal.ToString();
            if (!string.IsNullOrEmpty(id))
            {
                linkText = $"— [詳細を見る](/{projectName}/DynamicEntity/DetailPage?entity={entity}&id={id})";
            }
        }

        if (parts.Count == 0 && !string.IsNullOrEmpty(keyField) && row.TryGetValue(keyField, out var keyVal) && keyVal != null)
        {
            parts.Add($"ID: {keyVal}");
        }

        return $"- {string.Join(" ", parts)}{linkText}";
    }

    /// <summary>
    /// メイン情報（名前・タイトル等）を抽出
    /// </summary>
    private string? ExtractMainInfo(string entity, IDictionary<string, object?> row)
    {
        // 顧客
        if (entity == "customers")
        {
            if (row.TryGetValue("name", out var nameVal)) return nameVal?.ToString();
        }
        // 車両
        if (entity == "vehicles")
        {
            var brand = row.TryGetValue("brand", out var b) ? b?.ToString() : "";
            var model = row.TryGetValue("model", out var m) ? m?.ToString() : "";
            var year = row.TryGetValue("year", out var y) ? y?.ToString() : "";
            if (!string.IsNullOrEmpty(brand) || !string.IsNullOrEmpty(model))
            {
                return $"{year} {brand} {model}".Trim();
            }
        }
        // 予約
        if (entity == "service_appointments")
        {
            if (row.TryGetValue("appointment_type", out var typeVal))
            {
                var type = typeVal?.ToString() ?? "";
                if (type == "test_drive") return "試乗予約";
                if (type == "service") return "サービス予約";
                if (type == "consultation") return "相談予約";
                return type;
            }
        }
        // リード
        if (entity == "sales_leads")
        {
            if (row.TryGetValue("customer_id", out var cidVal)) return $"リード #{cidVal}";
        }

        // デフォルト：最初の文字列フィールド
        foreach (var kv in row)
        {
            if (kv.Value is string s && !string.IsNullOrEmpty(s) && s.Length < 100)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>
    /// ステータスを抽出
    /// </summary>
    private string? ExtractStatus(string entity, IDictionary<string, object?> row)
    {
        // 顧客ランク
        if (entity == "customers" && row.TryGetValue("tier_level", out var tierVal))
        {
            var tier = tierVal?.ToString();
            if (tier == "vip") return "VIP";
            if (tier == "gold") return "GOLD";
            if (tier == "silver") return "SILVER";
            if (tier == "regular") return "一般";
        }
        // 車両ステータス
        if (entity == "vehicles" && row.TryGetValue("status", out var statusVal))
        {
            var status = statusVal?.ToString();
            if (status == "available") return "販売中";
            if (status == "reserved") return "商談中";
            if (status == "sold") return "売約済";
            return status;
        }
        // 予約ステータス
        if (entity == "service_appointments" && row.TryGetValue("status", out var aptStatusVal))
        {
            var status = aptStatusVal?.ToString();
            if (status == "pending") return "未確認";
            if (status == "confirmed") return "確定";
            if (status == "completed") return "完了";
            if (status == "cancelled") return "キャンセル";
            return status;
        }
        // リードステータス
        if (entity == "sales_leads" && row.TryGetValue("status", out var leadStatusVal))
        {
            var status = leadStatusVal?.ToString();
            if (status == "new") return "新規";
            if (status == "active") return "進行中";
            if (status == "won") return "成約";
            if (status == "lost") return "失注";
            return status;
        }

        return null;
    }

    /// <summary>
    /// 追加情報（日付・金額など）を抽出
    /// </summary>
    private string? ExtractExtraInfo(string entity, IDictionary<string, object?> row)
    {
        var parts = new List<string>();

        // 顧客：最終来店日
        if (entity == "customers")
        {
            if (row.TryGetValue("last_visit_date", out var lastVisitVal) && lastVisitVal != null)
            {
                parts.Add($"最終来店：{FormatDate(lastVisitVal)}");
            }
            if (row.TryGetValue("phone", out var phoneVal) && phoneVal != null)
            {
                parts.Add($"📞 {phoneVal}");
            }
        }
        // 車両：価格・走行距離
        if (entity == "vehicles")
        {
            if (row.TryGetValue("price", out var priceVal) && priceVal != null)
            {
                parts.Add($"¥{FormatNumber(priceVal):N0}");
            }
            if (row.TryGetValue("mileage", out var mileageVal) && mileageVal != null)
            {
                parts.Add($"{mileageVal}km");
            }
        }
        // 予約：希望日時
        if (entity == "service_appointments")
        {
            if (row.TryGetValue("preferred_date", out var prefDateVal) && prefDateVal != null)
            {
                parts.Add($"希望日：{FormatDate(prefDateVal)}");
            }
        }
        // リード：興味のある車両
        if (entity == "sales_leads")
        {
            if (row.TryGetValue("vehicle_interest", out var vehicleVal) && vehicleVal != null)
            {
                parts.Add($"興味：{vehicleVal}");
            }
            if (row.TryGetValue("created_at", out var createdAtVal) && createdAtVal != null)
            {
                parts.Add($"作成日：{FormatDate(createdAtVal)}");
            }
        }

        return parts.Count > 0 ? string.Join(" — ", parts) : null;
    }

    /// <summary>
    /// キーフィールドを推定
    /// </summary>
    private string? InferKeyField(string entity, IEnumerable<string> fields)
    {
        if (fields.Contains($"{entity.TrimEnd('s')}_id"))
            return $"{entity.TrimEnd('s')}_id";
        if (fields.Contains("id"))
            return "id";
        if (fields.Contains($"{entity}_id"))
            return $"{entity}_id";
        
        // 文字列フィールドを優先
        return fields.FirstOrDefault(f => f.EndsWith("_id") || f == "id" || f == "code");
    }

    /// <summary>
    /// 日付フォーマット
    /// </summary>
    private string FormatDate(object? value)
    {
        if (value == null || value == DBNull.Value) return "-";
        if (value is DateTime dt) return dt.ToString("yyyy/MM/dd");
        var str = value.ToString();
        if (str != null && str.Length >= 10) return str[..10].Replace("-", "/");
        return str ?? "-";
    }

    /// <summary>
    /// 数値フォーマット
    /// </summary>
    private decimal FormatNumber(object? value)
    {
        if (value == null || value == DBNull.Value) return 0;
        if (value is decimal d) return d;
        if (value is double dbl) return (decimal)dbl;
        if (value is int i) return i;
        if (decimal.TryParse(value.ToString(), out var result)) return result;
        return 0;
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
