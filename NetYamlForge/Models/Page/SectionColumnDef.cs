

namespace NetYamlForge.Models;

// ── セクション列定義 ─────────────────────────────────────────────────────
/// <summary>
/// セクション列定義。entities の ColumnDefinition に相当。
/// YAML: columns: { col_name: { label: "表示名", type: string, hidden: false, ... } }
/// type の有効値: string | int | long | decimal | double | bool | date | datetime |
///               textarea | email | select | radio | color | money | rating | file
/// </summary>
public class SectionColumnDef : IColumnDef
{
    public string? Label { get; set; }
    public string? LabelKey { get; set; }
    public Dictionary<string, string>? LabelI18n { get; set; }
    public string Type { get; set; } = "string";
    public bool Hidden { get; set; }
    public bool Sortable { get; set; }
    public bool Searchable { get; set; }
    /// <summary>
    /// 選択肢。リスト形式（保存値のみ）または辞書形式（key=保存値/value=表示名）の両方を受け付ける。
    /// 辞書形式の場合は OptionLabels としても参照される。
    /// YAML: options: [a, b] または options: { a: "Label A", b: "Label B" }
    /// </summary>
    public Dictionary<string, string>? Options { get; set; }
    // IColumnDef.OptionLabels は Options にマップ
    Dictionary<string, string>? IColumnDef.OptionLabels => Options;
    /// <summary>entities.ColumnDefinition.ForeignKey に相当。</summary>
    public ForeignKeyDefinition? ForeignKey { get; set; }
    public List<ValidatorDefinition> Validators { get; set; } = new();
    public string GetLabel(string fallback) => I18nText.Resolve(LabelI18n, Label ?? fallback, LabelKey);
}

/// <summary>
/// セクション行アクション（ボタン）定義。テーブルの行アクション / form コンポーネントのボタン両用。
/// YAML: actions: [{ label: 詳細, url: "/path/{id}", class: "btn-outline-primary" }]
/// </summary>
public class SectionActionDefinition
{
    // ── 共通 ─────────────────────────────────────────────────────────────
    public string? Id { get; set; }
    /// <summary>ボタンラベル</summary>
    public string Label { get; set; } = "";
    /// <summary>submit | reset | link（未設定時は link として扱う）</summary>
    public string? Type { get; set; }
    /// <summary>DaisyUI スタイル: primary | ghost | secondary | danger</summary>
    public string? Style { get; set; }
    public string? Icon { get; set; }

    // ── テーブル行アクション専用 ─────────────────────────────────────────
    /// <summary>リンクURL。{column_name} プレースホルダーを行の値で置換可能。</summary>
    public string Url { get; set; } = "";
    /// <summary>CSSクラス（任意）。未設定時は "btn-outline" を使用。</summary>
    public string? Class { get; set; }
    public string? HxGet { get; set; }
    public string? HxTarget { get; set; }
    public string? HxSwap { get; set; }
    /// <summary>行アクション用確認メッセージ（YAML: confirm）</summary>
    public string? Confirm { get; set; }

    // ── form コンポーネントのアクション専用 ──────────────────────────────
    /// <summary>フォーム送信前の確認ダイアログメッセージ。{{field_id}} でフォーム値を参照可。</summary>
    public string? ConfirmMessage { get; set; }
    /// <summary>挿入先エンティティ名（テーブル名）</summary>
    public string? InsertEntity { get; set; }
    /// <summary>挿入フィールドマッピング。値に {{field_id}} テンプレートを使用可。</summary>
    public Dictionary<string, string> Fields { get; set; } = new();
    /// <summary>送信成功時のメッセージ</summary>
    public string? SuccessMessage { get; set; }
    /// <summary>送信成功後にリフレッシュするセクション ID</summary>
    public string? RefreshSection { get; set; }
}
