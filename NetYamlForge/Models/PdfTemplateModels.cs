// ファイル概要: YAML 帳票テンプレート（pdf-templates/*.yaml）の C# モデル定義。
// C# 側にレイアウト情報を持たず、すべての帳票固有情報は YAML で定義します。

namespace NetYamlForge.Models;

/// <summary>帳票テンプレートのルートモデル</summary>
public class PdfTemplateConfig
{
    public string Name { get; set; } = "";
    /// <summary>ファイル名テンプレート。{date:yyyyMMdd} を使用可</summary>
    public string? FilenameTemplate { get; set; }
    /// <summary>ページサイズ: A4 | A3 | Letter | Legal</summary>
    public string PageSize { get; set; } = "A4";
    /// <summary>portrait | landscape</summary>
    public string Orientation { get; set; } = "portrait";
    /// <summary>余白 [top, right, bottom, left]（ポイント）省略時は [36,42,36,42]</summary>
    public float[] Margins { get; set; } = [36f, 42f, 36f, 42f];
    public PdfThemeConfig Theme { get; set; } = new();
    /// <summary>追加データソース（items, customer など）。@FieldName でヘッダー値を参照</summary>
    public Dictionary<string, DataSourceConfig> DataSources { get; set; } = new();
    /// <summary>ページセクション（上から順に描画）</summary>
    public List<PdfSectionConfig> Sections { get; set; } = [];
}

public class PdfThemeConfig
{
    /// <summary>メインカラー（タイトル・区切り線）hex 6桁</summary>
    public string PrimaryColor { get; set; } = "1c3658";
    /// <summary>ラベル背景色 hex 6桁</summary>
    public string LabelColor { get; set; } = "78b4cc";
    /// <summary>ラベルテキスト色 hex 6桁</summary>
    public string LabelTextColor { get; set; } = "ffffff";
    /// <summary>淡い背景色（番号ボックスなど）hex 6桁</summary>
    public string SubtleBackground { get; set; } = "f0f0f0";
    /// <summary>明細奇数行背景色 hex 6桁</summary>
    public string OddRowColor { get; set; } = "f8fcfe";
    /// <summary>罫線色 hex 6桁</summary>
    public string BorderColor { get; set; } = "b4b4b4";
}

public class DataSourceConfig
{
    /// <summary>SQL クエリ。@FieldName 形式でヘッダーフィールドを参照できます</summary>
    public string Query { get; set; } = "";
}

/// <summary>
/// PDF セクション定義。type フィールドで種別を指定します。<br/>
/// 種別: documentHeader | separator | recipientBlock | infoWithSender |
///        totalBanner | itemsTable | taxSummary | remarksBox |
///        contractParties | contractInfoTable | contractSignatures
/// </summary>
public class PdfSectionConfig
{
    public string Type { get; set; } = "";

    // ── レイアウト設定（全セクション共通） ───────────────────────────────────
    /// <summary>
    /// セクションのレイアウト設定。カラム幅・マージン・フォントサイズなどを YAML で上書きできます。
    /// </summary>
    public PdfLayoutConfig Layout { get; set; } = new();

    // ── documentHeader ──────────────────────────────────────────────────────
    public string? Title { get; set; }
    public float TitleFontSize { get; set; } = 20f;
    public string? NumberField { get; set; }
    public string? NumberLabel { get; set; }
    public string? DateField { get; set; }
    public string? DateLabel { get; set; }

    // ── separator ───────────────────────────────────────────────────────────
    /// <summary>thick | double | thin</summary>
    public string Style { get; set; } = "thin";

    // ── recipientBlock ──────────────────────────────────────────────────────
    /// <summary>宛名フィールド参照。"dataSource.Field" または "HeaderField"</summary>
    public string? NameSource { get; set; }
    /// <summary>宛名の後に付ける敬称（例: "　御中"）</summary>
    public string? Suffix { get; set; }
    public float NameFontSize { get; set; } = 13f;
    public string? IntroText { get; set; }

    // ── infoWithSender ──────────────────────────────────────────────────────
    public List<InfoRowConfig> InfoRows { get; set; } = [];
    public List<SenderFieldConfig> SenderFields { get; set; } = [];

    // ── totalBanner ─────────────────────────────────────────────────────────
    /// <summary>金額フィールド名（ヘッダーから参照）</summary>
    public string? Field { get; set; }
    public string? Label { get; set; }
    public string? BannerSuffix { get; set; }

    // ── itemsTable ──────────────────────────────────────────────────────────
    public string? DataSource { get; set; }
    public int MinRows { get; set; } = 8;
    public List<ItemColumnConfig> Columns { get; set; } = [];

    // ── taxSummary ──────────────────────────────────────────────────────────
    public List<TaxRowConfig> TaxRows { get; set; } = [];

    // ── remarksBox ──────────────────────────────────────────────────────────
    /// <summary>{FieldName} または {dataSource.Field} プレースホルダー使用可</summary>
    public string? RemarksTemplate { get; set; }
    public float MinHeight { get; set; } = 50f;

    // ── contractParties（契約書の甲乙宛名） ─────────────────────────────────
    /// <summary>甲の名称ソース（"dataSource.Field" または "Field"）</summary>
    public string? PartyASource { get; set; }
    public string? PartyALabel { get; set; }
    public string? PartyBField { get; set; }
    public string? PartyBLabel { get; set; }
    /// <summary>前文テンプレート。{Field} プレースホルダー使用可</summary>
    public string? BodyTemplate { get; set; }

    // ── contractSignatures（署名欄） ─────────────────────────────────────────
    public List<SignatoryConfig> Signatories { get; set; } = [];
}

public class InfoRowConfig
{
    public string Label { get; set; } = "";
    /// <summary>ヘッダーフィールド名。OmitIfEmpty=true のとき空なら行を省略</summary>
    public string Field { get; set; } = "";
    public bool OmitIfEmpty { get; set; } = false;
}

public class SenderFieldConfig
{
    public string Field { get; set; } = "";
    public string? Prefix { get; set; }
    public float FontSize { get; set; } = 8.5f;
}

public class ItemColumnConfig
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

public class TaxRowConfig
{
    public string Label { get; set; } = "";
    public string Field { get; set; } = "";
    public bool OmitIfZero { get; set; } = false;
    public bool Bold { get; set; } = false;
}

public class SignatoryConfig
{
    public string Role { get; set; } = "";
    /// <summary>会社名・相手名（"dataSource.Field" または "Field"）</summary>
    public string? NameSource { get; set; }
    public string? SignatoryField { get; set; }
    /// <summary>SignatoryField が空のとき使う固定テキスト</summary>
    public string? SignatoryFallback { get; set; }
}

/// <summary>
/// セクションのレイアウト設定。C# のデフォルト値を YAML で上書きします。<br/>
/// すべてのプロパティはオプションで、省略するとデフォルト値が使われます。
/// </summary>
public class PdfLayoutConfig
{
    // ── カラム幅 %（セクション主レイアウト） ──────────────────────────────────
    /// <summary>
    /// メインカラムの幅比率（%）。<br/>
    /// 2カラム例: [55, 45]、3カラム例: [30, 40, 30]
    /// </summary>
    public float[]? Columns { get; set; }

    /// <summary>
    /// 内側ネストテーブルのカラム幅比率（%）。<br/>
    /// documentHeader の番号ボックス、infoWithSender の情報テーブルなどに使用します。
    /// </summary>
    public float[]? InnerColumns { get; set; }

    // ── 間隔 ──────────────────────────────────────────────────────────────────
    /// <summary>セクション上部のマージン（ポイント）</summary>
    public float? MarginTop { get; set; }

    /// <summary>セクション下部のマージン（ポイント）</summary>
    public float? MarginBottom { get; set; }

    // ── テーブルスタイル ──────────────────────────────────────────────────────
    /// <summary>テーブルセルの内側パディング（ポイント）</summary>
    public float? CellPadding { get; set; }

    // ── フォントサイズ ────────────────────────────────────────────────────────
    /// <summary>セクション内の基本テキストフォントサイズ（ポイント）</summary>
    public float? FontSize { get; set; }

    /// <summary>強調テキスト（合計金額など）のフォントサイズ（ポイント）</summary>
    public float? AccentFontSize { get; set; }
}
