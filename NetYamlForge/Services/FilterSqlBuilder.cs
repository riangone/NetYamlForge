// ファイル概要: フィルタータイプ別の SQL WHERE 句追加ロジックを集約するビルダー。
// DynamicCrudRepository と PageDataQueryService の共通フィルター処理を一元管理する。
// このファイルを変更する場面: フィルタータイプの追加・変更時のみ。

using System.Globalization;
using Dapper;

namespace NetYamlForge.Services;

internal static class FilterSqlBuilder
{
    /// <summary>range フィルター (decimal): key_min / key_max</summary>
    public static void AppendRange(
        string key, string expr,
        IDictionary<string, string?> filters,
        List<string> where, DynamicParameters param)
    {
        if (filters.TryGetValue($"{key}_min", out var minRaw) && !string.IsNullOrWhiteSpace(minRaw) &&
            decimal.TryParse(minRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var min))
        {
            where.Add($"{expr} >= @{key}_min");
            param.Add($"{key}_min", min);
        }

        if (filters.TryGetValue($"{key}_max", out var maxRaw) && !string.IsNullOrWhiteSpace(maxRaw) &&
            decimal.TryParse(maxRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var max))
        {
            where.Add($"{expr} <= @{key}_max");
            param.Add($"{key}_max", max);
        }
    }

    /// <summary>date-range フィルター: key_from / key_to</summary>
    public static void AppendDateRange(
        string key, string expr,
        IDictionary<string, string?> filters,
        List<string> where, DynamicParameters param)
    {
        if (filters.TryGetValue($"{key}_from", out var fromRaw) && !string.IsNullOrWhiteSpace(fromRaw) &&
            FilterValueParser.TryParseDateBound(fromRaw, upperBound: false, out var fromNorm))
        {
            where.Add($"{expr} >= @{key}_from");
            param.Add($"{key}_from", fromNorm);
        }

        if (filters.TryGetValue($"{key}_to", out var toRaw) && !string.IsNullOrWhiteSpace(toRaw) &&
            FilterValueParser.TryParseDateBound(toRaw, upperBound: true, out var toNorm))
        {
            where.Add($"{expr} <= @{key}_to");
            param.Add($"{key}_to", toNorm);
        }
    }

    /// <summary>gte フィルター: filters[key] = 下限, filters[key_to] = 上限（省略可）</summary>
    public static void AppendGte(
        string key, string expr,
        IDictionary<string, string?> filters,
        List<string> where, DynamicParameters param)
    {
        if (filters.TryGetValue(key, out var fromVal) && !string.IsNullOrWhiteSpace(fromVal) &&
            FilterValueParser.TryParseDateBound(fromVal, upperBound: false, out var fromNorm))
        {
            where.Add($"{expr} >= @{key}_from");
            param.Add($"{key}_from", fromNorm);
        }

        if (filters.TryGetValue($"{key}_to", out var toVal) && !string.IsNullOrWhiteSpace(toVal) &&
            FilterValueParser.TryParseDateBound(toVal, upperBound: true, out var toNorm))
        {
            where.Add($"{expr} <= @{key}_to");
            param.Add($"{key}_to", toNorm);
        }
    }

    /// <summary>lte フィルター: filters[key] = 上限, filters[key_from] = 下限（省略可）</summary>
    public static void AppendLte(
        string key, string expr,
        IDictionary<string, string?> filters,
        List<string> where, DynamicParameters param)
    {
        if (filters.TryGetValue(key, out var toVal) && !string.IsNullOrWhiteSpace(toVal) &&
            FilterValueParser.TryParseDateBound(toVal, upperBound: true, out var toNorm))
        {
            where.Add($"{expr} <= @{key}_to");
            param.Add($"{key}_to", toNorm);
        }

        if (filters.TryGetValue($"{key}_from", out var fromVal) && !string.IsNullOrWhiteSpace(fromVal) &&
            FilterValueParser.TryParseDateBound(fromVal, upperBound: false, out var fromNorm))
        {
            where.Add($"{expr} >= @{key}_from");
            param.Add($"{key}_from", fromNorm);
        }
    }

    /// <summary>checkbox / multi-select フィルター: カンマ区切り → IN 句</summary>
    public static void AppendMultiSelect(
        string key, string expr,
        IDictionary<string, string?> filters,
        List<string> where, DynamicParameters param)
    {
        if (!filters.TryGetValue(key, out var multiRaw) || string.IsNullOrWhiteSpace(multiRaw))
            return;

        var parts = multiRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var tokens = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var pName = $"{key}_{i}";
            tokens.Add($"@{pName}");
            param.Add(pName, parts[i]);
        }

        where.Add($"{expr} IN ({string.Join(", ", tokens)})");
    }

    /// <summary>完全一致フィルター: = @key</summary>
    public static void AppendExact(
        string key, string expr,
        IDictionary<string, string?> filters,
        List<string> where, DynamicParameters param)
    {
        if (filters.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            where.Add($"{expr} = @{key}");
            param.Add(key, val);
        }
    }

    /// <summary>部分一致フィルター: LIKE %val%</summary>
    public static void AppendLike(
        string key, string expr,
        IDictionary<string, string?> filters,
        List<string> where, DynamicParameters param)
    {
        if (filters.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            where.Add($"{expr} LIKE @{key}");
            param.Add(key, $"%{val}%");
        }
    }
}
