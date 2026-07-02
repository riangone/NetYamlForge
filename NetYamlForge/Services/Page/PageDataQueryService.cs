// ファイル概要: カスタムページのデータ取得クエリを実行するサービス。
// 責務: pages/*.yml に定義されたカスタムSQLクエリを安全に実行してデータを返す。
// エンティティ一覧クエリ（DynamicCrudRepository.GetAllAsync）とは別系統。
// このファイルを変更する場面: カスタムページのクエリ検証ルール変更のみ。

using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public sealed class PageDataQueryService
{
    // IdentifierRegex は SqlSafetyGuard から参照（重複定義排除）
    private static readonly Regex IdentifierRegex = SqlSafetyGuard.IdentifierRegex;
    private readonly IDbConnection _db;
    private readonly ILogger<PageDataQueryService> _logger;

    public PageDataQueryService(IDbConnection db, ILogger<PageDataQueryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 単一セクションのデータを取得する。HTMX 部分更新で使用。
    /// allFilters には全セクション共有のクエリパラメータ（prefixed）を渡す。
    /// </summary>
    public Task<(IEnumerable<Dictionary<string, object>> Rows, int Total)> LoadSectionDataAsync(
        SectionDefinition section,
        IDictionary<string, string> allFilters,
        IEnumerable<string>? allSectionIds = null,
        PageUserContext? userContext = null)
        => GetSectionDataAsync(section, ExtractSectionFilters(section, allFilters, allSectionIds), userContext);

    public async Task<Dictionary<string, (IEnumerable<Dictionary<string, object>> Rows, int Total)>> LoadPageDataAsync(
        PageDefinition page,
        IDictionary<string, string> filters,
        PageUserContext? userContext = null)
    {
        var result = new Dictionary<string, (IEnumerable<Dictionary<string, object>>, int)>();

        foreach (var section in page.Sections)
        {
            if (section.Hidden) continue;
            try
            {
                var (rows, total) = await GetSectionDataAsync(
                    section,
                    ExtractSectionFilters(section, filters, page.Sections.Select(s => s.Id)),
                    userContext);
                result[section.Id] = (rows, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Section data load failed. section={SectionId}", section.Id);
                result[section.Id] = (Enumerable.Empty<Dictionary<string, object>>(), 0);
            }
        }

        return result;
    }

    /// <summary>全体フィルター辞書から指定セクション用のフィルターを抽出する。</summary>
    private static Dictionary<string, string?> ExtractSectionFilters(
        SectionDefinition section,
        IDictionary<string, string> allFilters,
        IEnumerable<string>? allSectionIds = null)
    {
        var prefix = $"{section.Id}_";
        var sectionFilters = allFilters
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key[(section.Id.Length + 1)..], kv => (string?)kv.Value);

        var sectionPrefixes = (allSectionIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => $"{id}_")
            .ToList();

        // Include global filters that are not scoped to any section.
        // This allows common parameters like 'id', 'month', 'category_id', 'tag_id' to be shared across sections.
        foreach (var kv in allFilters)
        {
            var isSectionScoped = sectionPrefixes.Any(p => kv.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (!isSectionScoped && !sectionFilters.ContainsKey(kv.Key))
            {
                sectionFilters[kv.Key] = kv.Value;
            }
        }

        if (!string.IsNullOrEmpty(section.ForeignKey) &&
            !string.IsNullOrEmpty(section.LocalForeignKey) &&
            allFilters.TryGetValue(section.ForeignKey, out var fkVal))
        {
            sectionFilters[section.ForeignKey] = fkVal;
        }

        return sectionFilters;
    }

    private async Task<(IEnumerable<Dictionary<string, object>> Rows, int Total)> GetSectionDataAsync(
        SectionDefinition section,
        IDictionary<string, string?> filters,
        PageUserContext? userContext = null)
    {
        var param = new DynamicParameters();
        InjectUserContext(param, userContext);

        // 1. Build WHERE clause and bind parameters for defined filters.
        var where = BuildWhereClause(section, filters, param);
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        // 2. Bind all remaining filters that are not yet in param.
        // This makes global parameters like @category_id available to custom SQL.
        var existingNames = new HashSet<string>(param.ParameterNames, StringComparer.OrdinalIgnoreCase);
        foreach (var filter in filters)
        {
            if (!existingNames.Contains(filter.Key))
            {
                param.Add(filter.Key, filter.Value);
                existingNames.Add(filter.Key);
            }
        }

        // 3. Scan for missing parameters in custom source and bind to null.
        if (section.SourceType == "custom" && !string.IsNullOrWhiteSpace(section.Source))
        {
            var matches = Regex.Matches(section.Source, @"@([a-zA-Z0-9_]+)");
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                if (!existingNames.Contains(name))
                {
                    param.Add(name, null);
                    existingNames.Add(name);
                }
            }
        }

        string sql;
        string countSql;

        // ソートパラメータ（_sort / _dir）を解決する
        // URL パラメータ: {section.Id}__sort=col & {section.Id}__dir=asc|desc
        var requestedSort = filters.TryGetValue("_sort", out var rs) && rs is not null && IdentifierRegex.IsMatch(rs) ? rs : null;
        var requestedDir = filters.TryGetValue("_dir", out var rd) ? rd : null;
        var sortDir = string.Equals(requestedDir ?? section.DefaultSortDir, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        if (section.SourceType == "custom")
        {
            if (string.IsNullOrWhiteSpace(section.Source))
            {
                return (Enumerable.Empty<Dictionary<string, object>>(), 0);
            }

            var firstCol = section.Columns.Keys.FirstOrDefault() ?? "1";
            var defaultOrder = IdentifierRegex.IsMatch(firstCol) ? $"\"{firstCol}\"" : "1";
            var configuredSort = !string.IsNullOrWhiteSpace(section.DefaultSort) && IdentifierRegex.IsMatch(section.DefaultSort)
                ? $"\"{section.DefaultSort}\""
                : null;
            var orderBy = requestedSort != null ? $"\"{requestedSort}\"" : (configuredSort ?? defaultOrder);

            // Safe: section.Source is a trusted SQL subquery from YAML (admin-managed), orderBy/whereSql use only validated identifiers
#pragma warning disable DCS001
            sql = $@"
                SELECT * FROM ({section.Source}) AS _src
                {whereSql}
                ORDER BY {orderBy} {sortDir}
                LIMIT @_Size OFFSET @_Offset";

            countSql = $@"
                SELECT COUNT(*) FROM ({section.Source}) AS _src
                {whereSql}";
#pragma warning restore DCS001
        }
        else
        {
            if (string.IsNullOrWhiteSpace(section.Source) ||
                !IdentifierRegex.IsMatch(section.Source))
            {
                return (Enumerable.Empty<Dictionary<string, object>>(), 0);
            }

            var visibleCols = section.GetVisibleColumnNames();
            var cols = visibleCols.Count > 0
                ? string.Join(", ", visibleCols
                    .Where(c => IdentifierRegex.IsMatch(c))
                    .Select(c => $"\"{c}\""))
                : "*";

            var firstCol = visibleCols.FirstOrDefault() ?? "1";
            var defaultOrder = IdentifierRegex.IsMatch(firstCol) ? $"\"{firstCol}\"" : "1";
            var orderBy = requestedSort != null ? $"\"{requestedSort}\"" : defaultOrder;

            // Safe: section.Source/cols are validated by IdentifierRegex above; orderBy/whereSql use validated identifiers only
#pragma warning disable DCS001
            sql = $@"
                SELECT {cols} FROM ""{section.Source}""
                {whereSql}
                ORDER BY {orderBy} {sortDir}
                LIMIT @_Size OFFSET @_Offset";

            countSql = $@"
                SELECT COUNT(*) FROM ""{section.Source}""
                {whereSql}";
#pragma warning restore DCS001
        }

        var pageNum = filters.TryGetValue("_page", out var pgStr) && int.TryParse(pgStr, out var pgNum)
            ? Math.Max(1, pgNum) : 1;
        var effectivePageSize = filters.TryGetValue("_pagesize", out var psStr) &&
                                int.TryParse(psStr, out var psVal) && psVal > 0
            ? psVal : section.GetEffectivePageSize();
        param.Add("_Offset", (pageNum - 1) * effectivePageSize);
        param.Add("_Size", effectivePageSize);

        var rows = await _db.QueryAsync(sql, param);
        var total = await _db.ExecuteScalarAsync<int>(countSql, param);

        var dicts = rows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return d.ToDictionary(kv => kv.Key, kv => kv.Value);
        });

        return (dicts, total);
    }

    public async Task<Dictionary<string, object>?> GetRowByIdAsync(SectionDefinition section, int rowId)
    {
        if (string.IsNullOrWhiteSpace(section.TargetTable) || string.IsNullOrWhiteSpace(section.TargetPrimaryKey))
            return null;
        if (!IdentifierRegex.IsMatch(section.TargetTable) || !IdentifierRegex.IsMatch(section.TargetPrimaryKey))
            return null;

        // Safe: TargetTable/TargetPrimaryKey validated by IdentifierRegex
#pragma warning disable DCS001
        var sql = $"SELECT * FROM \"{section.TargetTable}\" WHERE \"{section.TargetPrimaryKey}\" = @Id LIMIT 1";
#pragma warning restore DCS001
        var row = await _db.QueryFirstOrDefaultAsync(sql, new { Id = rowId });
        if (row == null) return null;
        var d = (IDictionary<string, object>)row;
        return d.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static List<string> BuildWhereClause(
        SectionDefinition section,
        IDictionary<string, string?> filters,
        DynamicParameters param)
    {
        var where = new List<string>();
        var sectionFilters = section.Filters ?? new Dictionary<string, PageFilterDefinition>();

        foreach (var f in sectionFilters)
        {
            if (!IdentifierRegex.IsMatch(f.Key))
                continue;

            var key = f.Key;

            // If this is a custom SQL query and the query string already contains the parameter (e.g. @q),
            // skip adding it to the outer WHERE clause, allowing the custom SQL to handle the filtering natively.
            if (section.SourceType == "custom" &&
                !string.IsNullOrEmpty(section.Source) &&
                section.Source.Contains("@" + key))
            {
                if (filters.TryGetValue(key, out var val))
                {
                    param.Add(key, val);
                }
                continue;
            }

            var expr = $"\"{key}\"";
            var filterType = (f.Value.Type ?? "eq").ToLowerInvariant();

            switch (filterType)
            {
                case "gte":
                    FilterSqlBuilder.AppendGte(key, expr, filters, where, param);
                    break;
                case "lte":
                    FilterSqlBuilder.AppendLte(key, expr, filters, where, param);
                    break;
                case "range":
                    FilterSqlBuilder.AppendRange(key, expr, filters, where, param);
                    break;
                case "date-range":
                case "date_range":
                    FilterSqlBuilder.AppendDateRange(key, expr, filters, where, param);
                    break;
                case "like":
                    FilterSqlBuilder.AppendLike(key, expr, filters, where, param);
                    break;
                default:
                    FilterSqlBuilder.AppendExact(key, expr, filters, where, param);
                    break;
            }
        }

        if (!string.IsNullOrEmpty(section.ForeignKey) &&
            !string.IsNullOrEmpty(section.LocalForeignKey) &&
            IdentifierRegex.IsMatch(section.LocalForeignKey) &&
            filters.TryGetValue(section.ForeignKey, out var fkValue) &&
            !string.IsNullOrWhiteSpace(fkValue))
        {
            where.Add($"\"{section.LocalForeignKey}\" = @{section.ForeignKey}");
            param.Add(section.ForeignKey, fkValue);
        }

        return where;
    }

    /// <summary>
    /// DynamicParameters にログインユーザー情報を注入する。
    /// SQL 内で @currentUser / @isAdmin 等として参照可能になる。
    /// </summary>
    private static void InjectUserContext(DynamicParameters param, PageUserContext? ctx)
    {
        var user = ctx ?? PageUserContext.Anonymous;
        param.Add("currentUser", user.UserName);
        param.Add("currentUserDisplayName", user.DisplayName);
        param.Add("currentUserId", user.UserId);
        param.Add("currentUserRole", user.Roles.FirstOrDefault() ?? "");
        param.Add("isAdmin", user.IsAdmin ? 1 : 0);
        param.Add("isAuthenticated", user.IsAuthenticated ? 1 : 0);
        // 現在の UI 言語 (例: zh-CN / en-US / ja-JP / ko-KR)。
        // カスタム SQL 内で CASE @currentLanguage ... のように使い、
        // 表示用リテラル文字列を UI 言語に応じて切り替えるために利用する。
        param.Add("currentLanguage", System.Globalization.CultureInfo.CurrentUICulture.Name);
    }

}
