

namespace NetYamlForge.Models;

// ── セクションフォーム定義 ───────────────────────────────────────────────
/// <summary>
/// セクションフォームグループ（create / edit）。
/// YAML: forms: { create: { title: "新規", fields: [name, status] } }
/// </summary>
public class SectionFormGroupDef
{
    public string? Title { get; set; }
    public string? TitleKey { get; set; }
    public Dictionary<string, string>? TitleI18n { get; set; }
    public string? Description { get; set; }
    public string? DescriptionKey { get; set; }
    public Dictionary<string, string>? DescriptionI18n { get; set; }
    public List<string> Fields { get; set; } = new();

    /// <summary>未設定時は null（呼び出し側で汎用フォールバック文言を使う）。</summary>
    public string? GetTitle() =>
        string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(TitleKey) && (TitleI18n == null || TitleI18n.Count == 0)
            ? null
            : I18nText.Resolve(TitleI18n, Title ?? "", TitleKey);

    /// <summary>未設定時は null（呼び出し側で汎用フォールバック文言を使う）。</summary>
    public string? GetDescription() =>
        string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(DescriptionKey) && (DescriptionI18n == null || DescriptionI18n.Count == 0)
            ? null
            : I18nText.Resolve(DescriptionI18n, Description ?? "", DescriptionKey);
}

/// <summary>
/// フォームフィールド個別定義。forms の fieldDefs 配下。entities の FormDefinition に相当。
/// type の有効値: string | int | long | decimal | double | bool | date | datetime |
///               textarea | email | select | radio | color | money | rating | file
/// </summary>
public class SectionFormFieldDef
{
    public string? Label { get; set; }
    public string? LabelKey { get; set; }
    public Dictionary<string, string>? LabelI18n { get; set; }
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public bool Editable { get; set; } = true;
    /// <summary>
    /// 選択肢。リスト形式（保存値のみ）または辞書形式（key=保存値/value=表示名）の両方を受け付ける。
    /// YAML: options: [a, b] または options: { a: "Label A", b: "Label B" }
    /// </summary>
    public Dictionary<string, string>? Options { get; set; }
    public string? Placeholder { get; set; }
    /// <summary>entities.FormDefinition.ForeignKey に相当。</summary>
    public ForeignKeyDefinition? ForeignKey { get; set; }

    /// <summary>表示時のグリッド列スパン（デフォルト 1）。</summary>
    public int ColSpan { get; set; } = 1;

    public List<ValidatorDefinition> Validators { get; set; } = new();

    public string GetLabel(string fallback) => I18nText.Resolve(LabelI18n, Label ?? fallback, LabelKey);
}

/// <summary>form コンポーネントのフィールド定義</summary>
public class FormSectionFieldDef
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }
    /// <summary>string | bool | select | entity_select | textarea</summary>
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public string? Placeholder { get; set; }
    public string? Hint { get; set; }
    public string? Icon { get; set; }
    public string? Default { get; set; }
    /// <summary>select 型の選択肢リスト</summary>
    public List<ExtraFieldOptionDefinition>? Options { get; set; }
    /// <summary>entity_select 型: 参照エンティティ名（テーブル名）</summary>
    public string? Entity { get; set; }
    /// <summary>entity_select 型: 表示に使うカラム名</summary>
    public string? DisplayField { get; set; }
    /// <summary>entity_select 型: 保存値に使うカラム名</summary>
    public string? KeyField { get; set; }
}

public class ExtraFieldDefinition
{
    public string Id { get; set; } = "";
    public string? Label { get; set; }
    public string? Type { get; set; }
    public string? Default { get; set; }
    public List<ExtraFieldOptionDefinition>? Options { get; set; }
}
