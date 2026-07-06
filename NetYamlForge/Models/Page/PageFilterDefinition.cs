

namespace NetYamlForge.Models;

// ── ページフィルター定義（拡張版）────────────────────────────────────────
/// <summary>
/// セクションフィルター定義。entities の FilterDefinition に相当。
/// YAML: filters: { col: { label: "ラベル", type: like, options: { k: v } } }
/// type の有効値 (entities.FilterDefinition と統一):
///   like | eq | dropdown | date-range | range | toggle-group | bool-toggle |
///   multi-select | checkbox | entity-picker | entity-multi-picker | gte | lte
/// </summary>
public class PageFilterDefinition
{
    public string? Label { get; set; }
    public string? LabelKey { get; set; }
    public Dictionary<string, string>? LabelI18n { get; set; }
    public string Type { get; set; } = "like";
    public Dictionary<string, string>? Options { get; set; }
    public string GetLabel(string fallback) => I18nText.Resolve(LabelI18n, Label ?? fallback, LabelKey);
}
