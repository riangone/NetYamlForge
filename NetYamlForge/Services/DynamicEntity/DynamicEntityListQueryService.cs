// ファイル概要: エンティティ一覧表示に必要なデータを取得するサービスです。
// ページング（番号付き / キーセット）・検索・フィルター・COUNT の可否・
// クリア（検索条件リセット）を総合的に処理し、DynamicEntityListQueryResult を返します。
// Controller の Index / ListPartial アクションから呼ばれます。

using NetYamlForge.Models;
using Microsoft.AspNetCore.Http;

namespace NetYamlForge.Services;

/// <summary>
/// エンティティ一覧のクエリを実行するサービス。
/// ページング・検索・フィルター・FK データの取得を一括で処理します。
/// </summary>
public sealed class DynamicEntityListQueryService
{
    private readonly IDynamicCrudRepository _repo;
    private readonly DynamicEntityForeignKeyDataService _foreignKeyDataService;

    public DynamicEntityListQueryService(
        IDynamicCrudRepository repo,
        DynamicEntityForeignKeyDataService foreignKeyDataService)
    {
        _repo = repo;
        _foreignKeyDataService = foreignKeyDataService;
    }

    /// <summary>
    /// 一覧表示データを一括取得します。
    /// </summary>
    /// <param name="entity">エンティティ名</param>
    /// <param name="meta">エンティティ定義</param>
    /// <param name="search">全文検索キーワード</param>
    /// <param name="sort">ソート列名</param>
    /// <param name="dir">"asc" または "desc"</param>
    /// <param name="page">ページ番号（番号付きページング時）</param>
    /// <param name="pageSize">1ページあたり件数（null の場合は YAML 設定値を使用）</param>
    /// <param name="count">"0" なら COUNT(*) クエリをスキップ</param>
    /// <param name="clear">"1" なら検索・フィルターをリセット</param>
    /// <param name="cursor">キーセットページング用カーソル（最後のレコードID）</param>
    /// <param name="query">HTTP クエリコレクション（フィルター値の抽出に使用）</param>
    /// <param name="foreignKeysForForm">true: フォーム用FK、false: フィルター用FK を取得</param>
    public async Task<DynamicEntityListQueryResult> LoadAsync(
        string entity,
        EntityDefinition meta,
        string? search,
        string? sort,
        string? dir,
        int page,
        int? pageSize,
        string? count,
        string? clear,
        string? cursor,
        IQueryCollection query,
        bool foreignKeysForForm)
    {
        var effectivePageSize = pageSize ?? meta.Paging.PageSize;
        var isKeyset = meta.Paging.Mode.Equals("keyset", StringComparison.OrdinalIgnoreCase);
        var includeCount = ListQueryOptionResolver.ResolveCountEnabled(count);
        var clearRequested = ListQueryOptionResolver.ResolveClearRequested(clear);
        // クリアが要求された場合は検索キーワードを無視
        var effectiveSearch = clearRequested ? null : search;
        var filters = clearRequested
            ? FilterValueParser.BuildCleared(meta)
            : FilterValueParser.Build(meta, query);

        var itemsRaw = await _repo.GetAllAsync(
            entity,
            effectiveSearch,
            sort,
            dir,
            filters,
            page,
            effectivePageSize,
            cursor: cursor,
            keyset: isKeyset,
            fetchOneExtra: isKeyset || !includeCount);
        var (items, hasMore, nextCursor) = PagingResultBuilder.Build(
            itemsRaw,
            effectivePageSize,
            isKeyset || !includeCount,
            meta.Key);
        var total = includeCount ? await _repo.CountAsync(entity, effectiveSearch, filters) : -1;
        var fkData = foreignKeysForForm
            ? await _foreignKeyDataService.LoadForFormAsync(meta)
            : await _foreignKeyDataService.LoadForFiltersAsync(meta);

        return new DynamicEntityListQueryResult(
            items,
            total,
            fkData,
            filters,
            effectivePageSize,
            includeCount,
            hasMore,
            nextCursor,
            effectiveSearch);
    }
}

/// <summary>
/// DynamicEntityListQueryService.LoadAsync の戻り値。
/// Controller が View に渡す一覧表示に必要なすべてのデータを保持します。
/// </summary>
/// <param name="Items">現在ページのレコード一覧</param>
/// <param name="Total">総件数（IncludeCount=false の場合は -1）</param>
/// <param name="ForeignKeyData">FK フィールドの選択肢データ</param>
/// <param name="Filters">適用中のフィルター値マップ</param>
/// <param name="PageSize">実際に使用されたページサイズ</param>
/// <param name="IncludeCount">COUNT(*) を実行したか</param>
/// <param name="HasMore">次のページが存在するか（キーセット / COUNT なし時）</param>
/// <param name="NextCursor">キーセットページングの次カーソル値</param>
/// <param name="EffectiveSearch">実際に使用された検索キーワード（clear 時は null）</param>
public sealed record DynamicEntityListQueryResult(
    IEnumerable<dynamic> Items,
    int Total,
    Dictionary<string, IEnumerable<dynamic>> ForeignKeyData,
    Dictionary<string, string?> Filters,
    int PageSize,
    bool IncludeCount,
    bool HasMore,
    string? NextCursor,
    string? EffectiveSearch);
