using System.Text.RegularExpressions;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

/// <summary>
/// DynamicCrudRepository のメタデータ検証ロジックを分離したユーティリティクラス。
/// YAML由来メタデータの安全性チェックとSQL注入防止を担当します。
/// </summary>
public static class DynamicCrudMetadataValidator
{
    private static readonly Regex IdentifierRegex = SqlSafetyGuard.IdentifierRegex;
    private static readonly HashSet<string> AllowedJoinTypes = new(StringComparer.OrdinalIgnoreCase) { "left", "inner", "right" };

    // 安全校验済みメタデータのキャッシュ
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<EntityDefinition, bool> ValidatedMetadataCache = new();

    /// <summary>
    /// YAML由来メタデータの安全性チェックを実行します。
    /// SQL注入に繋がる文字や不正なトークンを事前に拒否します。
    /// </summary>
    public static void ValidateMetadata(EntityDefinition meta, string entityName)
    {
        if (ValidatedMetadataCache.ContainsKey(meta))
        {
            return;
        }

        SqlSafetyGuard.EnsureIdentifier(entityName, "entity");
        SqlSafetyGuard.EnsureIdentifier(meta.Table, $"{entityName}.table");

        // 複合主鍵対応
        var pkColumns = meta.GetPrimaryKeyColumns();
        foreach (var pkCol in pkColumns)
        {
            SqlSafetyGuard.EnsureIdentifier(pkCol, $"{entityName}.key.{pkCol}");
        }

        foreach (var col in meta.Columns)
        {
            SqlSafetyGuard.EnsureIdentifier(col.Key, $"{entityName}.column");
            if (col.Value.Expression != null)
            {
                SqlSafetyGuard.EnsureExpression(col.Value.Expression, $"{entityName}.columnExpression.{col.Key}");
            }
        }

        foreach (var form in meta.Forms)
        {
            SqlSafetyGuard.EnsureIdentifier(form.Key, $"{entityName}.form");
            if (form.Value.ForeignKey != null)
            {
                EnsureForeignKey(form.Value.ForeignKey, $"{entityName}.form.fk.{form.Key}");
            }
        }

        foreach (var filter in meta.Filters)
        {
            SqlSafetyGuard.EnsureIdentifier(filter.Key, $"{entityName}.filter");
            if (filter.Value.Expression != null)
            {
                SqlSafetyGuard.EnsureExpression(filter.Value.Expression, $"{entityName}.filterExpression.{filter.Key}");
            }

            if (filter.Value.ForeignKey != null)
            {
                EnsureForeignKey(filter.Value.ForeignKey, $"{entityName}.filter.fk.{filter.Key}");
            }
        }

        foreach (var j in meta.Joins)
        {
            if (!AllowedJoinTypes.Contains(j.Type))
            {
                throw new InvalidOperationException($"Unsafe join type '{entityName}.joinType': {j.Type}");
            }

            SqlSafetyGuard.EnsureIdentifier(j.Table, $"{entityName}.joinTable");
            SqlSafetyGuard.EnsureIdentifier(j.Alias, $"{entityName}.joinAlias");
            var joinOn = j.GetJoinCondition();
            SqlSafetyGuard.EnsureExpression(joinOn, $"{entityName}.joinOn");
        }

        // 安全校验完全通过，加入缓存
        ValidatedMetadataCache.TryAdd(meta, true);
    }

    private static void EnsureForeignKey(ForeignKeyDefinition fk, string name)
    {
        var displayColumns = fk.GetDisplayColumns();
        if (displayColumns.Count == 0)
        {
            throw new InvalidOperationException($"fk.displayColumn(s) is required: {name}");
        }

        foreach (var displayCol in displayColumns)
        {
            SqlSafetyGuard.EnsureIdentifier(displayCol, $"{name}.displayColumn");
        }

        if (!string.IsNullOrWhiteSpace(fk.Query))
        {
            var query = fk.Query!.Trim();
            if (SqlSafetyGuard.IsUnsafeToken(query))
            {
                throw new InvalidOperationException($"Unsafe fk.query '{name}': {query}");
            }

            if (!query.StartsWith("select", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"fk.query must start with SELECT: {name}");
            }

            if (!Regex.IsMatch(query, "\\bId\\b", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException($"fk.query must include Id column: {name}");
            }
        }
    }
}
