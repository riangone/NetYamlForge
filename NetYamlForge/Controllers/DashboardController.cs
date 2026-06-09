// ファイル概要: ダッシュボード画面を担当するコントローラです。
// プロジェクト別の dashboard.yml で定義した統計カードとグラフを DB から集計して表示します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。
//
// DCS001 抑制理由: このファイルのSQL補間はすべて dashboard.yml（管理者管理）から読み込んだ
// 識別子・式を使用しています。Filter/GroupExpression はSQL式として定義されており、
// エンドユーザーの入力ではありません。YAMLスキーマ検証（YamlSchemaValidator）で構造を検証済み。
#pragma warning disable DCS001

using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using NetYamlForge.Services;
using NetYamlForge.Services.Connection;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

// ─── ViewModels ──────────────────────────────────────────────────────────────

public class DashboardStatViewModel
{
    public string Label      { get; set; } = "";
    public string Value      { get; set; } = "";
    public string? Icon      { get; set; }
    public string? Color     { get; set; }
    /// <summary>クリック時の遷移先 URL（エンティティ一覧）</summary>
    public string? EntityUrl { get; set; }
    /// <summary>クエリ失敗または設定エラー時に true。ビューでエラー表示に切り替える。</summary>
    public bool HasError     { get; set; }
    /// <summary>エラー時のユーザー向けヒント（"設定エラー" / "クエリ失敗"）</summary>
    public string? ErrorHint { get; set; }
}

public class DashboardChartViewModel
{
    public string Title        { get; set; } = "";
    public string Type         { get; set; } = "bar";
    public string LabelsJson   { get; set; } = "[]";
    public string ValuesJson   { get; set; } = "[]";
    public string? ColorBg     { get; set; }
    public string? ColorBorder { get; set; }
    /// <summary>doughnut / pie 用 JSON カラー配列（null の場合は ColorBg 単色）</summary>
    public string? ColorsJson  { get; set; }
    /// <summary>クエリ失敗または設定エラー時に true。ビューでエラー表示に切り替える。</summary>
    public bool HasError       { get; set; }
    /// <summary>エラー時のユーザー向けヒント（"設定エラー" / "クエリ失敗"）</summary>
    public string? ErrorHint   { get; set; }
}

public class DashboardViewModel
{
    public List<DashboardStatViewModel>  Stats  { get; set; } = new();
    public List<DashboardChartViewModel> Charts { get; set; } = new();
}

// ─── Controller ──────────────────────────────────────────────────────────────

[Route("{project}/Dashboard/{action=Index}")]
public class DashboardController : Controller
{
    private readonly IDashboardConfigProvider        _dashConfig;
    private readonly IEntityMetadataProvider         _meta;
    private readonly IDbConnection                   _db;
    private readonly ProjectScope                    _projectScope;
    private readonly ILogger<DashboardController>    _logger;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { WriteIndented = false };

    public DashboardController(
        IDashboardConfigProvider     dashConfig,
        IEntityMetadataProvider      meta,
        IDbConnection                db,
        ProjectScope                 projectScope,
        ILogger<DashboardController> logger)
    {
        _dashConfig   = dashConfig;
        _meta         = meta;
        _db           = db;
        _projectScope = projectScope;
        _logger       = logger;
    }

    // ─── Index ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        var config = _dashConfig.GetConfig();
        var vm     = new DashboardViewModel();

        // 統計カード
        vm.Stats = await BuildStatsAsync(config);

        // グラフ
        vm.Charts = await BuildChartsAsync(config);

        // テーマ・プロジェクト表示名を ViewData に渡す
        var projectName = _projectScope.IsSet ? _projectScope.Current.Name : null;
        var theme = _projectScope.IsSet
            ? (_projectScope.Current.Layout?.DashboardTheme ?? "default")
            : "default";
        ViewData["DashboardTheme"]       = theme;
        ViewData["ProjectDisplayName"]   = _projectScope.IsSet ? _projectScope.Current.DisplayName : "Dashboard";

        // プロジェクト固有の Dashboard/Index.cshtml があればそちらを優先
        if (!string.IsNullOrEmpty(projectName))
        {
            var projectViewPath = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(),
                $"projects/{projectName}/views/Dashboard/Index.cshtml");
            if (System.IO.File.Exists(projectViewPath))
                return View($"/projects/{projectName}/views/Dashboard/Index.cshtml", vm);
        }

        // "Index" 名のプロジェクトトップビューとの解決衝突を避けるため、ビューを明示指定
        return View("~/Views/Dashboard/Index.cshtml", vm);
    }

    // ─── Stats ───────────────────────────────────────────────────────────────

    private Task<List<DashboardStatViewModel>> BuildStatsAsync(
        Models.DashboardConfig config)
    {
        var result = new List<DashboardStatViewModel>();
        var conn = _db is LazyDbConnection lazyConn ? lazyConn.InnerConnection : _db;
        var projectName = _projectScope.IsSet ? _projectScope.Current.Name : null;

        if (conn.State != ConnectionState.Open)
        {
            conn.Open();
        }

        foreach (var stat in config.Stats)
        {
            if (!_meta.TryGet(stat.Entity, out var meta))
                continue;

            try
            {
                // SQL 安全検証: YAML 設定値をホワイトリスト正規表現で検証（危険なキーワードを遮断）
                if (!string.IsNullOrEmpty(stat.Column))
                    SqlSafetyGuard.EnsureIdentifier(stat.Column, $"dashboard.stats[{stat.Entity}].column");
                SqlSafetyGuard.EnsureExpression(stat.Filter, $"dashboard.stats[{stat.Entity}].filter");

                string sql = stat.Aggregate.ToLowerInvariant() switch
                {
                    "sum" when !string.IsNullOrEmpty(stat.Column)
                        => $"SELECT COALESCE(SUM({stat.Column}), 0) FROM {meta.Table}",
                    "avg" when !string.IsNullOrEmpty(stat.Column)
                        => $"SELECT COALESCE(AVG({stat.Column}), 0) FROM {meta.Table}",
                    "count"
                        => $"SELECT COUNT(*) FROM {meta.Table}",
                    _ => ""
                };
                if (string.IsNullOrEmpty(sql)) continue;

                if (!string.IsNullOrEmpty(stat.Filter))
                    sql += $" WHERE {stat.Filter}";

                var raw = conn.ExecuteScalar<object>(sql);
                var formatted = FormatScalar(raw, stat.Aggregate);

                var routeValues = new RouteValueDictionary
                {
                    ["project"] = projectName,
                    ["entity"]  = stat.Entity
                };
                foreach (var (k, v) in ParseFilterToUrlParams(stat.Filter))
                    routeValues[k] = v;

                result.Add(new DashboardStatViewModel
                {
                    Label     = stat.GetLabel(),
                    Value     = formatted,
                    Icon      = stat.Icon,
                    Color     = stat.Color,
                    EntityUrl = Url.Action("Index", "DynamicEntity", routeValues)
                });
            }
            catch (Exception ex)
            {
                // 検証/クエリ失敗: 警告ログを出力してエラープレースホルダーを追加
                _logger.LogWarning(ex,
                    "Dashboard stat failed: entity={Entity}, label={Label}, reason={Reason}",
                    stat.Entity, stat.GetLabel(),
                    ex is InvalidOperationException ? "設定検証エラー" : "SQL実行エラー");

                result.Add(new DashboardStatViewModel
                {
                    Label     = stat.GetLabel(),
                    Value     = "–",
                    Icon      = stat.Icon,
                    HasError  = true,
                    ErrorHint = ex is InvalidOperationException ? "設定エラー" : "クエリ失敗"
                });
            }
        }

        return Task.FromResult(result);
    }

    // ─── Charts ──────────────────────────────────────────────────────────────

    private Task<List<DashboardChartViewModel>> BuildChartsAsync(
        Models.DashboardConfig config)
    {
        var conn = _db is LazyDbConnection lazyConn ? lazyConn.InnerConnection : _db;
        var result = new List<DashboardChartViewModel>();

        if (conn.State != ConnectionState.Open)
        {
            conn.Open();
        }

        foreach (var chart in config.Charts)
        {
            if (!_meta.TryGet(chart.Entity, out var meta))
                continue;

            try
            {
                // SQL 安全検証: 集計カラム・JOIN識別子・式・フィルターを個別にホワイトリスト検証
                if (!string.IsNullOrEmpty(chart.ValueColumn))
                    SqlSafetyGuard.EnsureIdentifier(chart.ValueColumn, $"dashboard.charts[{chart.Entity}].valueColumn");

                // 集計式
                string valueExpr = chart.ValueAggregate.ToLowerInvariant() switch
                {
                    "sum" when !string.IsNullOrEmpty(chart.ValueColumn)
                        => $"SUM({chart.ValueColumn})",
                    "avg" when !string.IsNullOrEmpty(chart.ValueColumn)
                        => $"AVG({chart.ValueColumn})",
                    _ => "COUNT(*)"
                };

                string sql;
                string groupByClause;

                // out 変数を先に宣言して未代入エラーを回避
                Models.EntityDefinition? joinMeta = null;
                bool hasJoin = !string.IsNullOrEmpty(chart.LabelJoinEntity)
                               && _meta.TryGet(chart.LabelJoinEntity!, out joinMeta);

                if (hasJoin && joinMeta is not null)
                {
                    // JOIN して FK 先のラベルカラムを取得（識別子を個別に検証）
                    SqlSafetyGuard.EnsureIdentifier(chart.LabelJoinKey, $"dashboard.charts[{chart.Entity}].labelJoinKey");
                    var display = chart.LabelJoinDisplay ?? joinMeta.Key;
                    SqlSafetyGuard.EnsureIdentifier(display, $"dashboard.charts[{chart.Entity}].labelJoinDisplay");

                    sql = $"SELECT j.{display} as label, {valueExpr} as value " +
                          $"FROM {meta.Table} " +
                          $"JOIN {joinMeta.Table} j ON {meta.Table}.{chart.LabelJoinKey} = j.{joinMeta.Key}";
                    groupByClause = $"GROUP BY j.{display}";
                }
                else
                {
                    // GroupExpression（カラム名 or SQL 式）を検証してグルーピング
                    SqlSafetyGuard.EnsureExpression(chart.GroupExpression, $"dashboard.charts[{chart.Entity}].groupExpression");
                    var grp = chart.GroupExpression ?? "1";
                    sql           = $"SELECT {grp} as label, {valueExpr} as value FROM {meta.Table}";
                    groupByClause = $"GROUP BY {grp}";
                }

                // WHERE 句も式として安全検証
                SqlSafetyGuard.EnsureExpression(chart.Filter, $"dashboard.charts[{chart.Entity}].filter");
                if (!string.IsNullOrEmpty(chart.Filter))
                    sql += $" WHERE {chart.Filter}";

                sql += $" {groupByClause}";

                var orderCol = (chart.OrderBy?.ToLowerInvariant() == "label") ? "label" : "value";
                var orderDir = (chart.OrderDir?.ToLowerInvariant() == "asc")  ? "ASC"   : "DESC";
                sql += $" ORDER BY {orderCol} {orderDir} LIMIT {chart.Limit}";

                var rows   = conn.Query(sql);
                var labels = new List<string>();
                var values = new List<double>();

                foreach (IDictionary<string, object> row in rows)
                {
                    labels.Add(row.TryGetValue("label", out var lv) ? (lv?.ToString() ?? "") : "");
                    values.Add(row.TryGetValue("value", out var vv) ? Convert.ToDouble(vv ?? 0) : 0);
                }

                result.Add(new DashboardChartViewModel
                {
                    Title        = chart.GetTitle(),
                    Type         = chart.Type,
                    LabelsJson   = JsonSerializer.Serialize(labels, _jsonOpts),
                    ValuesJson   = JsonSerializer.Serialize(values, _jsonOpts),
                    ColorBg      = chart.ColorBg,
                    ColorBorder  = chart.ColorBorder,
                    ColorsJson   = chart.Colors != null
                                   ? JsonSerializer.Serialize(chart.Colors, _jsonOpts)
                                   : null
                });
            }
            catch (Exception ex)
            {
                // 検証/クエリ失敗: 警告ログを出力してエラープレースホルダーを追加
                _logger.LogWarning(ex,
                    "Dashboard chart failed: entity={Entity}, title={Title}, reason={Reason}",
                    chart.Entity, chart.GetTitle(),
                    ex is InvalidOperationException ? "設定検証エラー" : "SQL実行エラー");

                result.Add(new DashboardChartViewModel
                {
                    Title      = chart.GetTitle(),
                    Type       = chart.Type,
                    LabelsJson = "[]",
                    ValuesJson = "[]",
                    HasError   = true,
                    ErrorHint  = ex is InvalidOperationException ? "設定エラー" : "クエリ失敗"
                });
            }
        }

        return Task.FromResult(result);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// SQL フィルター式（例: "Status='in_progress' AND Priority='urgent'"）を
    /// エンティティ一覧 URL のクエリパラメータ辞書に変換します。
    /// Column='value' 形式の単純等値条件のみ対応します。
    /// </summary>
    private static Dictionary<string, string> ParseFilterToUrlParams(string? filter)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(filter)) return result;

        var parts = filter.Split(new[] { " AND ", " and " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var m = Regex.Match(part.Trim(), @"^(\w+)\s*=\s*'([^']*)'$");
            if (m.Success)
                result[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return result;
    }

    private static string FormatScalar(object? raw, string aggregate)
    {
        // sum / avg は小数2桁で表示
        if (aggregate.Equals("sum", StringComparison.OrdinalIgnoreCase) ||
            aggregate.Equals("avg", StringComparison.OrdinalIgnoreCase))
        {
            return raw switch
            {
                decimal d  => d.ToString("N2"),
                double  db => db.ToString("N2"),
                float   f  => f.ToString("N2"),
                _          => Convert.ToDecimal(raw ?? 0).ToString("N2")
            };
        }
        return raw?.ToString() ?? "0";
    }
}
