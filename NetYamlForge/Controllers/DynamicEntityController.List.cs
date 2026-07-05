// ファイル概要: 動的エンティティの一覧・作成・編集・削除・部分更新を処理します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.DynamicEntity;
using NetYamlForge.Services.Hooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using System.Data;
using System.Text;
using System.Text.Json;

namespace NetYamlForge.Controllers;

public partial class DynamicEntityController : BaseProjectController
{
    [HttpGet("List/{entity}")]
    public IActionResult List(string entity)
    {
        return RedirectToAction(nameof(Index), new { entity });
    }

    public async Task<IActionResult> Index(
        string entity = "customer",
        string? search = null,
        string? sort = null,
        string? dir = null,
        int? pageSize = null,
        string? count = null,
        string? clear = null,
        string? cursor = null,
        string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        count = NormalizeSingleValue(count);
        clear = NormalizeSingleValue(clear);

        // 初期画面表示。メタデータから検索条件を解釈し、一覧表示モデルを構築します。
        if (!_meta.TryGet(entity, out var meta))
        {
            _logger.LogWarning("Entity '{Entity}' not found in project '{Project}'", entity, _projectScope.Current.Name);
            return NotFound($"Entity '{entity}' は このプロジェクトに存在しません。");
        }
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var queryResult = await _listQueryService.LoadAsync(
            entity,
            meta,
            search,
            sort,
            dir,
            page: 1,
            pageSize,
            count,
            clear,
            cursor,
            Request.Query,
            foreignKeysForForm: false);
        var page = 1;

        return View("Index",
            CreateListViewModel(
                entity,
                meta,
                queryResult.Items,
                queryResult.EffectiveSearch,
                sort,
                dir,
                queryResult.ForeignKeyData,
                page,
                queryResult.Total,
                queryResult.Filters,
                queryResult.PageSize,
                queryResult.IncludeCount,
                queryResult.HasMore,
                queryResult.NextCursor,
                cursor,
                returnUrl));
    }

    public async Task<IActionResult> ListPartial(
        string entity = "customer",
        string? search = null,
        string? sort = null,
        string? dir = null,
        int page = 1,
        int? pageSize = null,
        string? count = null,
        string? clear = null,
        string? cursor = null,
        string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        count = NormalizeSingleValue(count);
        clear = NormalizeSingleValue(clear);

        // HTMXによる一覧部分更新。count有無・keyset有無をここで切り替えます。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var queryResult = await _listQueryService.LoadAsync(
            entity,
            meta,
            search,
            sort,
            dir,
            page,
            pageSize,
            count,
            clear,
            cursor,
            Request.Query,
            foreignKeysForForm: true);
        _listHttpResponseService.TrySetPushUrl(
            Request,
            Response,
            Url.Action(nameof(Index), "DynamicEntity"),
            Request.Query,
            entity,
            returnUrl);

        return PartialView("_List",
            CreateListViewModel(
                entity,
                meta,
                queryResult.Items,
                queryResult.EffectiveSearch,
                sort,
                dir,
                queryResult.ForeignKeyData,
                page,
                queryResult.Total,
                queryResult.Filters,
                queryResult.PageSize,
                queryResult.IncludeCount,
                queryResult.HasMore,
                queryResult.NextCursor,
                cursor,
                returnUrl));
    }

    // エンティティ選択ピッカーモーダル用の一覧を返します
    public async Task<IActionResult> PickerList(
        string entity,
        string targetField = "",
        string displayColumn = "Id",
        string[]? displayColumns = null,
        string? query = null,
        bool multi = false,
        string? search = null,
        int page = 1,
        int pageSize = 10)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }

        var displayCols = new List<string>();
        if (displayColumns != null && displayColumns.Length > 0)
        {
            foreach (var item in displayColumns)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                displayCols.AddRange(
                    item.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }
        }

        var fk = new ForeignKeyDefinition
        {
            Entity = entity,
            DisplayColumn = displayColumn,
            DisplayColumns = displayCols.Count > 0 ? displayCols.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : null,
            Query = query
        };
        var itemsRaw = await _repo.GetAllForEntityAsync(entity, fk, search, page, pageSize, fetchOneExtra: true);
        var list = itemsRaw.ToList();
        var hasMore = list.Count > pageSize;
        if (hasMore) list = list.Take(pageSize).ToList();

        return PartialView("_Picker", new PickerViewModel(entity, meta, list, targetField, fk.GetDisplayColumns().ToList(), multi, search, page, pageSize, hasMore));
    }
}
