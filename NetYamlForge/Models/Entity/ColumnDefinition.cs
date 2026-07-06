using System.Globalization;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

/// <summary>
/// 列定義の共通インターフェース。ColumnDefinition と SectionColumnDef の両方が実装する。
/// テーブルセル値フォーマットなど横断的な処理に使用する。
/// </summary>
public interface IColumnDef
{
    string Type { get; }
    bool Hidden { get; }
    string GetLabel(string fallback);
    /// <summary>select/dropdown 用の key=保存値/value=表示名 辞書。null の場合は型変換のみ行う。</summary>
    Dictionary<string, string>? OptionLabels { get; }
}

public class ColumnDefinition : IColumnDef
{
    public string Type { get; set; } = "string";
    public bool Identity { get; set; }
    public bool Required { get; set; }
    public string? Label { get; set; }
    public bool Searchable { get; set; }
    public bool Sortable { get; set; }
    public bool Editable { get; set; } = true;
    public string? Expression { get; set; }
    public ForeignKeyDefinition? ForeignKey { get; set; }
    // 互換性維持: 一部定義で columns.*.options を使用しているため受け口を持たせる
    public List<string>? Options { get; set; }
    /// <summary>key=保存値, value=表示名 の選択肢辞書。Options (List) より優先して使用される。
    /// SectionFormFieldDef の Options (Dict) を _FormField で描画する際に設定する。</summary>
    public Dictionary<string, string>? OptionLabels { get; set; }
    public string? Placeholder { get; set; }
    public string? LabelKey { get; set; }
    public Dictionary<string, string>? LabelI18n { get; set; }
    public bool Hidden { get; set; }

    // ファイルアップロード関連プロパティ
    public string? UploadPath { get; set; }
    public string? AllowedExtensions { get; set; }
    public long? MaxFileSize { get; set; }
    public ThumbnailSizeDefinition? ThumbnailSize { get; set; }

    // 通貨・数値関連プロパティ
    public string? Currency { get; set; }
    public string? Locale { get; set; }
    public int? Precision { get; set; }

    public FieldSecurityDefinition? Security { get; set; }
    public List<ValidatorDefinition> Validators { get; set; } = new();

    public string GetLabel(string fallback) => I18nText.Resolve(LabelI18n, Label ?? fallback, LabelKey);
}

/// <summary>
/// サムネイルサイズの定義
/// </summary>
public class ThumbnailSizeDefinition
{
    public int Width { get; set; } = 150;
    public int Height { get; set; } = 150;
}

public class PagingDefinition
{
    public int PageSize { get; set; } = 5;
    public string Mode { get; set; } = "numbered";
    public bool EnableCount { get; set; } = true;
}
