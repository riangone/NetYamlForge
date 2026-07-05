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
    /// <summary>
    /// アクションの file 型 inputs に対応するアップロードファイルを一時ディレクトリに保存します。
    /// 成功した場合は (inputName → 一時ファイル絶対パス) の辞書を返します。
    /// バリデーションエラーがある場合はエラーメッセージを返します。
    /// </summary>
    private static async Task<(Dictionary<string, string> Files, Dictionary<string, List<string>> MultipleFiles, string? Error)> SaveActionUploadedFilesAsync(
        NetYamlForge.Models.ActionDefinition actionDef,
        IFormFileCollection formFiles)
    {
        var saved = new Dictionary<string, string>();
        var savedMultiple = new Dictionary<string, List<string>>();
        if (actionDef.Inputs == null) return (saved, savedMultiple, null);

        foreach (var input in actionDef.Inputs.Where(i => i.Type == "file"))
        {
            var files = formFiles.GetFiles(input.Name);
            if (files == null || files.Count == 0)
            {
                if (input.Required)
                    return (saved, savedMultiple, $"'{input.Label ?? input.Name}' は必須です。");
                continue;
            }

            var pathList = new List<string>();
            foreach (var formFile in files)
            {
                if (formFile.Length == 0) continue;

                // サイズ検証
                var maxBytes = input.MaxSizeBytes ?? 10L * 1024 * 1024;
                if (formFile.Length > maxBytes)
                    return (saved, savedMultiple, $"'{input.Label ?? input.Name}' のファイルサイズが上限（{maxBytes / 1024 / 1024} MB）を超えています。");

                // 拡張子検証
                if (!string.IsNullOrWhiteSpace(input.AllowedExtensions))
                {
                    var allowed = input.AllowedExtensions
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(e => e.TrimStart('.').ToLowerInvariant())
                        .ToHashSet();
                    var ext = Path.GetExtension(formFile.FileName).TrimStart('.').ToLowerInvariant();
                    if (!allowed.Contains(ext))
                        return (saved, savedMultiple, $"'{input.Label ?? input.Name}' の拡張子 .{ext} は許可されていません（許可: {input.AllowedExtensions}）。");
                }

                // 一時ファイルに保存
                var tempDir = Path.Combine(Path.GetTempPath(), "netyamlforge_uploads");
                Directory.CreateDirectory(tempDir);
                var safeFilename = $"{Guid.NewGuid():N}_{Path.GetFileName(formFile.FileName)}";
                var tempPath = Path.Combine(tempDir, safeFilename);

                await using var fs = System.IO.File.Create(tempPath);
                await formFile.CopyToAsync(fs);

                pathList.Add(tempPath);
            }

            if (pathList.Count == 0 && input.Required)
            {
                return (saved, savedMultiple, $"'{input.Label ?? input.Name}' は必須です。");
            }

            if (pathList.Count > 0)
            {
                saved[input.Name] = pathList[0];
                savedMultiple[input.Name] = pathList;
            }
        }

        return (saved, savedMultiple, null);
    }

    /// <summary>アクション実行後に一時ファイルを削除します。</summary>
    private void CleanupTempFiles(Dictionary<string, string> files, Dictionary<string, List<string>>? multipleFiles = null)
    {
        var allPaths = new HashSet<string>();
        foreach (var path in files.Values)
        {
            allPaths.Add(path);
        }
        if (multipleFiles != null)
        {
            foreach (var paths in multipleFiles.Values)
            {
                foreach (var path in paths)
                {
                    allPaths.Add(path);
                }
            }
        }
        foreach (var path in allPaths)
        {
            try { System.IO.File.Delete(path); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "一時ファイルの削除に失敗しました: {Path}", path);
            }
        }
    }

    /// <summary>
    /// ヘッダーアクション入力フォームをモーダル用パーシャルとして返します。
    /// </summary>
    public IActionResult HeaderActionForm(string entity, string actionKey)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        return PartialView("_ActionForm", new ActionFormViewModel(entity, actionKey, actionDef, null));
    }

    /// <summary>
    /// ヘッダーアクション（行に依存しない操作）を実行し、一覧パーシャルを返します。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InvokeHeaderAction(
        string entity,
        string actionKey,
        [FromForm] Dictionary<string, string?> form = null!,
        [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        form ??= new Dictionary<string, string?>();

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        var projectName = _projectScope.Current?.Name ?? "";
        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
            return BadRequest($"アクションハンドラー '{handlerName}' が見つかりません。");

        var inputs = form
            .Where(kv => kv.Key != "__RequestVerificationToken")
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        // ファイル入力を一時領域に保存する
        Dictionary<string, string> savedFiles;
        Dictionary<string, List<string>> savedMultipleFiles;
        try
        {
            var (sFiles, sMultipleFiles, fileError) = await SaveActionUploadedFilesAsync(actionDef, Request.Form.Files);
            if (fileError != null)
                return BadRequest(fileError);
            savedFiles = sFiles;
            savedMultipleFiles = sMultipleFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存上传文件时发生异常");
            return BadRequest($"上传文件处理失败：{ex.Message}");
        }

        var ctx = new CustomActionContext
        {
            Project = projectName,
            Entity = entity,
            Action = actionKey,
            RecordId = null,
            Inputs = inputs,
            Files = savedFiles,
            MultipleFiles = savedMultipleFiles,
            UserName = User.Identity?.Name
        };

        ActionHandlerResult result;
        try
        {
            result = await _commandService.ExecuteActionAsync(handler, ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ヘッダーアクション '{Action}' の実行中にエラーが発生しました", actionKey);
            return StatusCode(500, "アクションの実行中にエラーが発生しました。");
        }
        finally
        {
            CleanupTempFiles(savedFiles, savedMultipleFiles);
        }

        if (!result.Ok)
            return BadRequest(result.ErrorMessage ?? "アクションが失敗しました。");

        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        _listHttpResponseService.SetEntityFormSavedHeaders(Response);
        return PartialView("_List",
            CreateListViewModel(entity, meta, items, null, null, null, new(), 1, total, null, 5, true, false, null, null, returnUrl));
    }

    /// <summary>
    /// 複数レコードに対して同一アクションを一括実行します。
    /// 各 ID ごとにハンドラーを呼び出し、すべて成功した場合のみコミットします。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InvokeBulkAction(
        string entity,
        string actionKey,
        [FromForm] string[] ids,
        [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        if (ids == null || ids.Length == 0)
            return BadRequest("操作するレコードが指定されていません。");

        var projectName = _projectScope.Current?.Name ?? "";
        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
            return BadRequest($"アクションハンドラー '{handlerName}' が見つかりません。");

        var errors = new List<string>();
        foreach (var id in ids)
        {
            var ctx = new CustomActionContext
            {
                Project = projectName,
                Entity = entity,
                Action = actionKey,
                RecordId = id,
                Inputs = new Dictionary<string, object?>(),
                UserName = User.Identity?.Name
            };

            try
            {
                var result = await _commandService.ExecuteActionAsync(handler, ctx);
                if (!result.Ok)
                    errors.Add($"ID={id}: {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "一括アクション '{Action}' ID={Id} の実行中にエラーが発生しました", actionKey, id);
                errors.Add($"ID={id}: 実行エラー");
            }
        }

        if (errors.Count > 0)
            _logger.LogWarning("一括アクション '{Action}' の一部が失敗しました: {Errors}", actionKey, string.Join("; ", errors));

        var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
        _listHttpResponseService.SetEntityFormSavedHeaders(Response);
        return PartialView("_List",
            CreateListViewModel(entity, meta, items, null, null, null, new(), 1, total, null, 5, true, false, null, null, returnUrl));
    }

    /// <summary>
    /// カスタムアクション入力フォームをモーダル用パーシャルとして返します。
    /// inputs が空のアクションは確認ダイアログを表示せずそのまま InvokeAction を呼びます。
    /// </summary>
    public IActionResult ActionForm(string entity, string actionKey, string? id = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        return PartialView("_ActionForm", new ActionFormViewModel(entity, actionKey, actionDef, keyValue));
    }

    /// <summary>
    /// カスタムアクションを実行し、一覧パーシャルを返します。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InvokeAction(
        string entity,
        string actionKey,
        string? id = null,
        [FromForm] Dictionary<string, string?> form = null!,
        [FromForm] string? returnUrl = null)
    {
        entity = NormalizeSingleValue(entity) ?? "";
        form ??= new Dictionary<string, string?>();

        var meta = _meta.Get(entity);
        var accessDenied = RejectIfNotVisible(meta);
        if (accessDenied != null)
            return accessDenied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound($"アクション '{actionKey}' が見つかりません。");

        var keyValue = _keyResolver.ResolvePrimaryKeyValue(meta, id, Request.Query);
        var projectName = _projectScope.Current?.Name ?? "";

        // ハンドラー名を解決（省略時はアクションキー名）
        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
        {
            _logger.LogWarning(
                "プロジェクト '{Project}' のアクションハンドラー '{Handler}' が見つかりません",
                projectName, handlerName);
            return BadRequest($"アクションハンドラー '{handlerName}' が見つかりません。");
        }

        // 入力値を object? 辞書に変換
        var inputs = form
            .Where(kv => kv.Key != "__RequestVerificationToken")
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

        // ファイル入力を一時領域に保存する
        Dictionary<string, string> savedFiles;
        Dictionary<string, List<string>> savedMultipleFiles;
        try
        {
            var (sFiles, sMultipleFiles, fileError) = await SaveActionUploadedFilesAsync(actionDef, Request.Form.Files);
            if (fileError != null)
                return BadRequest(fileError);
            savedFiles = sFiles;
            savedMultipleFiles = sMultipleFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存上传文件时发生异常");
            return BadRequest($"上传文件处理失败：{ex.Message}");
        }

        var ctx = new NetYamlForge.Services.Hooks.CustomActionContext
        {
            Project = projectName,
            Entity = entity,
            Action = actionKey,
            RecordId = keyValue,
            Inputs = inputs,
            Files = savedFiles,
            MultipleFiles = savedMultipleFiles,
            UserName = User.Identity?.Name
        };

        // before フックを実行（IEntityHook として）
        var beforeHooks = actionDef.Hooks?.Before;
        if (beforeHooks != null && beforeHooks.Count > 0)
        {
            var hookCtx = new NetYamlForge.Services.Hooks.EntityHookContext
            {
                Entity = entity,
                Operation = NetYamlForge.Services.Hooks.CrudOperation.CustomAction,
                Id = int.TryParse(keyValue, out var intId) ? intId : null,
                Values = inputs,
                UserName = User.Identity?.Name
            };
            var beforeResult = await _commandService.RunBeforeHooksForActionAsync(beforeHooks, hookCtx);
            if (beforeResult.Cancel)
                return BadRequest(beforeResult.CancelMessage ?? "前処理によりキャンセルされました。");
        }

        // ハンドラー実行（トランザクション付き）
        ActionHandlerResult result;
        try
        {
            result = await _commandService.ExecuteActionAsync(handler, ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アクション '{Action}' の実行中にエラーが発生しました", actionKey);
            return StatusCode(500, "アクションの実行中にエラーが発生しました。");
        }
        finally
        {
            CleanupTempFiles(ctx.Files, ctx.MultipleFiles);
        }

        if (!result.Ok)
        {
            if (Request.Headers.ContainsKey("HX-Request"))
                return BadRequest(result.ErrorMessage ?? "アクションが失敗しました。");

            TempData["ErrorMessage"] = result.ErrorMessage ?? "アクションが失敗しました。";
            return Redirect(returnUrl ?? Request.Headers["Referer"].ToString() ?? Url.Action("Index", new { entity }) ?? "/");
        }

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            var (items, total) = await _listResponseService.LoadFirstPageAfterMutationAsync(entity);
            _listHttpResponseService.SetEntityFormSavedHeaders(Response);
            return PartialView("_List",
                CreateListViewModel(entity, meta, items, null, null, null, new(), 1, total, null, 5, true, false, null, null, returnUrl));
        }

        TempData["SuccessMessage"] = "アクションが完了しました。";
        return Redirect(returnUrl ?? Request.Headers["Referer"].ToString() ?? Url.Action("DetailPage", new { entity, id = keyValue }) ?? "/");
    }
}
