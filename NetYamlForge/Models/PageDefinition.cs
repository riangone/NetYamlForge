// ファイル概要: カスタムページ機能のモデル定義。pages/*.yaml にマッピングされます。

namespace NetYamlForge.Models;

/// <summary>カスタムページ定義</summary>
public class PageDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>メインテーブル（外部キー結合の基準）</summary>
    public string? MainTable { get; set; }
    /// <summary>カスタム Razor テンプレート名（拡張子なし）。未設定時は汎用 PageView.cshtml を使用。</summary>
    public string? Template { get; set; }
    /// <summary>カレンダーUI向けの追加設定（pages/*.yaml の calendar_ui）。</summary>
    public CalendarUiDefinition? CalendarUi { get; set; }
    public List<SectionDefinition> Sections { get; set; } = new();
}

public class CalendarUiDefinition
{
    public int MobileMonthCount { get; set; } = 1;
    public int DesktopMinMonthCount { get; set; } = 2;
    public int DesktopMaxMonthCount { get; set; } = 6;
    public bool ShowJapanHolidays { get; set; } = true;
    /// <summary>JP祝日ソース: hybrid | api | builtin</summary>
    public string? JapanHolidayProvider { get; set; }
    /// <summary>外部JP祝日API URLテンプレート（{year} を含む）</summary>
    public string? JapanHolidayApiUrlTemplate { get; set; }
    public int? JapanHolidayApiTimeoutMs { get; set; }
    public bool ShowChineseLunar { get; set; } = true;
    public List<CalendarHolidayDefinition> ThirdPartyHolidays { get; set; } = new();
    public List<CalendarHolidayDefinition> CustomHolidays { get; set; } = new();
}

public class CalendarHolidayDefinition
{
    public string Date { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Source { get; set; }
}

// ── セクション確認ダイアログ定義 ──────────────────────────────────────────
/// <summary>
/// セクション CRUD 確認ダイアログメッセージ。entities の ConfirmationDefinition に相当。
/// YAML: confirmation: { create: "本当に作成しますか？", update: "更新します", delete: "削除しますか？" }
/// </summary>
public class SectionConfirmationDef
{
    public string? Create { get; set; }
    public string? Update { get; set; }
    public string? Delete { get; set; }
}

// ── セクションフック定義 ─────────────────────────────────────────────────
/// <summary>
/// セクション CRUD フック定義。entities の EntityHooksDefinition に相当。
/// YAML キーは camelCase（beforeCreate / afterCreate 等）を使用します。
/// snake_case（before_create 等）も受け付けます（SectionHooksConverter が処理）。
/// フック名は EntityHookRegistry に登録された IEntityHook.Name と一致する必要があります。
/// </summary>
public class SectionHooksDefinition
{
    public List<string> BeforeCreate { get; set; } = new();
    public List<string> AfterCreate  { get; set; } = new();
    public List<string> BeforeUpdate { get; set; } = new();
    public List<string> AfterUpdate  { get; set; } = new();
    public List<string> BeforeDelete { get; set; } = new();
    public List<string> AfterDelete  { get; set; } = new();
}

// ── セクション列定義 ─────────────────────────────────────────────────────
/// <summary>
/// セクション列定義。entities の ColumnDefinition に相当。
/// YAML: columns: { col_name: { label: "表示名", type: string, hidden: false, ... } }
/// </summary>
public class SectionColumnDef : IColumnDef
{
    public string? Label { get; set; }
    /// <summary>string | int | decimal | bool | date | datetime | select</summary>
    public string Type { get; set; } = "string";
    public bool Hidden { get; set; }
    public bool Sortable { get; set; }
    public bool Searchable { get; set; }
    /// <summary>type=select のときの選択肢。key=保存値/value=表示名。IColumnDef.OptionLabels として公開。</summary>
    public Dictionary<string, string>? Options { get; set; }
    // IColumnDef.OptionLabels は Options にマップ
    Dictionary<string, string>? IColumnDef.OptionLabels => Options;
    public string GetLabel(string fallback) => Label ?? fallback;
}

// ── セクションフォーム定義 ───────────────────────────────────────────────
/// <summary>
/// セクションフォームグループ（create / edit）。
/// YAML: forms: { create: { title: "新規", fields: [name, status] } }
/// </summary>
public class SectionFormGroupDef
{
    public string? Title { get; set; }
    public List<string> Fields { get; set; } = new();
}

/// <summary>フォームフィールド個別定義。forms.field_defs 配下。</summary>
public class SectionFormFieldDef
{
    public string? Label { get; set; }
    /// <summary>string | int | decimal | bool | date | datetime | select | textarea</summary>
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    /// <summary>type=select のときの選択肢。key: 表示値</summary>
    public Dictionary<string, string>? Options { get; set; }
    public string? Placeholder { get; set; }
    public string GetLabel(string fallback) => Label ?? fallback;
}

// ── セクションページング定義 ──────────────────────────────────────────────
/// <summary>
/// ページング設定。entities の PagingDefinition に相当。
/// YAML: paging: { page_size: 20, mode: numbered, enable_count: true }
/// </summary>
public class SectionPagingDef
{
    public int PageSize { get; set; } = 10;
    /// <summary>numbered | keyset</summary>
    public string Mode { get; set; } = "numbered";
    public bool EnableCount { get; set; } = true;
}

// ── ページフィルター定義（拡張版）────────────────────────────────────────
/// <summary>
/// セクションフィルター定義。entities の FilterDefinition に相当。
/// YAML: filters: { col: { label: "ラベル", type: like, options: { k: v } } }
/// </summary>
public class PageFilterDefinition
{
    public string? Label { get; set; }
    /// <summary>like | eq | select | dropdown | toggle_group | bool_toggle | date_range | range | gte | lte</summary>
    public string Type { get; set; } = "like";
    public Dictionary<string, string>? Options { get; set; }
    public string GetLabel(string fallback) => Label ?? fallback;
}

/// <summary>ページのセクション（データグリッド）定義</summary>
public class SectionDefinition
{
    public string Id { get; set; } = "";
    public string? Title { get; set; }
    /// <summary>ソース種別: table | custom</summary>
    public string SourceType { get; set; } = "table";
    /// <summary>テーブル名 (table) または SQL クエリ (custom)</summary>
    public string? Source { get; set; }

    // ── 列定義 ──────────────────────────────────────────────────────
    /// <summary>
    /// 列定義。リスト形式 [id, name] または辞書形式 {id: {label: ID}, name: {label: 名前}} を受け付ける。
    /// YamlDotNet の SectionColumnsConverter が両形式を処理する。
    /// </summary>
    public Dictionary<string, SectionColumnDef> Columns { get; set; } = new();

    // ── フィルター定義 ───────────────────────────────────────────────
    public Dictionary<string, PageFilterDefinition>? Filters { get; set; }

    // ── フック定義 ───────────────────────────────────────────────────
    /// <summary>
    /// CRUD フック定義。entities の hooks に相当。
    /// YAML: hooks: { before_create: [trim], after_create: [audit_log] }
    /// </summary>
    public SectionHooksDefinition? Hooks { get; set; }

    // ── フォーム定義 ─────────────────────────────────────────────────
    /// <summary>
    /// フォームグループ定義。entities の forms に相当。
    /// キー: create | edit | update 。"edit" と "update" は相互エイリアス。
    /// 未設定時は UpdatableFields または Columns.Keys を使用。
    /// </summary>
    public Dictionary<string, SectionFormGroupDef>? Forms { get; set; }

    /// <summary>フォームフィールドの個別メタデータ。forms.field_defs 配下。</summary>
    public Dictionary<string, SectionFormFieldDef>? FieldDefs { get; set; }

    // ── ページング定義 ───────────────────────────────────────────────
    /// <summary>
    /// ページング設定。設定時は page_size を上書きする。
    /// YAML: paging: { page_size: 20, mode: numbered }
    /// </summary>
    public SectionPagingDef? Paging { get; set; }

    // ── 確認ダイアログ定義 ───────────────────────────────────────────
    /// <summary>
    /// CRUD 確認ダイアログメッセージ。entities の confirmation に相当。
    /// YAML: confirmation: { create: "...", update: "...", delete: "..." }
    /// </summary>
    public SectionConfirmationDef? Confirmation { get; set; }

    // ── コンポーネントサイズ ─────────────────────────────────────────
    /// <summary>DaisyUI サイズ修飾子（xs / sm / md / lg）。デフォルト: sm</summary>
    public string Size { get; set; } = "sm";

    // ── 後方互換フラット設定 ─────────────────────────────────────────
    public bool Editable { get; set; } = false;
    public bool ReadOnly { get; set; } = false;
    /// <summary>page_size: 整数での直接指定（Paging.PageSize が優先）</summary>
    public int PageSize { get; set; } = 10;
    /// <summary>親セクションとの結合キー（子セクション側のカラム名）</summary>
    public string? ForeignKey { get; set; }
    /// <summary>親の値を参照する本セクションのカラム名</summary>
    public string? LocalForeignKey { get; set; }
    /// <summary>行レベル更新/削除のターゲットテーブル名</summary>
    public string? TargetTable { get; set; }
    /// <summary>行レベル更新/削除で使用する主キーカラム名</summary>
    public string? TargetPrimaryKey { get; set; }
    /// <summary>update-row で更新を許可するカラム名ホワイトリスト。未設定時は Forms または Columns を使用。</summary>
    public List<string>? UpdatableFields { get; set; }

    // ── ヘルパーメソッド ─────────────────────────────────────────────
    /// <summary>有効なページサイズ（Paging.PageSize > PageSize の優先順）</summary>
    public int GetEffectivePageSize() => Paging?.PageSize ?? PageSize;

    /// <summary>N番目の列名を取得（見つからない場合は空文字）</summary>
    public string GetColumnAt(int index) =>
        Columns.Count > index ? Columns.Keys.ElementAt(index) : "";

    /// <summary>表示列名リスト（Hidden=false の列のみ）</summary>
    public IReadOnlyList<string> GetVisibleColumnNames() =>
        Columns.Where(c => !c.Value.Hidden).Select(c => c.Key).ToList();

    /// <summary>
    /// フォームで表示するフィールド名リスト。
    /// 優先順: forms[mode].Fields > UpdatableFields > Columns.Keys（PK除外）
    /// "edit" と "update" は相互エイリアス（どちらで定義しても動作する）。
    /// </summary>
    public IReadOnlyList<string> GetFormFields(string mode = "edit")
    {
        if (Forms != null)
        {
            // "edit" と "update" を相互エイリアスとして扱う
            var aliases = mode switch
            {
                "edit"   => new[] { "edit", "update" },
                "update" => new[] { "update", "edit" },
                _        => new[] { mode }
            };
            foreach (var alias in aliases)
            {
                if (Forms.TryGetValue(alias, out var form) && form.Fields.Count > 0)
                    return form.Fields;
            }
        }

        var cols = UpdatableFields ?? Columns.Keys.ToList();
        return cols
            .Where(f => !string.Equals(f, TargetPrimaryKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>フォームフィールドの個別定義を取得（ColumnDef にフォールバック）</summary>
    public SectionFormFieldDef GetFieldDef(string fieldName)
    {
        if (FieldDefs != null && FieldDefs.TryGetValue(fieldName, out var def))
            return def;

        // ColumnDef から type / label / options をフォールバック
        if (Columns.TryGetValue(fieldName, out var colDef))
        {
            return new SectionFormFieldDef
            {
                Label = colDef.Label,
                Type = colDef.Type,
                Options = colDef.Options
            };
        }

        return new SectionFormFieldDef();
    }

    // ── コンポーネント種別 ──────────────────────────────────────────
    public string Component { get; set; } = "table";

    // ── グラフ系共通 ────────────────────────────────────────────────
    public string? XField { get; set; }
    public string? YField { get; set; }
    public string? Color { get; set; }

    // ── stat_cards ──────────────────────────────────────────────────
    public string? ValueField { get; set; }
    public string? LabelField { get; set; }
    public string? IconField { get; set; }
    public string? Icon { get; set; }
    public string? DeltaField { get; set; }
    public string? SubValueField { get; set; }

    // ── kanban ──────────────────────────────────────────────────────
    public string? GroupBy { get; set; }
    public string? CardTitle { get; set; }
    public string? CardSubtitle { get; set; }

    // ── timeline ────────────────────────────────────────────────────
    public string? DateField { get; set; }
    public string? EventField { get; set; }
    public string? DescField { get; set; }

    // ── progress_bars ───────────────────────────────────────────────
    public string? ProgressField { get; set; }
    public string? MaxField { get; set; }

    // ── card_list / accordion / leaderboard ─────────────────────────
    public string? TitleField { get; set; }
    public string? SubtitleField { get; set; }
    public string? DescriptionField { get; set; }
    public string? ImageField { get; set; }
    public string? ActionUrl { get; set; }
    public string? ContentField { get; set; }
    public bool? ExpandedByDefault { get; set; }
    public string? NameField { get; set; }
    public string? AvatarField { get; set; }
    public string? TrendField { get; set; }
    public string? ParentField { get; set; }
    public string? EmailField { get; set; }
    public string? TypeField { get; set; }
    public string? UserField { get; set; }
    public string? CurrentField { get; set; }
    public string? TargetField { get; set; }
    public string? UnitField { get; set; }
    public string? DeadlineField { get; set; }
    public string? UploadUrl { get; set; }
    public int? MaxFileSize { get; set; }
    public string? AllowedTypes { get; set; }
    public bool? Multiple { get; set; }
    public string? StartDateField { get; set; }
    public string? EndDateField { get; set; }
    public string? ColorField { get; set; }
}

/// <summary>PageView からコンポーネント部分ビューへ渡すレンダリングモデル</summary>
public class SectionRenderModel
{
    public SectionDefinition Sec { get; set; } = new();
    public List<Dictionary<string, object>> Rows { get; set; } = new();
    public int Total { get; set; }
    public string Project { get; set; } = "";
    public string PageName { get; set; } = "";
    /// <summary>
    /// フィルター/ソート/ページパラメータ（全セクション共有）。
    /// HTMX部分更新時にコントローラーが HX-Current-URL から復元して渡す。
    /// null の場合は _SectionTable.cshtml が Request.Query を使用する。
    /// </summary>
    public IDictionary<string, string>? AllQueryParams { get; set; }
}

/// <summary>セクション行フォーム（新規/編集モーダル）用モデル</summary>
public class SectionRowFormModel
{
    public SectionDefinition Sec { get; set; } = new();
    /// <summary>null = 新規作成、非null = 編集</summary>
    public Dictionary<string, object>? Row { get; set; }
    public string Project { get; set; } = "";
    public string PageName { get; set; } = "";
    /// <summary>フック/バリデーションエラー時のメッセージ。非nullの場合にフォーム上部に表示。</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>エラー時にフォームフィールド値を保持するための送信済み値。</summary>
    public Dictionary<string, string?>? SubmittedValues { get; set; }
}
