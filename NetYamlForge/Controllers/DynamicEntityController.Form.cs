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
    public async Task<IActionResult> CreateForm(string entity = "customer", string mode = "modal")
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 新規作成フォームの描画（モーダル/ページ両対応）。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var vm = _formVmFactory.Build(entity, meta, null, fkData, mode: mode);
        return PartialView("_Form", vm);
    }

    public async Task<IActionResult> CreatePage(string entity = "customer", string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var breadcrumbs = _navigationService.BuildBreadcrumbChain(returnUrl);
        var vm = _formVmFactory.Build(entity, meta, null, fkData, mode: "page", breadcrumbChain: breadcrumbs);
        return View("FormPage", vm);
    }

    public async Task<IActionResult> EditForm(string entity, string? id = null, string mode = "modal")
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        // 既存レコードを読み込み、編集フォームを返します。
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        
        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        
        var item = await _repo.GetByIdAsync(entity, keyValue ?? "");
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var vm = _formVmFactory.Build(entity, meta, item, fkData, mode: mode);
        return PartialView("_Form", vm);
    }

    public async Task<IActionResult> DetailPage(string entity, string? id = null, string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }

        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);

        var item = await _repo.GetByIdAsync(entity, keyValue ?? "");
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var breadcrumbs = _navigationService.BuildBreadcrumbChain(returnUrl);
        var vm = new DynamicDetailViewModel(entity, meta, item, fkData, breadcrumbs, returnUrl);

        if (entity.Equals("document_task", StringComparison.OrdinalIgnoreCase) && item != null)
        {
            var itemDict = item as IDictionary<string, object>;
            if (itemDict != null)
            {
                // Case-insensitive key lookups
                var tblKey = itemDict.Keys.FirstOrDefault(k => k.Equals("ExtractedTable", StringComparison.OrdinalIgnoreCase));
                var extIdKey = itemDict.Keys.FirstOrDefault(k => k.Equals("ExtractedId", StringComparison.OrdinalIgnoreCase));
                var jsonPathKey = itemDict.Keys.FirstOrDefault(k => k.Equals("JsonPath", StringComparison.OrdinalIgnoreCase));

                if (tblKey != null && extIdKey != null &&
                    itemDict.TryGetValue(tblKey, out var tbl) && tbl != null &&
                    itemDict.TryGetValue(extIdKey, out var extId) && extId != null)
                {
                    var tableStr = tbl.ToString();
                    var idStr = extId.ToString();
                    if (!string.IsNullOrWhiteSpace(tableStr) && !string.IsNullOrWhiteSpace(idStr) && int.TryParse(idStr, out var intId) && intId > 0)
                    {
                        var cleanTable = new string(tableStr.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                        if (!string.IsNullOrEmpty(cleanTable))
                        {
                            try
                            {
                                var selectStatement = string.Format("SELECT * FROM \"{0}\" WHERE Id = @Id", cleanTable);
                                var extRow = await _db.QueryFirstOrDefaultAsync<dynamic>(selectStatement, new { Id = intId });
                                if (extRow != null)
                                {
                                    ViewBag.ExtractedData = extRow;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to load extracted dynamic data from {Table} with ID {Id}", cleanTable, idStr);
                            }
                        }
                    }
                }

                if (jsonPathKey != null && itemDict.TryGetValue(jsonPathKey, out var jsonPathObj) && jsonPathObj != null)
                {
                    var jsonRelativePath = jsonPathObj.ToString();
                    if (!string.IsNullOrWhiteSpace(jsonRelativePath))
                    {
                        try
                        {
                            var jsonAbsolutePath = Path.Combine(_env.WebRootPath, jsonRelativePath.TrimStart('/'));
                            if (System.IO.File.Exists(jsonAbsolutePath))
                            {
                                var rawJson = await System.IO.File.ReadAllTextAsync(jsonAbsolutePath);
                                using var jsonDoc = JsonDocument.Parse(rawJson);
                                var formattedJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
                                ViewBag.RawJson = formattedJson;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load raw JSON from path: {Path}", jsonRelativePath);
                        }
                    }
                }
            }
        }

        return View("DetailPage", vm);
    }

    public async Task<IActionResult> EditPage(string entity, string? id = null, string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "customer";

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
        {
            return accessDenied;
        }
        
        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        
        var item = await _repo.GetByIdAsync(entity, keyValue ?? "");
        var fkData = await _foreignKeyDataService.LoadForFormAsync(meta);
        var breadcrumbs = _navigationService.BuildBreadcrumbChain(returnUrl);
        var vm = _formVmFactory.Build(entity, meta, item, fkData, mode: "page", breadcrumbChain: breadcrumbs);
        return View("FormPage", vm);
    }
}
