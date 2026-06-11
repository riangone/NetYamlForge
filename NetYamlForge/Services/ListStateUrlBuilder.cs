// ファイル概要: 一覧画面の状態クエリを Index URL に正規化して返します。
// フィルター・ソート・ページングの URL パラメータを一元管理します。
// _List.cshtml / _FilterControl.cshtml から使用してください。

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace NetYamlForge.Services;

/// <summary>
/// 一覧画面の状態パラメータ（フィルター・ソート・ページング）を URL として構築するユーティリティ。
/// HTMX リクエストの hx-get 値、HX-Push-Url ヘッダー値を統一的に生成します。
/// </summary>
public static class ListStateUrlBuilder
{
    // ブラウザの URL バーに含めないパラメータ
    private static readonly HashSet<string> ExcludedFromStateUrl = new(StringComparer.OrdinalIgnoreCase)
    {
        "clear", "returnUrl", "returnEntity", "returnDisplayName"
    };

    // ページ番号をリセットすべきパラメータキー（ソート・フィルター変更時）
    private static readonly HashSet<string> PageResetTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "sort", "dir", "search", "pageSize"
    };

    private static string? NormalizeSingleValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var first = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? raw : first;
    }

    /// <summary>
    /// クエリコレクションから状態URLを生成します（HX-Push-Url 等で使用）。
    /// </summary>
    public static string BuildIndexStateUrl(string baseIndexUrl, IQueryCollection query, string entity, string? returnUrl = null)
    {
        var stateParams = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            if (ExcludedFromStateUrl.Contains(kv.Key)) continue;

            var value = NormalizeSingleValue(kv.Value.ToString());
            if (!string.IsNullOrWhiteSpace(value))
                stateParams[kv.Key] = value;
        }

        stateParams.TryAdd("entity", entity);
        stateParams.TryAdd("count", "true");
        // returnUrl は URL 肥大化を防ぐため含めない

        if (stateParams.Count == 0) return baseIndexUrl;

        var nonEmpty = stateParams
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return QueryHelpers.AddQueryString(baseIndexUrl, nonEmpty);
    }

    /// <summary>
    /// ソートリンク用 URL を生成します。
    /// 同じ列を再クリックした場合は昇順/降順を反転します。ページはリセットされます。
    /// </summary>
    public static string BuildSortUrl(
        string baseUrl,
        string entity,
        string sortColumn,
        string? currentSort,
        string? currentDir,
        string? search,
        int pageSize,
        IDictionary<string, string?>? filters = null)
    {
        // 同列クリック → 方向反転、別列クリック → 昇順
        var nextDir = string.Equals(sortColumn, currentSort, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentDir, "asc", StringComparison.OrdinalIgnoreCase)
            ? "desc" : "asc";

        var p = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity"]   = entity,
            ["sort"]     = sortColumn,
            ["dir"]      = nextDir,
            ["count"]    = "true",
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(search)) p["search"] = search;
        if (filters != null)
            foreach (var (k, v) in filters)
                if (!string.IsNullOrWhiteSpace(v)) p[k] = v;

        return QueryHelpers.AddQueryString(baseUrl, p!);
    }

    /// <summary>
    /// ページングリンク用 URL を生成します。現在のフィルター・ソート状態を保持します。
    /// </summary>
    public static string BuildPageUrl(
        string baseUrl,
        string entity,
        string? sort,
        string? dir,
        string? search,
        int pageSize,
        int? page = null,
        string? cursor = null,
        IDictionary<string, string?>? filters = null)
    {
        var p = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity"]   = entity,
            ["count"]    = "true",
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(sort))   p["sort"]   = sort;
        if (!string.IsNullOrWhiteSpace(dir))    p["dir"]    = dir;
        if (!string.IsNullOrWhiteSpace(search)) p["search"] = search;
        if (page.HasValue)                      p["page"]   = page.Value.ToString();
        if (!string.IsNullOrWhiteSpace(cursor)) p["cursor"] = cursor;
        if (filters != null)
            foreach (var (k, v) in filters)
                if (!string.IsNullOrWhiteSpace(v)) p[k] = v;

        return QueryHelpers.AddQueryString(baseUrl, p!);
    }

    /// <summary>
    /// 指定パラメータがページ番号リセットを要するかどうかを返します。
    /// </summary>
    public static bool ShouldResetPage(string paramKey) =>
        PageResetTriggers.Contains(paramKey);
}
