#pragma warning disable DCS001

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public partial class DynamicCrudRepository
{
    public async Task<IEnumerable<dynamic>> GetAllAsync(
        string entity,
        string? search,
        string? sort,
        string? dir,
        Dictionary<string, string?>? filters = null,
        int page = 1,
        int? pageSize = null,
        string? cursor = null,
        bool keyset = false,
        bool fetchOneExtra = false)
    {
        var meta = _meta.Get(entity);
        DynamicCrudMetadataValidator.ValidateMetadata(meta, entity);
        await _rls.EnsurePermissionAsync(meta, "read");
        pageSize ??= meta.Paging.PageSize;

        var activeFilterKeys = filters == null
            ? Array.Empty<string>()
            : filters.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).OrderBy(k => k).ToArray();
        var filterKeysStr = string.Join(",", activeFilterKeys);
        var hasSearch = !string.IsNullOrWhiteSpace(search);

        var rlsKey = "";
        if (meta.Security?.RowLevelSecurity?.Enabled == true)
        {
            var userRoles = await _rls.GetCurrentUserRolesAsync();
            rlsKey = "_" + string.Join(",", userRoles.OrderBy(r => r));
        }

        var cacheKey = $"GetAll_{meta.GetHashCode()}_{sort}_{dir}_{keyset}_{fetchOneExtra}_{hasSearch}_{filterKeysStr}{rlsKey}";

        var tempParam = new DynamicParameters();
        if (!SqlCache.TryGetValue(cacheKey, out var statement))
        {
            var selectList = string.Join(", ",
                meta.Columns.Select(c =>
                    c.Value.Expression != null
                        ? $"{c.Value.Expression} AS {c.Key}"
                        : $"{meta.Table}.{c.Key}"));

            var sql = new List<string> { $"SELECT {selectList} {BuildFromClause(meta)}" };
            var where = BuildWhere(meta, search, filters, tempParam);
            await ApplyRowLevelSecurityAsync(meta, where, tempParam);

            if (keyset)
            {
                var pkColumns = meta.GetPrimaryKeyColumns();
                var firstPk = pkColumns[0];
                if (long.TryParse(cursor, out var cursorValue))
                {
                    where.Add($"{meta.Table}.{firstPk} > @Cursor");
                }

                AppendWhere(sql, where);
                sql.Add($" ORDER BY {meta.Table}.{firstPk} ASC");
            }
            else
            {
                AppendWhere(sql, where);
                if (!string.IsNullOrWhiteSpace(sort) && meta.Columns.TryGetValue(sort, out var colDef) && colDef.Sortable)
                {
                    var expr = colDef.Expression ?? $"{meta.Table}.{sort}";
                    var direction = (dir?.ToLowerInvariant() == "desc") ? "DESC" : "ASC";
                    sql.Add($" ORDER BY {expr} {direction}");
                }
            }

            var tempEffectivePageSize = fetchOneExtra ? pageSize.Value + 1 : pageSize.Value;
            if (keyset)
            {
                _dialect.AppendKeysetPagination(sql, tempParam, tempEffectivePageSize);
            }
            else
            {
                var tempOffset = (page - 1) * pageSize.Value;
                var pkColumns = meta.GetPrimaryKeyColumns();
                var firstPk = pkColumns[0];
                _dialect.AppendNumberedPagination(sql, tempParam, tempEffectivePageSize, tempOffset, $"{meta.Table}.{firstPk}");
            }

            statement = string.Join(Environment.NewLine, sql);
            SqlCache.TryAdd(cacheKey, statement);
        }

        // 重新构建参数，由于是从缓存命中，我们直接运行轻量参数构造
        var param = new DynamicParameters();
        BuildWhere(meta, search, filters, param);
        var dummyWhere = new List<string>();
        await ApplyRowLevelSecurityAsync(meta, dummyWhere, param);

        var effectivePageSize = fetchOneExtra ? pageSize.Value + 1 : pageSize.Value;
        if (keyset)
        {
            if (long.TryParse(cursor, out var cursorValue))
            {
                param.Add("Cursor", cursorValue);
            }
            param.Add("PageSize", effectivePageSize);
        }
        else
        {
            var offset = (page - 1) * pageSize.Value;
            param.Add("PageSize", effectivePageSize);
            param.Add("Offset", offset);
        }

        _logger.LogInformation("GetAllAsync entity={Entity} page={Page} pageSize={PageSize} sql={Sql}", entity, page, pageSize, statement);
        var results = await TimedAsync("GetAllAsync", entity, statement, () => _db.QueryAsync(statement, param));
        
        var secured = new List<dynamic>();
        foreach (var r in results)
        {
            secured.Add(await ApplyFieldSecurityAsync(meta, r));
        }
        return secured;
    }

    public async Task<dynamic?> GetByIdAsync(string entity, object id)
    {
        // 主キー単件取得。soft-delete設定時は削除済みレコードを除外します。
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "read");

        var pkColumns = meta.GetPrimaryKeyColumns();
        if (pkColumns.Count > 1)
        {
            // 複合主鍵：id を JSON またはカンマ区切りとして解析し、辞書オーバーロードに委譲
            var keyValues = ParseCompositeId(id?.ToString() ?? "", pkColumns);
            return await GetByIdAsync(entity, keyValues);
        }

        var rlsKey = "";
        if (meta.Security?.RowLevelSecurity?.Enabled == true)
        {
            var userRoles = await GetCurrentUserRolesAsync();
            rlsKey = "_" + string.Join(",", userRoles.OrderBy(r => r));
        }
        var cacheKey = $"GetById_{meta.GetHashCode()}{rlsKey}";
        
        var param = new DynamicParameters();
        param.Add("Id", id);

        if (!SqlCache.TryGetValue(cacheKey, out var statement))
        {
            var sql = new List<string> { $"SELECT * FROM {meta.Table}" };
            var where = new List<string> { $"{pkColumns[0]} = @Id" };
            if (meta.SoftDelete)
                where.Add(SoftDeleteClause(meta));
            await ApplyRowLevelSecurityAsync(meta, where, param);
            AppendWhere(sql, where);
            statement = string.Join(Environment.NewLine, sql);
            SqlCache.TryAdd(cacheKey, statement);
        }
        else
        {
            var dummyWhere = new List<string>();
            await ApplyRowLevelSecurityAsync(meta, dummyWhere, param);
        }

        _logger.LogInformation("GetByIdAsync entity={Entity} id={Id}", entity, id);
        var row = (await TimedAsync("GetByIdAsync", entity, statement, () => _db.QueryAsync(statement, param))).FirstOrDefault();
        return row != null ? await ApplyFieldSecurityAsync(meta, row) : null;
    }

    /// <summary>
    /// 複合主鍵対応の単件取得。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    public async Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "read");
        var pkColumns = meta.GetPrimaryKeyColumns();

        var rlsKey = "";
        if (meta.Security?.RowLevelSecurity?.Enabled == true)
        {
            var userRoles = await GetCurrentUserRolesAsync();
            rlsKey = "_" + string.Join(",", userRoles.OrderBy(r => r));
        }
        var cacheKey = $"GetByIdComposite_{meta.GetHashCode()}{rlsKey}";
        
        var param = new DynamicParameters();
        if (!SqlCache.TryGetValue(cacheKey, out var statement))
        {
            var sql = new List<string> { $"SELECT * FROM {meta.Table}" };
            var where = new List<string> { BuildCompositeKeyWhere(pkColumns, keyValues, param) };
            if (meta.SoftDelete)
                where.Add(SoftDeleteClause(meta));
            await ApplyRowLevelSecurityAsync(meta, where, param);
            AppendWhere(sql, where);
            statement = string.Join(Environment.NewLine, sql);
            SqlCache.TryAdd(cacheKey, statement);
        }
        else
        {
            BuildCompositeKeyWhere(pkColumns, keyValues, param);
            var dummyWhere = new List<string>();
            await ApplyRowLevelSecurityAsync(meta, dummyWhere, param);
        }

        _logger.LogInformation("GetByIdAsync entity={Entity} keys={Keys}", entity, string.Join(",", pkColumns));
        var row = (await TimedAsync("GetByIdAsync.Composite", entity, statement, () => _db.QueryAsync(statement, param))).FirstOrDefault();
        return row != null ? await ApplyFieldSecurityAsync(meta, row) : null;
    }

    public async Task<IEnumerable<dynamic>> GetAllForEntityAsync(
        string entity,
        ForeignKeyDefinition? foreignKey = null,
        string? search = null,
        int page = 1,
        int? pageSize = null,
        bool fetchOneExtra = false)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "read");
        var pkColumns = meta.GetPrimaryKeyColumns();
        var firstPk = pkColumns[0];
        var fk = foreignKey ?? new ForeignKeyDefinition { Entity = entity, DisplayColumn = "Id" };

        var fkDisplayCols = fk.GetDisplayColumns();
        var fkDisplayColsStr = string.Join(",", fkDisplayCols.OrderBy(c => c));
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var hasPageSize = pageSize.HasValue && pageSize.Value > 0;

        var rlsKey = "";
        if (meta.Security?.RowLevelSecurity?.Enabled == true)
        {
            var userRoles = await GetCurrentUserRolesAsync();
            rlsKey = "_" + string.Join(",", userRoles.OrderBy(r => r));
        }
        var cacheKey = $"GetAllForEntity_{meta.GetHashCode()}_{fk.GetHashCode()}_{fkDisplayColsStr}_{hasSearch}_{hasPageSize}_{fetchOneExtra}{rlsKey}";

        var param = new DynamicParameters();
        if (!SqlCache.TryGetValue(cacheKey, out var statement))
        {
            var baseSql = string.IsNullOrWhiteSpace(fk.Query)
                ? $"SELECT {firstPk} AS Id, * FROM {meta.Table}"
                : fk.Query!.Trim();

            // Append RLS directly to baseSql if it is the default table query
            if (string.IsNullOrWhiteSpace(fk.Query) && meta.Security?.RowLevelSecurity?.Enabled == true)
            {
                var rlsWhere = new List<string>();
                var rlsParam = new DynamicParameters();
                await ApplyRowLevelSecurityAsync(meta, rlsWhere, rlsParam);
                if (rlsWhere.Count > 0)
                {
                    baseSql += " WHERE " + string.Join(" AND ", rlsWhere);
                }
            }

            var sql = new List<string> { $"SELECT * FROM ({baseSql}) fkq" };
            var where = new List<string>();

            if (hasSearch)
            {
                var cols = fk.GetDisplayColumns();
                if (cols.Count > 0)
                {
                    var terms = new List<string>();
                    for (var i = 0; i < cols.Count; i++)
                    {
                        terms.Add($"fkq.{cols[i]} LIKE @Search{i}");
                    }
                    where.Add("(" + string.Join(" OR ", terms) + ")");
                }
            }

            if (where.Count > 0)
            {
                sql.Add("WHERE " + string.Join(" AND ", where));
            }

            sql.Add("ORDER BY fkq.Id ASC");
            if (hasPageSize)
            {
                var tempParam = new DynamicParameters();
                var effectivePageSize = fetchOneExtra ? pageSize!.Value + 1 : pageSize!.Value;
                var offset = Math.Max(0, page - 1) * pageSize.Value;
                _dialect.AppendNumberedPagination(sql, tempParam, effectivePageSize, offset, "fkq.Id");
            }

            statement = string.Join(Environment.NewLine, sql);
            SqlCache.TryAdd(cacheKey, statement);
        }

        // 构建参数
        if (hasSearch)
        {
            var cols = fk.GetDisplayColumns();
            for (var i = 0; i < cols.Count; i++)
            {
                param.Add($"Search{i}", $"%{search}%");
            }
        }
        if (hasPageSize)
        {
            var effectivePageSize = fetchOneExtra ? pageSize!.Value + 1 : pageSize!.Value;
            var offset = Math.Max(0, page - 1) * pageSize.Value;
            param.Add("PageSize", effectivePageSize);
            param.Add("Offset", offset);
        }

        var dummyWhere = new List<string>();
        await ApplyRowLevelSecurityAsync(meta, dummyWhere, param);

        _logger.LogDebug("GetAllForEntityAsync entity={Entity} sql={Sql}", entity, statement);
        var results = await TimedAsync("GetAllForEntityAsync", entity, statement, () => _db.QueryAsync(statement, param));
        
        var secured = new List<dynamic>();
        foreach (var r in results)
        {
            secured.Add(await ApplyFieldSecurityAsync(meta, r));
        }
        return secured;
    }

    public async Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        await EnsurePermissionAsync(meta, "read");

        var activeFilterKeys = filters == null
            ? Array.Empty<string>()
            : filters.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).OrderBy(k => k).ToArray();
        var filterKeysStr = string.Join(",", activeFilterKeys);
        var hasSearch = !string.IsNullOrWhiteSpace(search);

        var rlsKey = "";
        if (meta.Security?.RowLevelSecurity?.Enabled == true)
        {
            var userRoles = await GetCurrentUserRolesAsync();
            rlsKey = "_" + string.Join(",", userRoles.OrderBy(r => r));
        }
        var cacheKey = $"Count_{meta.GetHashCode()}_{hasSearch}_{filterKeysStr}{rlsKey}";

        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var tempSql = $"SELECT COUNT(*) {BuildFromClause(meta)}";
            var tempParam = new DynamicParameters();
            var where = BuildWhere(meta, search, filters, tempParam);
            await ApplyRowLevelSecurityAsync(meta, where, tempParam);

            if (where.Any())
            {
                tempSql += " WHERE " + string.Join(" AND ", where);
            }
            sql = tempSql;
            SqlCache.TryAdd(cacheKey, sql);
        }

        var param = new DynamicParameters();
        BuildWhere(meta, search, filters, param);
        var dummyWhere = new List<string>();
        await ApplyRowLevelSecurityAsync(meta, dummyWhere, param);

        _logger.LogInformation("CountAsync entity={Entity} sql={Sql}", entity, sql);
        return await TimedAsync("CountAsync", entity, sql, () => _db.ExecuteScalarAsync<int>(sql, param));
    }
}
