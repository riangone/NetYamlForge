// ファイル概要: Dashboard に表示する統計情報・グラフの設定モデルです。
// config/dashboard.yml に対応する C# クラス定義。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

namespace NetYamlForge.Models;

public class DashboardConfig
{
    public List<DashboardStatDefinition> Stats { get; set; } = new();
    public List<DashboardChartDefinition> Charts { get; set; } = new();
}

// ────────────────────────────────────────────────────────────
// 統計カード定義
// ────────────────────────────────────────────────────────────
public class DashboardStatDefinition
{
    /// <summary>表示ラベル（デフォルト言語）</summary>
    public string Label { get; set; } = "";
    public string? LabelKey { get; set; }

    /// <summary>ロケール別ラベル（例: en-US / zh-CN / ja-JP）</summary>
    public Dictionary<string, string> LabelI18n { get; set; } = new();

    /// <summary>集計対象エンティティ名（entities.yml のキーと一致）</summary>
    public string Entity { get; set; } = "";

    /// <summary>集計種別: count / sum / avg</summary>
    public string Aggregate { get; set; } = "count";

    /// <summary>sum / avg の場合に集計するカラム名</summary>
    public string? Column { get; set; }

    /// <summary>WHERE 句（任意）。例: "IsDeleted = 0"</summary>
    public string? Filter { get; set; }

    /// <summary>アイコン文字（絵文字など）</summary>
    public string? Icon { get; set; }

    /// <summary>DaisyUI バッジカラークラス（例: badge-primary）</summary>
    public string? Color { get; set; }

    /// <summary>現在のロケールに対応するラベルを返します。</summary>
    public string GetLabel()
    {
        return I18nText.Resolve(LabelI18n, Label, LabelKey);
    }
}

// ────────────────────────────────────────────────────────────
// グラフ定義
// ────────────────────────────────────────────────────────────
public class DashboardChartDefinition
{
    /// <summary>グラフタイトル（デフォルト言語）</summary>
    public string Title { get; set; } = "";
    public string? TitleKey { get; set; }

    /// <summary>ロケール別タイトル</summary>
    public Dictionary<string, string> TitleI18n { get; set; } = new();

    /// <summary>Chart.js グラフ種別: bar / line / doughnut / pie</summary>
    public string Type { get; set; } = "bar";

    /// <summary>集計対象エンティティ名（entities.yml のキーと一致）</summary>
    public string Entity { get; set; } = "";

    /// <summary>集計種別: count / sum / avg</summary>
    public string ValueAggregate { get; set; } = "count";

    /// <summary>sum / avg の場合に集計するカラム名</summary>
    public string? ValueColumn { get; set; }

    /// <summary>
    /// GROUP BY に使う SQL 式。カラム名でも関数でも可。
    /// 例: "BillingCountry" / "strftime('%Y-%m', InvoiceDate)"
    /// labelJoinEntity を使う場合は不要。
    /// </summary>
    public string? GroupExpression { get; set; }

    // ── FK 経由でラベルを取得する場合（JOIN）──────────────────
    /// <summary>JOIN 先エンティティキー（entities.yml のキー）</summary>
    public string? LabelJoinEntity { get; set; }

    /// <summary>現在エンティティが持つ FK カラム名</summary>
    public string? LabelJoinKey { get; set; }

    /// <summary>JOIN 先エンティティで表示するカラム名</summary>
    public string? LabelJoinDisplay { get; set; }

    // ── 並び替え・件数 ────────────────────────────────────────
    /// <summary>ソート対象: "label" / "value"（既定: value）</summary>
    public string? OrderBy { get; set; }

    /// <summary>ソート方向: "asc" / "desc"（既定: desc）</summary>
    public string? OrderDir { get; set; }

    /// <summary>取得件数上限（既定: 10）</summary>
    public int Limit { get; set; } = 10;

    /// <summary>WHERE 句（任意）</summary>
    public string? Filter { get; set; }

    // ── 色設定 ───────────────────────────────────────────────
    /// <summary>背景色（単色: "rgba(99,102,241,0.2)"）</summary>
    public string? ColorBg { get; set; }

    /// <summary>枠線色（単色: "rgba(99,102,241,1)"）</summary>
    public string? ColorBorder { get; set; }

    /// <summary>doughnut / pie 用の複数色リスト</summary>
    public List<string>? Colors { get; set; }

    /// <summary>現在のロケールに対応するタイトルを返します。</summary>
    public string GetTitle()
    {
        return I18nText.Resolve(TitleI18n, Title, TitleKey);
    }
}
