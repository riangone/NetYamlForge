// ファイル概要: YAMLメタデータ構造（列・フォーム・フィルタ・レイアウト）を定義します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

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

/// <summary>テーブルセルの値をフォーマットするユーティリティ。</summary>
public static class ColumnValueFormatter
{
    public static string FormatValue(string type, object? value, Dictionary<string, string>? optionLabels = null)
    {
        if (type.Equals("bool", StringComparison.OrdinalIgnoreCase))
        {
            var boolStr = value?.ToString() ?? "";
            var isTrue = value is bool b ? b
                : boolStr.Equals("true", StringComparison.OrdinalIgnoreCase) || boolStr == "1";
            return isTrue ? "✓ Yes" : "✗ No";
        }
        if (type.Equals("datetime", StringComparison.OrdinalIgnoreCase) && value is DateTime dt)
            return dt.ToString("yyyy-MM-dd");
        if (optionLabels != null)
        {
            var key = value?.ToString() ?? "";
            return optionLabels.TryGetValue(key, out var label) ? label : key;
        }
        return value?.ToString() ?? string.Empty;
    }
}

public static class I18nText
{
    private static readonly ResourceManager SharedResourceManager =
        new("NetYamlForge.Resources.Localization.SharedResource", typeof(SharedResource).Assembly);

    public static string Resolve(Dictionary<string, string>? map, string fallback, string? key = null)
    {
        var fromKey = ResolveByKey(key);
        if (!string.IsNullOrWhiteSpace(fromKey))
        {
            return fromKey;
        }

        return ResolveFromMap(map, fallback);
    }

    public static string? ResolveByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var culture = CultureInfo.CurrentUICulture;
        var yamlText = YamlKeyLocalizer.Resolve(key, culture, LocalizationProjectContext.CurrentProjectName);
        if (!string.IsNullOrWhiteSpace(yamlText))
        {
            return yamlText;
        }

        var exact = SafeGetResource(key, culture);
        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        var neutral = culture.TwoLetterISOLanguageName;
        if (!string.IsNullOrWhiteSpace(neutral))
        {
            var neutralText = SafeGetResource(key, new CultureInfo(neutral));
            if (!string.IsNullOrWhiteSpace(neutralText))
            {
                return neutralText;
            }
        }

        var enUs = SafeGetResource(key, new CultureInfo("en-US"));
        if (!string.IsNullOrWhiteSpace(enUs))
        {
            return enUs;
        }

        var en = SafeGetResource(key, new CultureInfo("en"));
        if (!string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        return null;
    }

    private static string? SafeGetResource(string key, CultureInfo culture)
    {
        try
        {
            return SharedResourceManager.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    public static string ResolveFromMap(Dictionary<string, string>? map, string fallback)
    {
        if (map == null || map.Count == 0)
        {
            return fallback;
        }

        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (map.TryGetValue(culture, out var exact) && !string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        var neutral = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var neutralKey = map.Keys.FirstOrDefault(k => k.StartsWith(neutral, StringComparison.OrdinalIgnoreCase));
        if (neutralKey != null && map.TryGetValue(neutralKey, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        if (map.TryGetValue("en-US", out var enUs) && !string.IsNullOrWhiteSpace(enUs))
        {
            return enUs;
        }

        if (map.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        return fallback;
    }
}

public class ForeignKeyDefinition
{
    public string Entity { get; set; } = default!;
    public string DisplayColumn { get; set; } = "Id";
    public List<string>? DisplayColumns { get; set; }
    /// <summary>
    /// 参照先データ取得のカスタムSQL。指定時はこのSQLをベースに候補を取得します。
    /// 期待される戻り値には `Id` 列を含めてください。
    /// </summary>
    public string? Query { get; set; }
    // ドロップダウンの代わりにピッカーモーダルで選択するか（単一選択）
    public bool Picker { get; set; }
    // ピッカーモーダルで複数選択するか
    public bool MultiPicker { get; set; }

    public IReadOnlyList<string> GetDisplayColumns()
    {
        if (DisplayColumns != null && DisplayColumns.Count > 0)
        {
            return DisplayColumns
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(DisplayColumn))
        {
            return DisplayColumn
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        return new[] { "Id" };
    }
}

public class JoinDefinition
{
    public string Type { get; set; } = "left";
    public string Table { get; set; } = default!;
    public string Alias { get; set; } = default!;
    public string? On { get; set; }
    // YAML 1.1 互換: `on:` キーが bool `true` として解釈されるケースを受ける
    public string? True { get; set; }

    public string? GetJoinCondition() =>
        !string.IsNullOrWhiteSpace(On) ? On : True;
}

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
    public List<string>? Options { get; set; }
    public string? LabelKey { get; set; }
    public Dictionary<string, string>? LabelI18n { get; set; }
    public bool Hidden { get; set; }

    public string GetLabel(string fallback) => I18nText.Resolve(LabelI18n, Label ?? fallback, LabelKey);
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

public class FormLayoutDefinition
{
    public int Columns { get; set; } = 2;
    public List<string> Order { get; set; } = new();
}

public class FilterLayoutDefinition
{
    public int Columns { get; set; } = 4;
    public List<string> Order { get; set; } = new();
}

public class EntityLayoutDefinition
{
    public FormLayoutDefinition Forms { get; set; } = new();
    public FilterLayoutDefinition Filters { get; set; } = new();
}

public class EntityDefinition
{
    public string Table { get; set; } = default!;
    /// <summary>
    /// 主键定義。単一主鍵の場合は列名を文字列で設定。
    /// 複合主鍵の場合は Keys プロパティを使用（こちらが優先される）。
    /// </summary>
    public string Key { get; set; } = default!;
    /// <summary>
    /// 複合主鍵の列名リスト。設定されている場合、Key プロパティより優先される。
    /// 例：["OrderId", "ProductId"]
    /// </summary>
    public List<string> Keys { get; set; } = new();
    public string DisplayName { get; set; } = default!;
    public string? DisplayNameKey { get; set; }
    public Dictionary<string, string>? DisplayNameI18n { get; set; }
    public List<JoinDefinition> Joins { get; set; } = new();
    public Dictionary<string, FormDefinition> Forms { get; set; } = new();
    public Dictionary<string, ColumnDefinition> Columns { get; set; } = new();
    public PagingDefinition Paging { get; set; } = new();
    public EntityLayoutDefinition Layout { get; set; } = new();
    public bool SoftDelete { get; set; }
    public bool IsPublic { get; set; } = true;
    public Dictionary<string, FilterDefinition> Filters { get; set; } = new();
    public Dictionary<string, EntityLinkDefinition> Links { get; set; } = new();
    /// <summary>新規作成・更新時の確認ダイアログ設定</summary>
    public ConfirmationDefinition? Confirmation { get; set; }
    /// <summary>前処理・後処理フックの設定</summary>
    public EntityHooksDefinition? Hooks { get; set; }
    /// <summary>一覧・詳細画面に追加するカスタムアクションボタン定義</summary>
    public Dictionary<string, ActionDefinition> Actions { get; set; } = new();

    /// <summary>
    /// 主鍵列名のリストを取得する。複合主鍵の場合は Keys を返し、
    /// 単一主鍵の場合は Key を含むリストを返す。
    /// </summary>
    public IReadOnlyList<string> GetPrimaryKeyColumns()
    {
        if (Keys.Count > 0)
        {
            return Keys.AsReadOnly();
        }
        return new[] { Key }.AsReadOnly();
    }

    /// <summary>
    /// 主鍵が複合主鍵かどうかを返す。
    /// </summary>
    public bool IsCompositeKey => Keys.Count > 1 || (Keys.Count == 1 && Keys[0] != Key);

    public string GetDisplayName() => I18nText.Resolve(DisplayNameI18n, DisplayName, DisplayNameKey);

    public IEnumerable<KeyValuePair<string, FormDefinition>> GetOrderedForms()
    {
        if (Layout.Forms.Order.Count == 0)
        {
            return Forms;
        }

        var result = new List<KeyValuePair<string, FormDefinition>>();
        foreach (var key in Layout.Forms.Order)
        {
            if (Forms.TryGetValue(key, out var def))
            {
                result.Add(new KeyValuePair<string, FormDefinition>(key, def));
            }
        }

        result.AddRange(Forms.Where(f => result.All(x => x.Key != f.Key)));
        return result;
    }

    public IEnumerable<KeyValuePair<string, FilterDefinition>> GetOrderedFilters()
    {
        if (Layout.Filters.Order.Count == 0)
        {
            return Filters;
        }

        var result = new List<KeyValuePair<string, FilterDefinition>>();
        foreach (var key in Layout.Filters.Order)
        {
            if (Filters.TryGetValue(key, out var def))
            {
                result.Add(new KeyValuePair<string, FilterDefinition>(key, def));
            }
        }

        result.AddRange(Filters.Where(f => result.All(x => x.Key != f.Key)));
        return result;
    }
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

/// <summary>
/// 前処理・後処理フックの設定。
/// entities.yml の hooks セクションに対応します。
/// 単一フック名（文字列）と複数フック名（リスト）の両方をサポートします。
/// </summary>
public class EntityHooksDefinition : HooksDefinitionBase
{
    private string? _beforeCreate;
    private List<string>? _beforeCreateList;
    private string? _afterCreate;
    private List<string>? _afterCreateList;
    private string? _beforeUpdate;
    private List<string>? _beforeUpdateList;
    private string? _afterUpdate;
    private List<string>? _afterUpdateList;
    private string? _beforeDelete;
    private List<string>? _beforeDeleteList;
    private string? _afterDelete;
    private List<string>? _afterDeleteList;
    private Dictionary<string, object>? _presets;

    /// <summary>DB 書き込み前に実行するフック名（create 時、単一またはリスト）</summary>
    public object? BeforeCreate
    {
        get => _beforeCreateList ?? (object?)_beforeCreate;
        set
        {
            if (value is string s) _beforeCreate = s;
            else if (value is List<object> list) _beforeCreateList = list.ConvertAll(x => x.ToString() ?? "");
            else if (value is System.Collections.IEnumerable e) _beforeCreateList = e.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        }
    }

    /// <summary>DB 書き込み後に実行するフック名（create 時、単一またはリスト）</summary>
    public object? AfterCreate
    {
        get => _afterCreateList ?? (object?)_afterCreate;
        set
        {
            if (value is string s) _afterCreate = s;
            else if (value is List<object> list) _afterCreateList = list.ConvertAll(x => x.ToString() ?? "");
            else if (value is System.Collections.IEnumerable e) _afterCreateList = e.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        }
    }

    /// <summary>DB 書き込み前に実行するフック名（update 時、単一またはリスト）</summary>
    public object? BeforeUpdate
    {
        get => _beforeUpdateList ?? (object?)_beforeUpdate;
        set
        {
            if (value is string s) _beforeUpdate = s;
            else if (value is List<object> list) _beforeUpdateList = list.ConvertAll(x => x.ToString() ?? "");
            else if (value is System.Collections.IEnumerable e) _beforeUpdateList = e.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        }
    }

    /// <summary>DB 書き込み後に実行するフック名（update 時、単一またはリスト）</summary>
    public object? AfterUpdate
    {
        get => _afterUpdateList ?? (object?)_afterUpdate;
        set
        {
            if (value is string s) _afterUpdate = s;
            else if (value is List<object> list) _afterUpdateList = list.ConvertAll(x => x.ToString() ?? "");
            else if (value is System.Collections.IEnumerable e) _afterUpdateList = e.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        }
    }

    /// <summary>DB 書き込み前に実行するフック名（delete 時、単一またはリスト）</summary>
    public object? BeforeDelete
    {
        get => _beforeDeleteList ?? (object?)_beforeDelete;
        set
        {
            if (value is string s) _beforeDelete = s;
            else if (value is List<object> list) _beforeDeleteList = list.ConvertAll(x => x.ToString() ?? "");
            else if (value is System.Collections.IEnumerable e) _beforeDeleteList = e.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        }
    }

    /// <summary>DB 書き込み後に実行するフック名（delete 時、単一またはリスト）</summary>
    public object? AfterDelete
    {
        get => _afterDeleteList ?? (object?)_afterDelete;
        set
        {
            if (value is string s) _afterDelete = s;
            else if (value is List<object> list) _afterDeleteList = list.ConvertAll(x => x.ToString() ?? "");
            else if (value is System.Collections.IEnumerable e) _afterDeleteList = e.Cast<object>().Select(x => x.ToString() ?? "").ToList();
        }
    }

    /// <summary>
    /// 再利用可能なフックプリセット定義。
    /// 例:
    /// hooks:
    ///   presets:
    ///     commonValidation:
    ///       - "validate_required:FirstName,LastName"
    ///       - "trim:FirstName,LastName"
    /// </summary>
    public Dictionary<string, object>? Presets
    {
        get => _presets;
        set => _presets = value;
    }

    /// <summary>フック名リストを取得するヘルパーメソッド</summary>
    public List<string>? GetHookList(Func<EntityHooksDefinition, object?> selector)
    {
        var value = selector(this);
        if (value is string s) return new List<string> { s };
        if (value is List<string> list) return list;
        if (value is System.Collections.IEnumerable e) return e.Cast<object>().Select(x => x?.ToString() ?? "").ToList();
        return null;
    }

    /// <summary>
    /// プリセット名に対応するフック名リストを返す（HooksDefinitionBase の実装）。
    /// </summary>
    protected override IReadOnlyList<string>? GetPreset(string presetName)
    {
        if (_presets == null || !_presets.TryGetValue(presetName, out var presetValue))
            return null;
        var list = ToHookList(presetValue);
        return list.Count == 0 ? null : list;
    }

    /// <summary>
    /// フック名リストを取得し、@preset 形式を展開した実行順リストを返します。
    /// 例: ["@commonValidation", "audit_log"]。
    /// </summary>
    public List<string>? GetExpandedHookList(
        Func<EntityHooksDefinition, object?> selector,
        Action<string>? onWarning = null)
    {
        var hooks = GetHookList(selector);
        if (hooks == null || hooks.Count == 0)
            return hooks;
        return ExpandHookEntries(hooks, onWarning);
    }

    private static List<string> ToHookList(object? value)
    {
        if (value is null)
        {
            return new List<string>();
        }

        if (value is string s)
        {
            return new List<string> { s };
        }

        if (value is List<string> list)
        {
            return list;
        }

        if (value is System.Collections.IEnumerable e)
        {
            return e.Cast<object>().Select(x => x?.ToString() ?? "").ToList();
        }

        return new List<string>();
    }
}

/// <summary>カスタムアクションの入力フィールド定義</summary>
public class ActionInputField
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = "string";
    public string? Label { get; set; }
    public bool Required { get; set; }
    public List<string>? Options { get; set; }
}

/// <summary>カスタムアクションのフック定義（実行前後）</summary>
public class ActionHooksDefinition
{
    public List<string>? Before { get; set; }
    public List<string>? After { get; set; }
}

/// <summary>
/// 一覧画面に表示するカスタムアクションボタン定義。
/// entities.yml の actions セクションに対応します。
/// </summary>
public class ActionDefinition
{
    /// <summary>ボタンに表示するラベル</summary>
    public string Label { get; set; } = default!;
    /// <summary>
    /// アクションのスコープ。
    /// "row"（デフォルト）: 各行のアクション列に表示。
    /// "header": 一覧ヘッダーの右側に表示（行に依存しない操作 = エクスポート等）。
    /// </summary>
    public string Scope { get; set; } = "row";
    /// <summary>実行前に表示する確認メッセージ（null の場合は確認なし）</summary>
    public string? Confirm { get; set; }
    /// <summary>実行する ICustomActionHandler の Name（省略時はアクションキー名と同じ）</summary>
    public string? Handler { get; set; }
    /// <summary>アクション実行時に入力を求めるフィールド定義</summary>
    public List<ActionInputField>? Inputs { get; set; }
    /// <summary>アクション前後に実行するフック</summary>
    public ActionHooksDefinition? Hooks { get; set; }
}

public class EntityLinkDefinition
{
    public string Label { get; set; } = default!;
    public string? LabelKey { get; set; }
    /// <summary>多言語ラベル（entities.yml の labelI18n セクション）</summary>
    public Dictionary<string, string>? LabelI18n { get; set; }
    public string TargetEntity { get; set; } = default!;
    // 静的クエリパラメータ（例: sort=Name）
    public Dictionary<string, string>? Query { get; set; }
    // 行ごとの動的フィルタ: targetQueryParam → sourceRowColumn
    // 例: { "CustomerId": "CustomerId" } → 行の CustomerId 値を ?CustomerId=xxx として付与
    public Dictionary<string, string>? Filter { get; set; }

    public string GetLabel() => I18nText.Resolve(LabelI18n, Label, LabelKey);
}

public class EntityConfigRoot
{
    /// <summary>
    /// 読み込む前にインポートする YAML ファイルのパスリスト（このファイルからの相対パス）。
    /// 例: ["shared/base-fields.yml"]
    /// </summary>
    public List<string> Imports { get; set; } = new();
    public Dictionary<string, EntityDefinition> Entities { get; set; } = new();
}
