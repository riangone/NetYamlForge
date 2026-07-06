using System.Globalization;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

/// <summary>
/// フォームフィールド定義。entities.forms 配下の各フィールド定義。
/// type の有効値：string | int | long | decimal | double | bool | date | datetime |
///               textarea | email | select | radio | color | money | rating | file |
///               toggle-group | multi-select | checkbox-group | switch-group |
///               autocomplete | tags | percent | tel | url | password |
///               datetime-range | code | json | signature | map | sortable-list |
///               image | richtext | markdown
/// </summary>
public class FormDefinition
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
    /// <summary>
    /// 選択肢リスト。YAML: options: [a, b, c]
    /// </summary>
    public List<string>? Options { get; set; }
    /// <summary>
    /// 選択肢辞書。key=保存値、value=表示名。YAML: options: { a: "Label A", b: "Label B" }
    /// Options (List) より優先して使用されます。
    /// filters の OptionLabels と同様の役割を果たします。
    /// </summary>
    public Dictionary<string, string>? OptionLabels { get; set; }
    /// <summary>
    /// toggle-group や multi-select などで使用するオプションラベル辞書。
    /// OptionLabels と同じく key=保存値、value=表示名。
    /// YAML: optionLabels: { a: "📝 A", b: "📤 B" }
    /// </summary>
    public Dictionary<string, string>? OptionLabelsWithIcon { get; set; }
    public string? LabelKey { get; set; }
    public Dictionary<string, string>? LabelI18n { get; set; }
    public bool Hidden { get; set; }
    /// <summary>
    /// プレースホルダーテキスト
    /// </summary>
    public string? Placeholder { get; set; }
    /// <summary>
    /// 通貨タイプ（money タイプで使用）
    /// </summary>
    public string? Currency { get; set; }
    /// <summary>
    /// ロケール（通貨・数値フォーマットで使用）
    /// </summary>
    public string? Locale { get; set; }
    /// <summary>
    /// 小数点以下の桁数（decimal/double タイプで使用）
    /// </summary>
    public int? Precision { get; set; }
    /// <summary>
    /// ファイルアップロード先ディレクトリ
    /// </summary>
    public string? UploadPath { get; set; }
    /// <summary>
    /// 許可されるファイル拡張子（カンマ区切り）
    /// </summary>
    public string? AllowedExtensions { get; set; }
    /// <summary>
    /// 最大ファイルサイズ（バイト）
    /// </summary>
    public long? MaxFileSize { get; set; }
    /// <summary>
    /// サムネイルサイズ定義
    /// </summary>
    public ThumbnailSizeDefinition? ThumbnailSize { get; set; }

    /// <summary>
    /// 表示時のグリッド列スパン（デフォルト 1）。
    /// </summary>
    public int ColSpan { get; set; } = 1;

    public FieldSecurityDefinition? Security { get; set; }
    public List<ValidatorDefinition> Validators { get; set; } = new();

    public string GetLabel(string fallback) => I18nText.Resolve(LabelI18n, Label ?? fallback, LabelKey);
}

public class FormLayoutDefinition
{
    public int Columns { get; set; } = 2;
    public List<string> Order { get; set; } = new();
}

/// <summary>
/// 新規作成・更新時の確認ダイアログメッセージ設定。
/// entities.yml の confirmation セクションに対応します。
/// </summary>
public class ConfirmationDefinition
{
    /// <summary>新規作成時の確認メッセージ（null/空の場合は確認なし）</summary>
    public string? Create { get; set; }
    /// <summary>更新時の確認メッセージ（null/空の場合は確認なし）</summary>
    public string? Update { get; set; }
    /// <summary>削除時の確認メッセージ（null/空の場合は確認なし）</summary>
    public string? Delete { get; set; }
}
