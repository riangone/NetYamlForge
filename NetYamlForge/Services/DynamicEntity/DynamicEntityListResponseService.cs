// ファイル概要: CRUD 操作（作成・更新・削除）成功後に一覧データを再取得するサービスです。
// Controller が変更操作の完了後にリストを最新状態に更新するために呼び出します。
// 検索条件・フィルターをリセットして先頭ページを取得します。

namespace NetYamlForge.Services;

/// <summary>
/// CRUD ミューテーション完了後の一覧再取得サービス。
/// 変更後に HTMX の部分更新でリストを最新化する際に使用します。
/// </summary>
public sealed class DynamicEntityListResponseService
{
    private readonly IDynamicCrudRepository _repo;

    public DynamicEntityListResponseService(IDynamicCrudRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// CRUD 操作成功後に、検索・フィルターなしで先頭ページのデータを取得します。
    /// </summary>
    /// <param name="entity">エンティティ名（YAML キー）</param>
    /// <param name="includeCount">true なら COUNT(*) を実行して総件数も返す。false なら Total = -1。</param>
    /// <returns>Items: 先頭ページのレコード一覧、Total: 総件数（includeCount=false なら -1）</returns>
    public async Task<(IEnumerable<dynamic> Items, int Total)> LoadFirstPageAfterMutationAsync(
        string entity,
        bool includeCount = true)
    {
        var items = await _repo.GetAllAsync(entity, null, null, null);
        var total = includeCount ? await _repo.CountAsync(entity, null) : -1;
        return (items, total);
    }
}

