// ファイル概要: 動的CRUDのSQL組み立て・検索・更新処理を提供するリポジトリ実装です。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。
//
// DCS001 抑制理由: このファイルはSQL動的生成エンジンです。すべてのテーブル名・列名は
// IdentifierRegex / ValidateIdentifier で検証済みです。新しいSQL補間を追加する場合は
// 必ず事前にIdentifierRegex検証を実施してください。
#pragma warning disable DCS001

using System;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services.Dialect;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.Tenant;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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

public partial class DynamicCrudRepository : IDynamicCrudRepository
{
    private readonly IDbConnection _db;
    private readonly IEntityMetadataProvider _meta;
    private readonly ILogger<DynamicCrudRepository> _logger;
    private readonly ISqlDialect _dialect;
    private readonly DynamicCrudRowLevelSecurity _rls;
    private int _slowQueryThresholdMs;
    private int _slowQuerySummaryIntervalMs;
    private long _lastSettingsRefreshUnixMs;
    private static readonly ConcurrentDictionary<string, long> SlowQueryCounters = new(StringComparer.OrdinalIgnoreCase);
    private static long _lastSlowSummaryUnixMs;

    // SQL 语句模板缓存，避免高并发下的字符串拼接开销
    private static readonly ConcurrentDictionary<string, string> SqlCache = new();

    public DynamicCrudRepository(
        IDbConnection db,
        IEntityMetadataProvider meta,
        ISqlDialect dialect,
        ILogger<DynamicCrudRepository> logger,
        DynamicCrudRowLevelSecurity rls,
        IHttpContextAccessor? httpContextAccessor = null,
        IProjectBusinessLogicRegistry? bizLogicRegistry = null,
        IUserAuthService? userAuthService = null,
        ProjectScope? projectScope = null,
        TenantContext? tenantContext = null)
    {
        _db = db;
        _meta = meta;
        _dialect = dialect;
        _logger = logger;
        _rls = rls;
        _slowQueryThresholdMs = ResolveSlowQueryThresholdMs();
        _slowQuerySummaryIntervalMs = ResolveSlowQuerySummaryIntervalMs();
        _lastSettingsRefreshUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
}
