// ファイル概要: _Form.cshtml に渡す DynamicFormViewModel を組み立てるファクトリです。
// 作成フォーム（item=null）と編集フォーム（item=既存レコード）の両方に対応します。
// mode により「modal」（モーダルダイアログ）か「page」（フルページ）を切り替えます。

using NetYamlForge.Models;
using NetYamlForge.Controllers;

namespace NetYamlForge.Services;

/// <summary>
/// フォームビューモデルのファクトリ。Controller の CreateForm / EditForm アクションから呼ばれます。
/// DynamicFormViewModel のコンストラクタ引数を一か所に集約し、Controller を薄く保ちます。
/// </summary>
public sealed class DynamicEntityFormViewModelFactory
{
    /// <summary>
    /// フォームビューモデルを構築します。
    /// </summary>
    /// <param name="entity">エンティティ名（YAML キー）</param>
    /// <param name="meta">エンティティ定義（フォームフィールドの型・ラベル等）</param>
    /// <param name="item">編集対象レコード（新規作成時は null）</param>
    /// <param name="foreignKeyData">FK フィールドの選択肢データ（DynamicEntityForeignKeyDataService が提供）</param>
    /// <param name="errors">バリデーションエラー（フィールド名 → メッセージ）</param>
    /// <param name="mode">"modal"（ダイアログ表示）または "page"（フルページ表示）</param>
    /// <param name="breadcrumbChain">フルページ表示時のパンくずリスト</param>
    /// <param name="submittedValues">バリデーション失敗時に送信値を再表示するためのマップ</param>
    public DynamicFormViewModel Build(
        string entity,
        EntityDefinition meta,
        dynamic? item,
        Dictionary<string, IEnumerable<dynamic>> foreignKeyData,
        Dictionary<string, string>? errors = null,
        string mode = "modal",
        IReadOnlyList<BreadcrumbItem>? breadcrumbChain = null,
        Dictionary<string, string?>? submittedValues = null)
    {
        return new DynamicFormViewModel(
            entity,
            meta,
            item,
            foreignKeyData,
            errors ?? new Dictionary<string, string>(),
            mode,
            breadcrumbChain,
            submittedValues,
            meta.Columns);
    }
}

