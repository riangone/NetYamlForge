// ファイル概要: エンティティ間のリンク URL を組み立てるサービスです。
// EntityLinkDefinition（YAML の links セクション）を解釈し、
// 現在レコードのフィールド値を埋め込んだ遷移先 URL を生成します。
// _List.cshtml のリンクセル描画から呼ばれます。

using NetYamlForge.Controllers;
using NetYamlForge.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace NetYamlForge.Services;

public sealed class DynamicEntityNavigationService
{
    private readonly IEntityMetadataProvider _meta;

    public DynamicEntityNavigationService(IEntityMetadataProvider meta)
    {
        _meta = meta;
    }

    public IReadOnlyList<BreadcrumbItem> BuildBreadcrumbChain(string? returnUrl, int maxDepth = 8)
    {
        var chain = new List<BreadcrumbItem>();
        var current = returnUrl;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrEmpty(current) && maxDepth-- > 0)
        {
            if (!visited.Add(current))
            {
                break;
            }

            var queryStart = current.IndexOf('?');
            var query = queryStart >= 0 ? current[(queryStart + 1)..] : string.Empty;
            if (string.IsNullOrEmpty(query)) break;

            var parsed = QueryHelpers.ParseQuery(query);
            parsed.TryGetValue("entity", out var entityValues);
            var entityName = NormalizeSingleValue(entityValues.ToString());
            if (string.IsNullOrEmpty(entityName)) break;

            var label = entityName;
            if (_meta.TryGet(entityName, out var pageMeta))
            {
                label = pageMeta.GetDisplayName();
            }

            chain.Add(new BreadcrumbItem(label, current));

            parsed.TryGetValue("returnUrl", out var prevValues);
            current = NormalizeSingleValue(prevValues.ToString());
            if (string.IsNullOrEmpty(current)) break;
        }

        chain.Reverse();
        return chain;
    }

    public string? ExtractEntityFromReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        var queryStart = returnUrl.IndexOf('?');
        var query = queryStart >= 0 ? returnUrl[(queryStart + 1)..] : returnUrl;
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        var parsed = QueryHelpers.ParseQuery(query);
        if (parsed.TryGetValue("entity", out var entity) && !string.IsNullOrWhiteSpace(entity))
        {
            return NormalizeSingleValue(entity.ToString());
        }

        return null;
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

