using System.Globalization;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

/// <summary>
/// ツールバーに追加するカスタムダウンロードエクスポート定義。
/// entities.yml の exports セクションに対応します。
/// </summary>
public class ExportDefinition
{
    /// <summary>ボタンに表示するラベル</summary>
    public string Label { get; set; } = default!;
    /// <summary>出力フォーマット: csv (デフォルト), tsv, json</summary>
    public string Format { get; set; } = "csv";
    /// <summary>
    /// ダウンロードファイル名パターン。
    /// {date:yyyyMMdd} のようなプレースホルダーを使用可能。
    /// 省略時は "{entity}_{exportKey}_{date:yyyyMMdd_HHmmss}.{format}" を使用。
    /// </summary>
    public string? Filename { get; set; }
    /// <summary>
    /// 出力する列キーのリスト（省略時: 非表示でない全列）。
    /// カスタム SQL 使用時は SQL 結果の全列を出力。
    /// </summary>
    public List<string>? Columns { get; set; }
    /// <summary>カスタム SQL クエリ（省略時: エンティティのクエリパイプラインを使用）</summary>
    public string? SqlQuery { get; set; }
    /// <summary>カスタム SQL ファイルパス（プロジェクトルートからの相対パス）</summary>
    public string? SqlFile { get; set; }
    /// <summary>format: pdf の場合の PDF 固有オプション</summary>
    public PdfExportOptions? Pdf { get; set; }
}

/// <summary>
/// PDF エクスポートのレイアウト・スタイル設定。
/// entities.yml の exports.*.pdf セクションに対応します。
/// </summary>
public class PdfExportOptions
{
    /// <summary>PDF の最上部に表示するレポートタイトル</summary>
    public string? Title { get; set; }
    /// <summary>用紙サイズ: A4 (デフォルト), A3, LETTER, LEGAL</summary>
    public string PageSize { get; set; } = "A4";
    /// <summary>向き: portrait (デフォルト) / landscape</summary>
    public string Orientation { get; set; } = "portrait";
    /// <summary>
    /// 使用フォントの TTF/OTF ファイルパス（プロジェクトルートからの相対パス、または絶対パス）。
    /// 省略時はシステムフォントを自動検索し、見つからない場合は Helvetica にフォールバック。
    /// 日本語など多バイト文字を含む場合は必ず Unicode 対応フォントを指定してください。
    /// </summary>
    public string? FontFile { get; set; }
    /// <summary>ヘッダー行の背景色（16進数 例: "#1E3A5F"）</summary>
    public string HeaderColor { get; set; } = "#1E3A5F";
    /// <summary>奇数データ行の背景色（省略時は交互色なし）</summary>
    public string? OddRowColor { get; set; }
    /// <summary>フッターにページ番号を表示するか</summary>
    public bool ShowPageNumbers { get; set; } = true;
    /// <summary>タイトル下に生成日時を表示するか</summary>
    public bool ShowGeneratedAt { get; set; } = true;
    /// <summary>列ごとの幅・配置設定（省略時は等幅）</summary>
    public List<PdfColumnOptions>? Columns { get; set; }
}

/// <summary>PDF テーブルの列ごとのスタイル設定</summary>
public class PdfColumnOptions
{
    /// <summary>エンティティ列キー（YAML の columns キーと同じ）</summary>
    public string Key { get; set; } = default!;
    /// <summary>ヘッダーに表示するラベル（省略時はエンティティ定義のラベルを使用）</summary>
    public string? Label { get; set; }
    /// <summary>列幅（パーセント。0 または省略時は等幅配分）</summary>
    public float Width { get; set; } = 0f;
    /// <summary>テキスト配置: left (デフォルト) / center / right</summary>
    public string Align { get; set; } = "left";
}
