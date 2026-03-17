// ファイル概要: エンティティ定義に含まれる外部キー（ForeignKey）の候補データを
// 一括ロードするサービスです。フィルタードロップダウンとフォームのドロップダウン用に
// 別々のメソッドを提供します。ピッカーモーダルは Ajax で個別取得するためここでは扱いません。

using NetYamlForge.Models;

namespace NetYamlForge.Services;

/// <summary>
/// フィルター・フォームに表示する外部キー選択肢データを非同期ロードするサービス。
/// EntityDefinition の Filters / Forms に定義された ForeignKey 設定をもとに
/// 参照先エンティティのレコードをすべて取得します。
/// </summary>
public sealed class DynamicEntityForeignKeyDataService
{
    private readonly IDynamicCrudRepository _repo;

    public DynamicEntityForeignKeyDataService(IDynamicCrudRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// フィルターバーのドロップダウン用に、FK を持つ全フィルターの選択肢データを取得します。
    /// </summary>
    /// <returns>フィールド名 → 選択肢レコード一覧 のディクショナリ</returns>
    public async Task<Dictionary<string, IEnumerable<dynamic>>> LoadForFiltersAsync(EntityDefinition meta)
    {
        var result = new Dictionary<string, IEnumerable<dynamic>>();
        foreach (var col in meta.Filters.Where(c => c.Value.ForeignKey != null))
        {
            var fk = col.Value.ForeignKey!;
            result[col.Key] = await _repo.GetAllForEntityAsync(fk.Entity, fk);
        }

        return result;
    }

    /// <summary>
    /// フォームのドロップダウン用に、FK を持つ全フォームフィールドの選択肢データを取得します。
    /// ピッカーモーダル（picker: true）のフィールドも含まれますが、
    /// ピッカーは表示時に Ajax で再取得するため初期値として使われます。
    /// </summary>
    /// <returns>フィールド名 → 選択肢レコード一覧 のディクショナリ</returns>
    public async Task<Dictionary<string, IEnumerable<dynamic>>> LoadForFormAsync(EntityDefinition meta)
    {
        var result = new Dictionary<string, IEnumerable<dynamic>>();
        foreach (var col in meta.Forms.Where(c => c.Value.ForeignKey != null))
        {
            var fk = col.Value.ForeignKey!;
            result[col.Key] = await _repo.GetAllForEntityAsync(fk.Entity, fk);
        }

        return result;
    }
}

