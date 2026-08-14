#pragma warning disable DCS001

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public partial class DynamicCrudRepository
{
    private static Dictionary<string, object?> ParseCompositeId(string idStr, IReadOnlyList<string> pkColumns)
    {
        var result = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(idStr))
        {
            try
            {
                using var doc = JsonDocument.Parse(idStr);
                var root = doc.RootElement;
                foreach (var col in pkColumns)
                {
                    if (root.TryGetProperty(col, out var el))
                        result[col] = el.ValueKind == JsonValueKind.Number ? (object?)el.GetInt64() : el.GetString();
                    else
                        result[col] = null;
                }
                return result;
            }
            catch
            {
                var parts = idStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < pkColumns.Count; i++)
                    result[pkColumns[i]] = i < parts.Length ? parts[i] : null;
                return result;
            }
        }

        foreach (var col in pkColumns) result[col] = null;
        return result;
    }

    /// <summary>
    /// 複合主キーの WHERE 句（col0 = @Pk0 AND col1 = @Pk1 ...）を組み立て、
    /// DynamicParameters にバインド値を追加する。
    /// </summary>
    private static string BuildCompositeKeyWhere(
        IReadOnlyList<string> pkColumns,
        IDictionary<string, object?> keyValues,
        DynamicParameters param,
        string paramPrefix = "Pk")
    {
        var whereParts = new List<string>(pkColumns.Count);
        for (var i = 0; i < pkColumns.Count; i++)
        {
            var col = pkColumns[i];
            var paramName = $"{paramPrefix}{i}";
            whereParts.Add($"{col} = @{paramName}");
            param.Add(paramName, keyValues.TryGetValue(col, out var val) ? val : null);
        }
        return string.Join(" AND ", whereParts);
    }

    /// <summary>論理削除フィルター条件を返す。</summary>
    private static string SoftDeleteClause(EntityDefinition meta)
    {
        var col = meta.SoftDeleteColumn;
        return $"({meta.Table}.{col} = 0 OR {meta.Table}.{col} IS NULL)";
    }

    private static void ApplyFilters(
        EntityDefinition meta,
        Dictionary<string, string?>? filters,
        List<string> where,
        DynamicParameters param)
    {
        DynamicCrudFilterApplier.ApplyFilters(meta, filters, where, param);
    }

    /// <summary>
    /// QueryExecutionService.BuildFilters のキー形式を解析してベースフィールド名と演算子を返します。
    /// 例: "tier_level" → ("tier_level", "eq"), "price>" → ("price", "gt")
    /// </summary>
    private static (string BaseField, string Op) ParseDynamicFilterKey(string key)
    {
        return DynamicCrudFilterApplier.ParseDynamicFilterKey(key);
    }

    private List<string> BuildWhere(
        EntityDefinition meta,
        string? search,
        Dictionary<string, string?>? filters,
        DynamicParameters param)
    {
        // 検索条件 + フィルタ条件 + softDelete条件を一元的に合成します。
        var where = new List<string>();
        DynamicCrudFilterApplier.ApplyFilters(meta, filters, where, param);

        _rls.ApplyTenantContext(meta, where, param);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchable = meta.Columns.Where(c => c.Value.Searchable).ToList();
            if (searchable.Any())
            {
                var likeClauses = new List<string>();
                foreach (var col in searchable)
                {
                    var expr = col.Value.Expression ?? $"{meta.Table}.{col.Key}";
                    var p = $"@s_{col.Key}";
                    likeClauses.Add($"{expr} LIKE {p}");
                    param.Add(p, $"%{search}%");
                }

                where.Add("(" + string.Join(" OR ", likeClauses) + ")");
            }
        }

        if (meta.SoftDelete)
            where.Add(SoftDeleteClause(meta));

        return where;
    }

    private static void AppendWhere(List<string> sql, List<string> where)
    {
        if (where.Any())
        {
            sql.Add("WHERE " + string.Join(" AND ", where));
        }
    }

    private static string BuildFromClause(EntityDefinition meta)
    {
        // YAML定義のJOINを含めたFROM句を生成します。
        var parts = new List<string> { $"FROM {meta.Table}" };
        foreach (var j in meta.Joins)
        {
            var joinOn = j.GetJoinCondition();
            parts.Add($"{j.Type.ToUpperInvariant()} JOIN {j.Table} {j.Alias} ON {joinOn}");
        }

        return string.Join(" ", parts);
    }

    private static void ValidateMetadata(EntityDefinition meta, string entityName)
    {
        DynamicCrudMetadataValidator.ValidateMetadata(meta, entityName);
    }
}
