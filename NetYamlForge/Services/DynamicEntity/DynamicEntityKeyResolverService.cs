// ファイル概要: URLパラメータから主キー値を解決するサービスです。
// 単一主キー（?id=1）・複合主キー（?OrderId=1&ProductId=2）・
// JSON形式（?id={"OrderId":1,"ProductId":2}）・カンマ区切り（?id=1,2）を統一的に処理します。
// Controller の EditForm / Update / Delete アクションで使用されます。

using Microsoft.AspNetCore.Http;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

/// <summary>
/// HTTP クエリパラメータから主キー値を抽出・解決するサービス。
/// 単一主キーと複合主キーの両方に対応します。
/// </summary>
public sealed class DynamicEntityKeyResolverService
{
    /// <summary>
    /// 単一主キー値を解決します。ルートの {id} パラメータを優先し、
    /// なければクエリの主キー列名を検索します。
    /// </summary>
    /// <param name="meta">エンティティ定義（主キー列名の取得に使用）</param>
    /// <param name="id">ルートパラメータの id 値（nullable）</param>
    /// <param name="query">HTTPリクエストのクエリパラメータコレクション</param>
    /// <returns>主キー値文字列、見つからない場合は null</returns>
    public string? ResolvePrimaryKeyValue(EntityDefinition meta, string? id, IQueryCollection query)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        var pkColumns = meta.GetPrimaryKeyColumns();
        foreach (var col in pkColumns)
        {
            var value = NormalizeSingleValue(query[col].FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// 主キー値を列名 → 値 のディクショナリとして解決します。複合主キーに対応します。
    /// <para>解決の試行順序:</para>
    /// <list type="number">
    ///   <item>クエリパラメータ（?OrderId=1&amp;ProductId=2）</item>
    ///   <item>JSON形式のidパラメータ（?id={"OrderId":1,"ProductId":2}）</item>
    ///   <item>カンマ区切りのidパラメータ（?id=1,2 → 主キー列の順に割り当て）</item>
    /// </list>
    /// </summary>
    public IDictionary<string, object?> ResolveKeyValues(EntityDefinition meta, string? id, IQueryCollection query)
    {
        var pkColumns = meta.GetPrimaryKeyColumns();
        if (pkColumns.Count == 1)
        {
            var key = pkColumns[0];
            return new Dictionary<string, object?>
            {
                [key] = ResolvePrimaryKeyValue(meta, id, query)
            };
        }

        var result = new Dictionary<string, object?>();
        foreach (var col in pkColumns)
        {
            result[col] = query[col].FirstOrDefault();
        }

        if (result.Any(kv => kv.Value != null))
        {
            return result;
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(id);
                if (parsed != null && parsed.Count > 0)
                {
                    return parsed;
                }
            }
            catch
            {
                var parts = id.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < Math.Min(parts.Length, pkColumns.Count); i++)
                {
                    result[pkColumns[i]] = parts[i];
                }
            }
        }

        return result;
    }

    private static string? NormalizeSingleValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }
}

