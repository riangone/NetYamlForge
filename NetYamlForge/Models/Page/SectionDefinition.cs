

namespace NetYamlForge.Models;

/// <summary>ページのセクション（データグリッド）定義</summary>
public class SectionDefinition
{
    public string Id { get; set; } = "";
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// このセクションを表示するロールのホワイトリスト。
    /// 未設定（null/空）なら全ユーザーに表示。管理者は常に表示。
    /// YAML: visibleToRoles: [sales_rep, manager]
    /// </summary>
    public List<string>? VisibleToRoles { get; set; }
    /// <summary>true のとき、このセクションを画面・データ取得から除外する。YAML: hidden: true</summary>
    public bool Hidden { get; set; } = false;
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
    /// ページング設定。entities の PagingDefinition と共通クラスを使用。
    /// 未設定時は PageSize フィールド（デフォルト 10）にフォールバック。
    /// YAML: paging: { page_size: 20, mode: numbered }
    /// </summary>
    public PagingDefinition? Paging { get; set; }

    // ── 確認ダイアログ定義 ───────────────────────────────────────────
    /// <summary>
    /// CRUD 確認ダイアログメッセージ。entities の confirmation と共通クラスを使用。
    /// YAML: confirmation: { create: "...", update: "...", delete: "..." }
    /// </summary>
    public ConfirmationDefinition? Confirmation { get; set; }

    // ── コンポーネントサイズ ─────────────────────────────────────────
    /// <summary>CSS クラス名。HTML class 属性に適用。</summary>
    public string? Class { get; set; }
    /// <summary>DaisyUI サイズ修飾子（xs / sm / md / lg）。デフォルト: sm</summary>
    public string Size { get; set; } = "sm";

    // ── デフォルトソート ─────────────────────────────────────────────
    /// <summary>URL パラメータ未指定時のデフォルトソート列名。未設定時は最初の列。</summary>
    public string? DefaultSort { get; set; }
    /// <summary>デフォルトソート方向 (asc / desc)。未設定時は asc。</summary>
    public string? DefaultSortDir { get; set; }

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

    // ── アクション定義 ─────────────────────────────────────────────────
    /// <summary>
    /// 行レベルアクション（ボタン）定義。
    /// YAML: actions: [{ label: 詳細, url: "/path/{id}", class: "btn-outline-primary" }]
    /// </summary>
    public List<SectionActionDefinition>? Actions { get; set; }

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
                Options = colDef.Options != null ? new Dictionary<string, string>(colDef.Options) : null,
                Validators = colDef.Validators
            };
        }

        return new SectionFormFieldDef();
    }

    // ── コンポーネント種別 ──────────────────────────────────────────
    public string Component { get; set; } = "table";

    // ── form コンポーネント ─────────────────────────────────────────
    /// <summary>form レイアウト: vertical | horizontal</summary>
    public string? Layout { get; set; }
    /// <summary>form コンポーネントのフィールド定義リスト（YAML: fields）</summary>
    public List<FormSectionFieldDef>? Fields { get; set; }

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

    // ── table コンポーネント ────────────────────────────────────────
    /// <summary>秒単位の自動リフレッシュ間隔（0 または未設定で無効）</summary>
    public int AutoRefresh { get; set; }
    /// <summary>データが空のときに表示するメッセージ（未設定時は i18n "No data available"）</summary>
    public string? EmptyMessage { get; set; }
    /// <summary>行の条件付き色付けルール（YAML: rowColorRules）</summary>
    public List<RowColorRule>? RowColorRules { get; set; }

    // ── card_list / accordion / leaderboard ─────────────────────────
    /// <summary>accordion / card_list 用の静的アイテムリスト（sourceType: none のとき使用）</summary>
    public List<AccordionItemDef>? Items { get; set; }
    public string? TitleField { get; set; }
    public string? SubtitleField { get; set; }
    public string? DescriptionField { get; set; }
    public string? ImageField { get; set; }
    public string? CaptionField { get; set; }
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

    // ── file_upload ─────────────────────────────────────────────────────────
    public string? DropzoneLabel { get; set; }
    public string? DropzoneHint { get; set; }
    public bool? ShowPreview { get; set; }
    public int? PreviewMaxThumbnails { get; set; }
    public int? MaxFiles { get; set; }
    public string? UploadDir { get; set; }
    public SectionFileUploadOnCompleteDefinition? OnUploadComplete { get; set; }
    public List<ExtraFieldDefinition>? ExtraFields { get; set; }
}

public class ExtraFieldOptionDefinition
{
    public string? Value { get; set; }
    public string? Label { get; set; }
}

/// <summary>accordion / card_list 用の静的アイテム定義</summary>
public class AccordionItemDef
{
    public string Title { get; set; } = "";
    public string? Content { get; set; }
}

/// <summary>table の行条件色付けルール（YAML: rowColorRules）</summary>
public class RowColorRule
{
    /// <summary>SQLite CASE 風の条件式（列名 LIKE '%値%' 等）</summary>
    public string Condition { get; set; } = "";
    /// <summary>CSS クラス名（row-danger / row-info / row-success 等）</summary>
    public string ColorClass { get; set; } = "";
}

/// <summary>file_upload セクションの onUploadComplete 設定</summary>
public class SectionFileUploadOnCompleteDefinition
{
    public string? InsertEntity { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new();
    public string? ThenInsertEntity { get; set; }
    public Dictionary<string, string> ThenFields { get; set; } = new();
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
