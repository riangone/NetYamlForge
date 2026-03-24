// ファイル概要: YAML 帳票テンプレート（pdf-templates/*.yaml）の C# モデル定義。
// C# は 5 つの汎用プリミティブ（line / paragraph / row / labelTable / dataTable）のみを定義し、
// 帳票固有のレイアウトはすべて YAML で組み合わせて定義します。

namespace NetYamlForge.Models;

/// <summary>帳票テンプレートのルートモデル</summary>
public class PdfTemplateConfig
{
    public string Name { get; set; } = "";
    /// <summary>ファイル名テンプレート。{date:yyyyMMdd} を使用可</summary>
    public string? FilenameTemplate { get; set; }
    public string PageSize { get; set; } = "A4";
    /// <summary>portrait | landscape</summary>
    public string Orientation { get; set; } = "portrait";
    /// <summary>余白 [top, right, bottom, left]（ポイント）</summary>
    public float[] Margins { get; set; } = [36f, 42f, 36f, 42f];
    public PdfThemeConfig Theme { get; set; } = new();
    public Dictionary<string, DataSourceConfig> DataSources { get; set; } = new();
    public List<PdfSectionConfig> Sections { get; set; } = [];
}

public class PdfThemeConfig
{
    public string PrimaryColor { get; set; } = "1c3658";
    public string LabelColor { get; set; } = "78b4cc";
    public string LabelTextColor { get; set; } = "ffffff";
    public string SubtleBackground { get; set; } = "f0f0f0";
    public string OddRowColor { get; set; } = "f8fcfe";
    public string BorderColor { get; set; } = "b4b4b4";
}

public class DataSourceConfig
{
    /// <summary>SQL クエリ。@FieldName 形式でヘッダーフィールドを参照できます</summary>
    public string Query { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
// PDF セクション（プリミティブ 5 種類）
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// PDF セクション定義。<br/>
/// type: <b>line</b> | <b>paragraph</b> | <b>row</b> | <b>labelTable</b> | <b>dataTable</b>
/// </summary>
public class PdfSectionConfig
{
    // ── 共通 ─────────────────────────────────────────────────────────────────
    /// <summary>line | paragraph | row | labelTable | dataTable</summary>
    public string Type { get; set; } = "";
    public float? MarginTop { get; set; }
    public float? MarginBottom { get; set; }

    // ── line ─────────────────────────────────────────────────────────────────
    /// <summary>線の太さ（ポイント）。デフォルト 0.5</summary>
    public float LineWeight { get; set; } = 0.5f;

    // ── paragraph ────────────────────────────────────────────────────────────
    /// <summary>静的テキスト</summary>
    public string? Text { get; set; }
    /// <summary>データフィールド参照（"dataSource.Field" または "Field"）</summary>
    public string? Field { get; set; }
    /// <summary>{Field} プレースホルダーを使ったテキストテンプレート</summary>
    public string? Template { get; set; }
    /// <summary>フィールド値の前に付加するテキスト</summary>
    public string? Prefix { get; set; }
    /// <summary>フィールド値の後に付加するテキスト</summary>
    public string? Suffix { get; set; }
    /// <summary>Suffix のフォントサイズ（省略時は FontSize と同じ）</summary>
    public float? SuffixFontSize { get; set; }
    public float FontSize { get; set; } = 9f;
    public bool Bold { get; set; } = false;
    /// <summary>テキスト色（hex 6桁 またはテーマキー: primary/label/labelText/subtle/border）</summary>
    public string? Color { get; set; }
    /// <summary>left | center | right</summary>
    public string? Align { get; set; }
    /// <summary>currency | quantity | text</summary>
    public string? Format { get; set; }

    // ── row ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// カラム幅比率（%）。例: [55, 45] / [30, 40, 30]<br/>
    /// 省略時はセル数で均等分割。
    /// </summary>
    public float[]? ColumnWidths { get; set; }
    /// <summary>
    /// セル定義のリスト。複数行にわたる場合は rowSpan/colSpan を使用し、
    /// iText がカラム数に応じて自動的に行を改めます。
    /// </summary>
    public List<PdfCellConfig> Cells { get; set; } = [];

    // ── labelTable ─────────────────────────────────────────────────────────
    // （labelTable はラベル列と値列を持つ 2 カラムテーブル）
    /// <summary>ラベルセルの背景色（テーマキーまたは hex）。デフォルト "label"</summary>
    public string LabelBackground { get; set; } = "label";
    /// <summary>ラベルセルのテキスト色（テーマキーまたは hex）。null で標準色</summary>
    public string? LabelTextColor { get; set; } = "labelText";
    /// <summary>セルのボーダー太さ（ポイント）。デフォルト 0.5</summary>
    public float BorderWeight { get; set; } = 0.5f;
    /// <summary>セルの内側パディング（ポイント）。デフォルト 4</summary>
    public float CellPadding { get; set; } = 4f;
    public List<LabelValueRowConfig> Rows { get; set; } = [];

    // ── dataTable ─────────────────────────────────────────────────────────
    public string? DataSource { get; set; }
    public int MinRows { get; set; } = 8;
    /// <summary>奇数行の背景色（テーマキーまたは hex）。デフォルト "oddRow"</summary>
    public string OddRowBackground { get; set; } = "oddRow";
    public List<DataColumnConfig> Columns { get; set; } = [];
}

// ─────────────────────────────────────────────────────────────────────────────
// row 内のセル定義
// ─────────────────────────────────────────────────────────────────────────────

public class PdfCellConfig
{
    /// <summary>背景色（テーマキーまたは hex）</summary>
    public string? Background { get; set; }
    /// <summary>
    /// セルボーダー太さ（ポイント）。<br/>
    /// null = ボーダーなし（row のデフォルト）、0 より大きい値 = ボーダーあり
    /// </summary>
    public float? BorderWeight { get; set; }
    public float? Padding { get; set; }
    public float? PaddingLeft { get; set; }
    public float? PaddingRight { get; set; }
    public float? PaddingTop { get; set; }
    public float? PaddingBottom { get; set; }
    /// <summary>top | middle | bottom</summary>
    public string? VAlign { get; set; }
    public float? MinHeight { get; set; }
    public int ColSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    /// <summary>セル内の要素（再帰的に paragraph / row / labelTable / dataTable を配置可）</summary>
    public List<PdfSectionConfig> Elements { get; set; } = [];
}

// ─────────────────────────────────────────────────────────────────────────────
// labelTable の行定義
// ─────────────────────────────────────────────────────────────────────────────

public class LabelValueRowConfig
{
    public string Label { get; set; } = "";
    /// <summary>ヘッダーフィールド名または "dataSource.Field"</summary>
    public string Field { get; set; } = "";
    /// <summary>currency | quantity | text</summary>
    public string? Format { get; set; }
    /// <summary>値が 0 の場合に行を省略する</summary>
    public bool OmitIfZero { get; set; } = false;
    /// <summary>値が空の場合に行を省略する</summary>
    public bool OmitIfEmpty { get; set; } = false;
    public bool Bold { get; set; } = false;
}

// ─────────────────────────────────────────────────────────────────────────────
// dataTable の列定義
// ─────────────────────────────────────────────────────────────────────────────

public class DataColumnConfig
{
    /// <summary>フィールド名。"_rowNumber" は自動連番</summary>
    public string Field { get; set; } = "";
    public string Label { get; set; } = "";
    public float Width { get; set; } = 10f;
    /// <summary>left | center | right</summary>
    public string Align { get; set; } = "left";
    /// <summary>currency | quantity | text</summary>
    public string? Format { get; set; }
}
