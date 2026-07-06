using System.Globalization;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

public class ForeignKeyDefinition
{
    public string Entity { get; set; } = default!;
    public string DisplayColumn { get; set; } = "Id";
    public List<string>? DisplayColumns { get; set; }
    /// <summary>
    /// 参照先データ取得のカスタムSQL。指定時はこのSQLをベースに候補を取得します。
    /// 期待される戻り値には `Id` 列を含めてください。
    /// </summary>
    public string? Query { get; set; }
    // ドロップダウンの代わりにピッカーモーダルで選択するか（単一選択）
    public bool Picker { get; set; }
    // ピッカーモーダルで複数選択するか
    public bool MultiPicker { get; set; }

    public IReadOnlyList<string> GetDisplayColumns()
    {
        if (DisplayColumns != null && DisplayColumns.Count > 0)
        {
            return DisplayColumns
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(DisplayColumn))
        {
            return DisplayColumn
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        return new[] { "Id" };
    }
}

public class JoinDefinition
{
    public string Type { get; set; } = "left";
    public string Table { get; set; } = default!;
    public string Alias { get; set; } = default!;
    public string? On { get; set; }
    // YAML 1.1 互換: `on:` キーが bool `true` として解釈されるケースを受ける
    public string? True { get; set; }

    public string? GetJoinCondition() =>
        !string.IsNullOrWhiteSpace(On) ? On : True;
}

public class EntityLinkDefinition
{
    public string Label { get; set; } = default!;
    public string? LabelKey { get; set; }
    /// <summary>多言語ラベル（entities.yml の labelI18n セクション）</summary>
    public Dictionary<string, string>? LabelI18n { get; set; }
    public string TargetEntity { get; set; } = default!;
    // 静的クエリパラメータ（例: sort=Name）
    public Dictionary<string, string>? Query { get; set; }
    // 行ごとの動的フィルタ: targetQueryParam → sourceRowColumn
    // 例: { "CustomerId": "CustomerId" } → 行の CustomerId 値を ?CustomerId=xxx として付与
    public Dictionary<string, string>? Filter { get; set; }

    public string GetLabel() => I18nText.Resolve(LabelI18n, Label, LabelKey);
}
