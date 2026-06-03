// ファイル概要: 動的CRUDのSQL組み立て・検索・更新処理を提供するリポジトリ実装です。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。
//
// DCS001 抑制理由: このファイルはSQL動的生成エンジンです。すべてのテーブル名・列名は
// IdentifierRegex / ValidateIdentifier で検証済みです。新しいSQL補間を追加する場合は
// 必ず事前にIdentifierRegex検証を実施してください。
#pragma warning disable DCS001

using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services.Dialect;

namespace NetYamlForge.Services;

public interface IDynamicCrudRepository
{
    // 一覧取得: 通常ページング / count省略 / keysetカーソル方式をサポートします。
    Task<IEnumerable<dynamic>> GetAllAsync(
        string entity,
        string? search,
        string? sort,
        string? dir,
        Dictionary<string, string?>? filters = null,
        int page = 1,
        int? pageSize = null,
        string? cursor = null,
        bool keyset = false,
        bool fetchOneExtra = false);
    Task<dynamic?> GetByIdAsync(string entity, object id);
    /// <summary>
    /// 複合主鍵対応の単件取得。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues);
    // 登録系は監査ログ連携のため外部トランザクションを受け取れる設計です。
    Task<int> InsertAsync(string entity, IDictionary<string, object?> values, IDbTransaction? tx = null);
    Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, IDbTransaction? tx = null);
    /// <summary>
    /// 複合主鍵対応の更新メソッド。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, IDbTransaction? tx = null);
    Task<int> DeleteAsync(string entity, object id, IDbTransaction? tx = null);
    /// <summary>
    /// 複合主鍵対応の削除メソッド。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, IDbTransaction? tx = null);
    Task<IEnumerable<dynamic>> GetAllForEntityAsync(
        string entity,
        ForeignKeyDefinition? foreignKey = null,
        string? search = null,
        int page = 1,
        int? pageSize = null,
        bool fetchOneExtra = false);
    Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null);
}

public class DynamicCrudRepository : IDynamicCrudRepository
{
    private readonly IDbConnection _db;
    private readonly IEntityMetadataProvider _meta;
    private readonly ILogger<DynamicCrudRepository> _logger;
    private readonly ISqlDialect _dialect;
    private int _slowQueryThresholdMs;
    private int _slowQuerySummaryIntervalMs;
    private long _lastSettingsRefreshUnixMs;
    private static readonly ConcurrentDictionary<string, long> SlowQueryCounters = new(StringComparer.OrdinalIgnoreCase);
    private static long _lastSlowSummaryUnixMs;
    // IdentifierRegex / ExpressionRegex は SqlSafetyGuard から参照（重複定義排除）
    private static readonly Regex IdentifierRegex = SqlSafetyGuard.IdentifierRegex;
    private static readonly Regex ExpressionRegex = SqlSafetyGuard.ExpressionRegex;
    private static readonly HashSet<string> AllowedJoinTypes = new(StringComparer.OrdinalIgnoreCase) { "left", "inner", "right" };

    // 缓存已通过安全校验的实体元数据，避免每次请求重复进行正则和标识符校验
    private static readonly ConcurrentDictionary<EntityDefinition, bool> ValidatedMetadataCache = new();

    // SQL 语句模板缓存，避免高并发下的字符串拼接开销
    private static readonly ConcurrentDictionary<string, string> SqlCache = new();

    public DynamicCrudRepository(IDbConnection db, IEntityMetadataProvider meta, ISqlDialect dialect, ILogger<DynamicCrudRepository> logger)
    {
        _db = db;
        _meta = meta;
        _dialect = dialect;
        _logger = logger;
        _slowQueryThresholdMs = ResolveSlowQueryThresholdMs();
        _slowQuerySummaryIntervalMs = ResolveSlowQuerySummaryIntervalMs();
        _lastSettingsRefreshUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

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
        ValidateMetadata(meta, entity);
        pageSize ??= meta.Paging.PageSize;

        var activeFilterKeys = filters == null
            ? Array.Empty<string>()
            : filters.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).OrderBy(k => k).ToArray();
        var filterKeysStr = string.Join(",", activeFilterKeys);
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var cacheKey = $"GetAll_{meta.GetHashCode()}_{sort}_{dir}_{keyset}_{fetchOneExtra}_{hasSearch}_{filterKeysStr}";

        if (!SqlCache.TryGetValue(cacheKey, out var statement))
        {
            var selectList = string.Join(", ",
                meta.Columns.Select(c =>
                    c.Value.Expression != null
                        ? $"{c.Value.Expression} AS {c.Key}"
                        : $"{meta.Table}.{c.Key}"));

            var sql = new List<string> { $"SELECT {selectList} {BuildFromClause(meta)}" };
            var tempParam = new DynamicParameters();
            var where = BuildWhere(meta, search, filters, tempParam);

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
        return await TimedAsync("GetAllAsync", entity, statement, () => _db.QueryAsync(statement, param));
    }

    public async Task<dynamic?> GetByIdAsync(string entity, object id)
    {
        // 主キー単件取得。soft-delete設定時は削除済みレコードを除外します。
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);

        var pkColumns = meta.GetPrimaryKeyColumns();
        if (pkColumns.Count > 1)
        {
            // 複合主鍵：id を JSON またはカンマ区切りとして解析し、辞書オーバーロードに委譲
            var keyValues = ParseCompositeId(id?.ToString() ?? "", pkColumns);
            return await GetByIdAsync(entity, keyValues);
        }

        var cacheKey = $"GetById_{meta.GetHashCode()}";
        if (!SqlCache.TryGetValue(cacheKey, out var statement))
        {
            var sql = new StringBuilder();
            sql.AppendLine($"SELECT * FROM {meta.Table} WHERE {pkColumns[0]} = @Id");
            if (meta.SoftDelete)
                sql.Append($" AND {SoftDeleteClause(meta.Table)}");
            statement = sql.ToString();
            SqlCache.TryAdd(cacheKey, statement);
        }

        _logger.LogInformation("GetByIdAsync entity={Entity} id={Id}", entity, id);
        return (await TimedAsync("GetByIdAsync", entity, statement, () => _db.QueryAsync(statement, new { Id = id }))).FirstOrDefault();
    }

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
    /// 複合主鍵対応の単件取得。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    public async Task<dynamic?> GetByIdAsync(string entity, IDictionary<string, object?> keyValues)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        var pkColumns = meta.GetPrimaryKeyColumns();

        var cacheKey = $"GetByIdComposite_{meta.GetHashCode()}";
        if (!SqlCache.TryGetValue(cacheKey, out var statement))
        {
            var tempParam = new DynamicParameters();
            var whereClause = BuildCompositeKeyWhere(pkColumns, keyValues, tempParam);
            var sql = new StringBuilder($"SELECT * FROM {meta.Table} WHERE {whereClause}");
            if (meta.SoftDelete)
                sql.Append($" AND {SoftDeleteClause(meta.Table)}");
            statement = sql.ToString();
            SqlCache.TryAdd(cacheKey, statement);
        }

        var param = new DynamicParameters();
        BuildCompositeKeyWhere(pkColumns, keyValues, param);

        _logger.LogInformation("GetByIdAsync entity={Entity} keys={Keys}", entity, string.Join(",", pkColumns));
        return (await TimedAsync("GetByIdAsync.Composite", entity, statement, () => _db.QueryAsync(statement, param))).FirstOrDefault();
    }

    public async Task<int> InsertAsync(string entity, IDictionary<string, object?> values, IDbTransaction? tx = null)
    {
        // identity列を除外してINSERT文を动的生成します。
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);

        var cacheKey = $"Insert_{meta.GetHashCode()}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var cols = meta.Columns
                .Where(c => !c.Value.Identity && string.IsNullOrWhiteSpace(c.Value.Expression))
                .Select(c => c.Key)
                .ToArray();

            var colList = string.Join(", ", cols);
            var paramList = string.Join(", ", cols.Select(c => "@" + c));
            sql = $"INSERT INTO {meta.Table} ({colList}) VALUES ({paramList});";
            SqlCache.TryAdd(cacheKey, sql);
        }

        _logger.LogInformation("InsertAsync entity={Entity} sql={Sql}", entity, sql);
        return await TimedAsync("InsertAsync", entity, sql, () => _db.ExecuteAsync(sql, values, tx));
    }

    public async Task<int> UpdateAsync(string entity, object id, IDictionary<string, object?> values, IDbTransaction? tx = null)
    {
        // editable=falseのフォーム列は更新対象から除外します。
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        var pkColumns = meta.GetPrimaryKeyColumns();

        var valuesKeysStr = string.Join(",", values.Keys.OrderBy(k => k));
        var cacheKey = $"Update_{meta.GetHashCode()}_{valuesKeysStr}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var fields = meta.Forms
                .Where(f => !f.Value.Identity && values.ContainsKey(f.Key) && f.Value.Editable)
                .Select(f => f.Key)
                .ToArray();

            var setClause = string.Join(", ", fields.Select(f => $"{f} = @{f}"));
            var whereParts = pkColumns.Select((col, i) => $"{col} = @Pk{i}");
            sql = $"UPDATE {meta.Table} SET {setClause} WHERE {string.Join(" AND ", whereParts)}";
            SqlCache.TryAdd(cacheKey, sql);
        }

        var param = new DynamicParameters(values);
        // 複合主鍵の場合は values から主鍵値を取得、単一主鍵の場合は id を使用
        for (var i = 0; i < pkColumns.Count; i++)
        {
            var col = pkColumns[i];
            if (values.TryGetValue(col, out var pkVal) && pkVal != null)
            {
                param.Add($"Pk{i}", pkVal);
            }
            else
            {
                // Fallback: use id parameter for single PK or first PK column
                param.Add($"Pk{i}", i == 0 ? id : null);
            }
        }

        _logger.LogInformation("UpdateAsync entity={Entity} id={Id} sql={Sql}", entity, id, sql);
        return await TimedAsync("UpdateAsync", entity, sql, () => _db.ExecuteAsync(sql, param, tx));
    }

    /// <summary>
    /// 複合主鍵対応の更新メソッド。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    public async Task<int> UpdateAsync(string entity, IDictionary<string, object?> keyValues, IDictionary<string, object?> values, IDbTransaction? tx = null)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        var pkColumns = meta.GetPrimaryKeyColumns();

        var valuesKeysStr = string.Join(",", values.Keys.OrderBy(k => k));
        var cacheKey = $"UpdateComposite_{meta.GetHashCode()}_{valuesKeysStr}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var fields = meta.Forms
                .Where(f => !f.Value.Identity && values.ContainsKey(f.Key) && f.Value.Editable)
                .Select(f => f.Key)
                .ToArray();

            var setClause = string.Join(", ", fields.Select(f => $"{f} = @{f}"));
            var tempParam = new DynamicParameters();
            var whereClause = BuildCompositeKeyWhere(pkColumns, keyValues, tempParam);
            sql = $"UPDATE {meta.Table} SET {setClause} WHERE {whereClause}";
            SqlCache.TryAdd(cacheKey, sql);
        }

        var param = new DynamicParameters(values);
        BuildCompositeKeyWhere(pkColumns, keyValues, param);

        _logger.LogInformation("UpdateAsync entity={Entity} keys={Keys} sql={Sql}", entity, string.Join(",", pkColumns), sql);
        return await TimedAsync("UpdateAsync.Composite", entity, sql, () => _db.ExecuteAsync(sql, param, tx));
    }

    public async Task<int> DeleteAsync(string entity, object id, IDbTransaction? tx = null)
    {
        // softDelete=trueなら論理削除、falseなら物理削除を実行します。
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        var pkColumns = meta.GetPrimaryKeyColumns();

        var cacheKey = $"Delete_{meta.GetHashCode()}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            // 単一主鍵: @Id で直接バインド。複合主鍵: @Id0, @Id1... で展開。
            var whereClause = pkColumns.Count == 1
                ? $"{pkColumns[0]} = @Id"
                : string.Join(" AND ", pkColumns.Select((col, i) => $"{col} = @Id{i}"));

            if (meta.SoftDelete)
            {
                sql = $"UPDATE {meta.Table} SET IsDeleted = 1 WHERE {whereClause}";
            }
            else
            {
                sql = $"DELETE FROM {meta.Table} WHERE {whereClause}";
            }
            SqlCache.TryAdd(cacheKey, sql);
        }

        var args = new { Id = id };
        if (meta.SoftDelete)
        {
            _logger.LogInformation("SoftDelete entity={Entity} id={Id}", entity, id);
            return await TimedAsync("SoftDelete", entity, sql, () => _db.ExecuteAsync(sql, args, tx));
        }

        _logger.LogInformation("Delete entity={Entity} id={Id}", entity, id);
        return await TimedAsync("Delete", entity, sql, () => _db.ExecuteAsync(sql, args, tx));
    }

    /// <summary>
    /// 複合主鍵対応の削除メソッド。主鍵値をディクショナリ形式で受け取る。
    /// </summary>
    public async Task<int> DeleteAsync(string entity, IDictionary<string, object?> keyValues, IDbTransaction? tx = null)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);
        var pkColumns = meta.GetPrimaryKeyColumns();

        var cacheKey = $"DeleteComposite_{meta.GetHashCode()}";
        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var tempParam = new DynamicParameters();
            var whereClause = BuildCompositeKeyWhere(pkColumns, keyValues, tempParam);
            if (meta.SoftDelete)
            {
                sql = $"UPDATE {meta.Table} SET IsDeleted = 1 WHERE {whereClause}";
            }
            else
            {
                sql = $"DELETE FROM {meta.Table} WHERE {whereClause}";
            }
            SqlCache.TryAdd(cacheKey, sql);
        }

        var param = new DynamicParameters();
        BuildCompositeKeyWhere(pkColumns, keyValues, param);

        if (meta.SoftDelete)
        {
            _logger.LogInformation("SoftDelete entity={Entity} keys={Keys}", entity, string.Join(",", pkColumns));
            return await TimedAsync("SoftDelete.Composite", entity, sql, () => _db.ExecuteAsync(sql, param, tx));
        }

        _logger.LogInformation("Delete entity={Entity} keys={Keys}", entity, string.Join(",", pkColumns));
        return await TimedAsync("Delete.Composite", entity, sql, () => _db.ExecuteAsync(sql, param, tx));
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
        var pkColumns = meta.GetPrimaryKeyColumns();
        var firstPk = pkColumns[0];
        var fk = foreignKey ?? new ForeignKeyDefinition { Entity = entity, DisplayColumn = "Id" };

        var fkDisplayCols = fk.GetDisplayColumns();
        var fkDisplayColsStr = string.Join(",", fkDisplayCols.OrderBy(c => c));
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var hasPageSize = pageSize.HasValue && pageSize.Value > 0;
        var cacheKey = $"GetAllForEntity_{meta.GetHashCode()}_{fk.GetHashCode()}_{fkDisplayColsStr}_{hasSearch}_{hasPageSize}_{fetchOneExtra}";

        if (!SqlCache.TryGetValue(cacheKey, out var statement))
        {
            var baseSql = string.IsNullOrWhiteSpace(fk.Query)
                ? $"SELECT {firstPk} AS Id, * FROM {meta.Table}"
                : fk.Query!.Trim();

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
        var param = new DynamicParameters();
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

        _logger.LogDebug("GetAllForEntityAsync entity={Entity} sql={Sql}", entity, statement);
        return await TimedAsync("GetAllForEntityAsync", entity, statement, () => _db.QueryAsync(statement, param));
    }

    public async Task<int> CountAsync(string entity, string? search, Dictionary<string, string?>? filters = null)
    {
        var meta = _meta.Get(entity);
        ValidateMetadata(meta, entity);

        var activeFilterKeys = filters == null
            ? Array.Empty<string>()
            : filters.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).OrderBy(k => k).ToArray();
        var filterKeysStr = string.Join(",", activeFilterKeys);
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var cacheKey = $"Count_{meta.GetHashCode()}_{hasSearch}_{filterKeysStr}";

        if (!SqlCache.TryGetValue(cacheKey, out var sql))
        {
            var tempSql = $"SELECT COUNT(*) {BuildFromClause(meta)}";
            var tempParam = new DynamicParameters();
            var where = BuildWhere(meta, search, filters, tempParam);

            if (where.Any())
            {
                tempSql += " WHERE " + string.Join(" AND ", where);
            }
            sql = tempSql;
            SqlCache.TryAdd(cacheKey, sql);
        }

        var param = new DynamicParameters();
        BuildWhere(meta, search, filters, param);

        _logger.LogInformation("CountAsync entity={Entity} sql={Sql}", entity, sql);
        return await TimedAsync("CountAsync", entity, sql, () => _db.ExecuteScalarAsync<int>(sql, param));
    }

    private async Task<T> TimedAsync<T>(string operation, string entity, string sql, Func<Task<T>> action)
    {
        RefreshSlowQuerySettingsIfNeeded();
        var sw = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds >= _slowQueryThresholdMs)
            {
                var counterKey = $"{operation}:{entity}";
                var totalCount = SlowQueryCounters.AddOrUpdate(counterKey, 1, (_, current) => current + 1);
                _logger.LogWarning(
                    "Slow query detected op={Operation} entity={Entity} elapsedMs={ElapsedMs} thresholdMs={ThresholdMs} slowCount={SlowCount} sql={Sql}",
                    operation, entity, sw.ElapsedMilliseconds, _slowQueryThresholdMs, totalCount, sql);

                if (totalCount % 10 == 0)
                {
                    _logger.LogInformation(
                        "Slow query metric summary op={Operation} entity={Entity} totalSlowCount={SlowCount}",
                        operation, entity, totalCount);
                }

                TryEmitSlowQuerySnapshot();
            }
        }
    }

    private static int ResolveSlowQueryThresholdMs()
    {
        var raw = Environment.GetEnvironmentVariable("DYNAMICCRUD_SLOW_QUERY_MS");
        return int.TryParse(raw, out var ms) && ms > 0 ? ms : 500;
    }

    private static int ResolveSlowQuerySummaryIntervalMs()
    {
        var raw = Environment.GetEnvironmentVariable("DYNAMICCRUD_SLOW_QUERY_SUMMARY_MS");
        return int.TryParse(raw, out var ms) && ms > 0 ? ms : 300000;
    }

    private void RefreshSlowQuerySettingsIfNeeded()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - Interlocked.Read(ref _lastSettingsRefreshUnixMs) < 10000)
        {
            return;
        }

        var previousThreshold = _slowQueryThresholdMs;
        var previousSummary = _slowQuerySummaryIntervalMs;
        var nextThreshold = ResolveSlowQueryThresholdMs();
        var nextSummary = ResolveSlowQuerySummaryIntervalMs();
        _slowQueryThresholdMs = nextThreshold;
        _slowQuerySummaryIntervalMs = nextSummary;
        Interlocked.Exchange(ref _lastSettingsRefreshUnixMs, now);

        if (previousThreshold != nextThreshold || previousSummary != nextSummary)
        {
            _logger.LogInformation(
                "Slow query settings reloaded thresholdMs={ThresholdMs} summaryIntervalMs={SummaryIntervalMs}",
                nextThreshold,
                nextSummary);
        }
    }

    private void TryEmitSlowQuerySnapshot()
    {
        if (_slowQuerySummaryIntervalMs <= 0 || SlowQueryCounters.IsEmpty)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var last = Interlocked.Read(ref _lastSlowSummaryUnixMs);
        if (now - last < _slowQuerySummaryIntervalMs)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastSlowSummaryUnixMs, now, last) != last)
        {
            return;
        }

        var snapshot = SlowQueryCounters
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToArray();

        if (snapshot.Length == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Slow query metric snapshot intervalMs={IntervalMs} topCounters={Counters}",
            _slowQuerySummaryIntervalMs,
            string.Join(", ", snapshot));
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
    private static string SoftDeleteClause(string tableName) =>
        $"({tableName}.IsDeleted = 0 OR {tableName}.IsDeleted IS NULL)";

    private static void ApplyFilters(
        EntityDefinition meta,
        Dictionary<string, string?>? filters,
        List<string> where,
        DynamicParameters param)
    {
        // フィルタ型ごとのSQL変換:
        // dropdown=一致, multi/checkbox=IN, range/date-range=境界条件
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
        // QueryExecutionService.BuildFilters が生成するキー形式を解析して適用する
        var yamlFilterKeys = meta.Filters.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validColumns   = meta.Columns.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in filters)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;

            var (baseField, op) = ParseDynamicFilterKey(kv.Key);

            if (yamlFilterKeys.Contains(baseField)) continue;          // YAML定義済み → スキップ
            if (!validColumns.Contains(baseField)) continue;           // 無効カラム → スキップ
            if (!SqlSafetyGuard.IsValidIdentifier(baseField)) continue; // 識別子検証

            var expr     = $"{meta.Table}.{baseField}";
            var pName    = $"dyn_{baseField}_{op}";

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
                    param.Add(pName, kv.Value); // BuildFilters が既に %...% を付けている
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
    /// 例: "tier_level" → ("tier_level", "eq"), "price>" → ("price", "gt")
    /// </summary>
    private static (string BaseField, string Op) ParseDynamicFilterKey(string key)
    {
        if (key.EndsWith("!=",     StringComparison.Ordinal)) return (key[..^2], "ne");
        if (key.EndsWith(">=",     StringComparison.Ordinal)) return (key[..^2], "gte");
        if (key.EndsWith("<=",     StringComparison.Ordinal)) return (key[..^2], "lte");
        if (key.EndsWith(">",      StringComparison.Ordinal)) return (key[..^1], "gt");
        if (key.EndsWith("<",      StringComparison.Ordinal)) return (key[..^1], "lt");
        if (key.EndsWith(":",      StringComparison.Ordinal)) return (key[..^1], "like");
        if (key.EndsWith("[]",     StringComparison.Ordinal)) return (key[..^2], "in");
        if (key.EndsWith("__null", StringComparison.Ordinal)) return (key[..^6], "is_null");
        return (key, "eq");
    }

    private static List<string> BuildWhere(
        EntityDefinition meta,
        string? search,
        Dictionary<string, string?>? filters,
        DynamicParameters param)
    {
        // 検索条件 + フィルタ条件 + softDelete条件を一元的に合成します。
        var where = new List<string>();
        ApplyFilters(meta, filters, where, param);

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
            where.Add(SoftDeleteClause(meta.Table));

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
        if (ValidatedMetadataCache.ContainsKey(meta))
        {
            return;
        }

        // YAML由来メタデータの安全性チェック。
        // SQL注入に繋がる文字や不正なトークンを事前に拒否します。
        static void EnsureForeignKey(ForeignKeyDefinition fk, string name)
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

        SqlSafetyGuard.EnsureIdentifier(entityName, "entity");
        SqlSafetyGuard.EnsureIdentifier(meta.Table, $"{entityName}.table");
        
        // 複合主鍵対応：Keys が設定されていればそれら、そうでなければ Key を検証
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
}
