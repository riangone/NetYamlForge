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
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string entity, [FromForm] Dictionary<string, string?> form, [FromForm] IFormFileCollection? files = null, string mode = "modal", [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 登録処理。CRUD本体と監査ログを同一トランザクションで実行します。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var (values, errors) = await _formValidationService.ConvertAndValidateAsync(meta, form, _projectScope.Current?.Name ?? "DefaultProject");
        var isPageMode = mode.Equals("page", StringComparison.OrdinalIgnoreCase);

        if (errors.Any())
            return await RenderFormWithErrorsAsync(entity, meta, mode, form, errors);

        var beforeCreateHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeCreate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterCreateHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterCreate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var createResult = await _commandService.CreateAsync(
            entity,
            values,
            beforeCreateHooks,
            afterCreateHooks,
            User.Identity?.Name);
        if (!createResult.Ok)
        {
            errors["__hook"] = createResult.Error?.Message ?? "前処理によりキャンセルされました。";
            return await RenderFormWithErrorsAsync(entity, meta, mode, form, errors);
        }
        if (isPageMode)
            return Redirect(returnUrl ?? Url.Action(nameof(Index), new { entity })!);

        return await ReturnListAfterSaveAsync(entity, meta, returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string entity, string? id = null, [FromForm] Dictionary<string, string?>? form = null, [FromForm] IFormFileCollection? files = null, string mode = "modal", [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 更新処理。登録同様に監査ログと整合性を取るためTx内で実行します。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        
        var pkColumns = meta.GetPrimaryKeyColumns();
        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        
        form ??= new Dictionary<string, string?>();
        var (values, errors) = await _formValidationService.ConvertAndValidateAsync(meta, form, _projectScope.Current?.Name ?? "DefaultProject");
        var isPageMode = mode.Equals("page", StringComparison.OrdinalIgnoreCase);

        if (errors.Any())
            return await RenderFormWithErrorsAsync(entity, meta, mode, form, errors, keyValue);

        var beforeUpdateHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeUpdate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterUpdateHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterUpdate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var updateResult = await _commandService.UpdateAsync(
            entity,
            pkColumns[0],
            keyValue,
            values,
            beforeUpdateHooks,
            afterUpdateHooks,
            User.Identity?.Name);
        if (!updateResult.Ok)
        {
            errors["__hook"] = updateResult.Error?.Message ?? "前処理によりキャンセルされました。";
            return await RenderFormWithErrorsAsync(entity, meta, mode, form, errors, keyValue);
        }
        if (isPageMode)
            return Redirect(returnUrl ?? Url.Action(nameof(Index), new { entity })!);

        return await ReturnListAfterSaveAsync(entity, meta, returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string entity, string? id = null, [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 削除処理（論理/物理はRepository側で自動分岐）。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        
        var pkColumns = meta.GetPrimaryKeyColumns();
        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);

        var beforeDeleteHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeDelete, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterDeleteHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterDelete, msg => _logger.LogWarning("{Message}", msg)) : null;
        var deleteResult = await _commandService.DeleteAsync(
            entity,
            pkColumns[0],
            keyValue,
            beforeDeleteHooks,
            afterDeleteHooks,
            User.Identity?.Name);
        if (!deleteResult.Ok)
        {
            if (_commandErrorHttpMapper.IsConflict(deleteResult.Error))
            {
                return Conflict(deleteResult.Error?.Message ?? "対象データが更新済みか、既に削除されています。");
            }

            return BadRequest(deleteResult.Error?.Message ?? "前処理により削除がキャンセルされました。");
        }
        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        return PartialView("_List",
            CreateListViewModel(
                entity,
                meta,
                items,
                null,
                null,
                null,
                new(),
                1,
                total,
                null,
                5,
                true,
                false,
                null,
                null,
                returnUrl));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete(string entity, [FromForm] List<string>? ids = null, [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null) return accessDenied;

        var pkColumns = meta.GetPrimaryKeyColumns();
        var pkCol = pkColumns[0];
        var beforeDeleteHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeDelete, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterDeleteHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterDelete, msg => _logger.LogWarning("{Message}", msg)) : null;

        var errors = new List<string>();
        foreach (var idVal in ids ?? [])
        {
            var result = await _commandService.DeleteAsync(entity, pkCol, idVal, beforeDeleteHooks, afterDeleteHooks, User.Identity?.Name);
            if (!result.Ok)
                errors.Add(result.Error?.Message ?? idVal);
        }

        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        return PartialView("_List",
            CreateListViewModel(entity, meta, items, null, null, null, new(), 1, total, null, 5, true, false, null, null, returnUrl));
    }
}
