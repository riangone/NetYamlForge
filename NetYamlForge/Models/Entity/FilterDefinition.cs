using System.Globalization;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

public class FilterDefinition
{
    public string Type { get; set; } = "dropdown";
    public string? Label { get; set; }
    public string? LabelKey { get; set; }
    public Dictionary<string, string>? LabelI18n { get; set; }
    public string? Expression { get; set; }
    public ForeignKeyDefinition? ForeignKey { get; set; }
    public List<string>? Options { get; set; }
    /// <summary>key=保存値/value=表示名 の選択肢辞書。Options (List) より優先。
    /// PageFilterDefinition.Options (Dict) を _FilterControl で使用する際に設定する。</summary>
    public Dictionary<string, string>? OptionLabels { get; set; }

    public string GetLabel(string fallback) => I18nText.Resolve(LabelI18n, Label ?? fallback, LabelKey);
}

public class FilterLayoutDefinition
{
    public int Columns { get; set; } = 4;
    public List<string> Order { get; set; } = new();
}
