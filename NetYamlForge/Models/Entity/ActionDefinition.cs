using System.Globalization;
using System.Resources;
using NetYamlForge.Localization;

namespace NetYamlForge.Models;

/// <summary>
/// 前処理・後処理フックの設定。
/// entities.yml の hooks セクションに対応します。
/// </summary>
public class EntityHooksDefinition
{
    public object? BeforeCreate { get; set; }
    public object? AfterCreate { get; set; }
    public object? BeforeUpdate { get; set; }
    public object? AfterUpdate { get; set; }
    public object? BeforeDelete { get; set; }
    public object? AfterDelete { get; set; }
    public Dictionary<string, object>? Presets { get; set; }
}

/// <summary>カスタムアクションの入力フィールド定義</summary>
public class ActionInputField
{
    public string Name { get; set; } = default!;
    /// <summary>
    /// 入力フィールドの種别。
    /// string / text / textarea / date / number / dropdown（Options 指定時）/ file
    /// </summary>
    public string Type { get; set; } = "string";
    public string? Label { get; set; }
    public string? LabelKey { get; set; }
    public Dictionary<string, string>? LabelI18n { get; set; }
    public bool Required { get; set; }
    /// <summary>dropdown 用の選択肢</summary>
    public List<string>? Options { get; set; }
    /// <summary>type: file — 許可する拡張子（カンマ区切り例: ".csv,.xlsx"）。省略時は全拡張子許可。</summary>
    public string? AllowedExtensions { get; set; }
    /// <summary>type: file — 最大ファイルサイズ（バイト）。省略時は 10MB。</summary>
    public long? MaxSizeBytes { get; set; }
    /// <summary>type: file — 複数ファイルの選択を許可するかどうか</summary>
    public bool Multiple { get; set; }

    public string GetLabel(string fallback) => I18nText.Resolve(LabelI18n, Label ?? fallback, LabelKey);
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
    /// <summary>多语言翻译 Key</summary>
    public string? LabelKey { get; set; }
    /// <summary>多语言翻译 map</summary>
    public Dictionary<string, string>? LabelI18n { get; set; }
    /// <summary>実行前に表示する確認メッセージ（null の場合は確認なし）</summary>
    public string? Confirm { get; set; }
    /// <summary>确认提示多语言 Key</summary>
    public string? ConfirmKey { get; set; }
    /// <summary>确认提示多语言 map</summary>
    public Dictionary<string, string>? ConfirmI18n { get; set; }
    /// <summary>ボタンに適用する CSS クラス（例: btn-success, btn-danger）</summary>
    public string? Class { get; set; }
    /// <summary>実行する ICustomActionHandler の Name（省略時はアクションキー名と同じ）</summary>
    public string? Handler { get; set; }
    /// <summary>アクション実行時に入力を求めるフィールド定義</summary>
    public List<ActionInputField>? Inputs { get; set; }
    /// <summary>アクション前後に実行するフック</summary>
    public ActionHooksDefinition? Hooks { get; set; }

    public string GetLabel() => I18nText.Resolve(LabelI18n, Label, LabelKey);
    public string? GetConfirm() => I18nText.Resolve(ConfirmI18n, Confirm ?? "", ConfirmKey);
}
