using System.Text.RegularExpressions;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

/// <summary>
/// DynamicCrudRepository のフィルタ適用ロジックを分離したユーティリティクラス。
/// YAML定義フィルターとAI生成動的フィルターの両方を処理します。
/// </summary>
public static class DynamicCrudFilterApplier
{
    private static readonly Regex IdentifierRegex = SqlSafetyGuard.IdentifierRegex;

    /// <summary>
    /// フィルタ条件をWHERE句に適用します。
    /// YAML定義フィルター（UIフィルター）とAI生成動的フィルターの両方を処理します。
    /// </summary>
    public static void ApplyFilters(
        EntityDefinition meta,
        Dictionary<string, string?>? filters,
        List<string> where,
        DynamicParameters param)
    {
        if (filters == null)
        {
            return;
        }

        // 1. YAML定義フィルター（UIフィルター）
        foreach (var f in meta.Filters)
        {
            var key = f.Key;
            var filterType = (f.Value.Type ?? "dropdown").ToLowerInvariant();
            var expr = f.Value.Expression ?? $"{meta.Table}.{key}";

            switch (filterType)
            {
                case "range":
                    FilterSqlBuilder.AppendRange(key, expr, filters, where, param);
                    break;
                case "date-range":
                    FilterSqlBuilder.AppendDateRange(key, expr, filters, where, param);
                    break;
                case "checkbox":
                case "multi-select":
                    FilterSqlBuilder.AppendMultiSelect(key, expr, filters, where, param);
                    break;
                default:
                    FilterSqlBuilder.AppendExact(key, expr, filters, where, param);
                    break;
            }
        }

        // 2. AI生成動的フィルター（YAML未定義だが有効なカラム）
        var yamlFilterKeys = meta.Filters.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validColumns = meta.Columns.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in filters)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;

            var (baseField, op) = ParseDynamicFilterKey(kv.Key);

            if (yamlFilterKeys.Contains(baseField)) continue;
            if (!validColumns.Contains(baseField)) continue;
            if (!SqlSafetyGuard.IsValidIdentifier(baseField)) continue;

            var expr = $"{meta.Table}.{baseField}";
            var pName = $"dyn_{baseField}_{op}";

            switch (op)
            {
                case "eq":
                    where.Add($"{expr} = @{pName}");
                    param.Add(pName, kv.Value);
                    break;
                case "ne":
                    where.Add($"{expr} != @{pName}");
                    param.Add(pName, kv.Value);
                    break;
                case "gt":
                    where.Add($"{expr} > @{pName}");
                    param.Add(pName, kv.Value);
                    break;
                case "lt":
                    where.Add($"{expr} < @{pName}");
                    param.Add(pName, kv.Value);
                    break;
                case "gte":
                    where.Add($"{expr} >= @{pName}");
                    param.Add(pName, kv.Value);
                    break;
                case "lte":
                    where.Add($"{expr} <= @{pName}");
                    param.Add(pName, kv.Value);
                    break;
                case "like":
                    where.Add($"{expr} LIKE @{pName}");
                    param.Add(pName, kv.Value);
                    break;
                case "in":
                    var inVals = kv.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var inParams = inVals.Select((v, i) => $"@{pName}_{i}").ToList();
                    where.Add($"{expr} IN ({string.Join(", ", inParams)})");
                    for (var i = 0; i < inVals.Length; i++)
                        param.Add($"{pName}_{i}", inVals[i].Trim());
                    break;
                case "is_null":
                    where.Add($"{expr} IS NULL");
                    break;
            }
        }
    }

    /// <summary>
    /// QueryExecutionService.BuildFilters のキー形式を解析してベースフィールド名と演算子を返します。
    /// </summary>
    internal static (string BaseField, string Op) ParseDynamicFilterKey(string key)
    {
        if (key.EndsWith("!=", StringComparison.Ordinal)) return (key[..^2], "ne");
        if (key.EndsWith(">=", StringComparison.Ordinal)) return (key[..^2], "gte");
        if (key.EndsWith("<=", StringComparison.Ordinal)) return (key[..^2], "lte");
        if (key.EndsWith(">", StringComparison.Ordinal)) return (key[..^1], "gt");
        if (key.EndsWith("<", StringComparison.Ordinal)) return (key[..^1], "lt");
        if (key.EndsWith(":", StringComparison.Ordinal)) return (key[..^1], "like");
        if (key.EndsWith("[]", StringComparison.Ordinal)) return (key[..^2], "in");
        if (key.EndsWith("__null", StringComparison.Ordinal)) return (key[..^6], "is_null");
        return (key, "eq");
    }
}
