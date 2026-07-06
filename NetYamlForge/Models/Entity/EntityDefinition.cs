using System.Globalization;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

public class EntityLayoutDefinition
{
    public FormLayoutDefinition Forms { get; set; } = new();
    public FilterLayoutDefinition Filters { get; set; } = new();
}

public class EntityDefinition
{
    public string Table { get; set; } = default!;
    public WorkflowDefinition? Workflow { get; set; }
    public RateLimitingDefinition? RateLimiting { get; set; }
    public SecurityDefinition? Security { get; set; }
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
    public string Api { get; set; } = "disabled";
    public Dictionary<string, FilterDefinition> Filters { get; set; } = new();
    public Dictionary<string, EntityLinkDefinition> Links { get; set; } = new();
    /// <summary>新規作成・更新時の確認ダイアログ設定</summary>
    public ConfirmationDefinition? Confirmation { get; set; }
    /// <summary>前処理・後処理フックの設定</summary>
    public EntityHooksDefinition? Hooks { get; set; }
    /// <summary>一覧・詳細画面に追加するカスタムアクションボタン定義</summary>
    public Dictionary<string, ActionDefinition> Actions { get; set; } = new();
    /// <summary>ツールバーに追加するカスタムエクスポートボタン定義</summary>
    public Dictionary<string, ExportDefinition> Exports { get; set; } = new();
    /// <summary>
    /// 帳票 PDF テンプレート名。設定すると一覧行に「帳票」ボタンが表示されます。
    /// プロジェクトの pdf-templates/ ディレクトリ内の YAML ファイル名（拡張子なし）を指定します。
    /// </summary>
    public string? PdfTemplate { get; set; }

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

public class EntityConfigRoot
{
    /// <summary>
    /// 読み込む前にインポートする YAML ファイルのパスリスト（このファイルからの相対パス）。
    /// 例: ["shared/base-fields.yml"]
    /// </summary>
    public List<string> Imports { get; set; } = new();
    public Dictionary<string, EntityDefinition> Entities { get; set; } = new();
}
