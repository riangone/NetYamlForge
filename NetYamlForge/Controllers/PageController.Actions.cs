using System.Data;
using System.Security.Claims;
using System.Linq;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

public partial class PageController
{
    // POST /{project}/Page/{pageName}/section/{sectionId}/insert-row
    [Authorize]
    [HttpPost("{pageName}/section/{sectionId}/insert-row")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InsertRow(string project, string pageName, string sectionId)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound();
        if (!await _pagePermission.CanWritePageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null || !section.Editable || section.ReadOnly || string.IsNullOrEmpty(section.TargetTable))
            return Forbid();

        var allowed = new HashSet<string>(section.GetFormFields("create"), StringComparer.OrdinalIgnoreCase);
        var fields = FilterFormFields(allowed);

        var result = await _rowMutationService.InsertRowAsync(proj.Name, section, fields, User.Identity?.Name);
        if (!result.ok)
        {
            return PartialView("Components/_SectionRowForm", _formViewModelFactory.BuildWithError(
                section, null, proj.Name, pageName,
                result.message ?? "挿入に失敗しました。",
                fields.ToDictionary(kv => kv.Key, kv => (string?)kv.Value)));
        }

        await TryWritePageAuditAsync("page_insert", section.TargetTable, $"Page={pageName},Section={sectionId}");
        return await ReturnSectionOrRedirectAsync(proj, section, pageName, sectionId);
    }

    // POST /{project}/Page/{pageName}/section/{sectionId}/update-all-fields
    [Authorize]
    [HttpPost("{pageName}/section/{sectionId}/update-all-fields")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAllFields(
        string project, string pageName, string sectionId, [FromForm] string rowId)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound();
        if (!await _pagePermission.CanWritePageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null || !section.Editable || section.ReadOnly || string.IsNullOrEmpty(section.TargetTable))
            return Forbid();

        if (!int.TryParse(rowId, out var rowIdInt))
            return BadRequest("無効な行 ID です。");

        var allowed = new HashSet<string>(section.GetFormFields("edit"), StringComparer.OrdinalIgnoreCase);
        var fields = FilterFormFields(allowed);

        var result = await _rowMutationService.UpdateAllFieldsAsync(proj.Name, section, rowIdInt, fields, User.Identity?.Name);
        if (!result.ok)
        {
            var pkCol = section.TargetPrimaryKey ?? "id";
            var errorRow = new Dictionary<string, object> { [pkCol] = rowIdInt };
            return PartialView("Components/_SectionRowForm", _formViewModelFactory.BuildWithError(
                section, errorRow, proj.Name, pageName,
                result.message ?? "更新に失敗しました。",
                fields.ToDictionary(kv => kv.Key, kv => (string?)kv.Value)));
        }

        await TryWritePageAuditAsync("page_update_all", section.TargetTable, $"Page={pageName},Section={sectionId},RowId={rowIdInt}");
        return await ReturnSectionOrRedirectAsync(proj, section, pageName, sectionId);
    }

    // POST /{project}/Page/{pageName}/section/{sectionId}/update-row
    [Authorize]
    [HttpPost("{pageName}/section/{sectionId}/update-row")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRow(
        string project, string pageName, string sectionId,
        [FromForm] string rowId,
        [FromForm] string field,
        [FromForm] string value)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound($"ページ '{pageName}' が見つかりません。");
        if (!await _pagePermission.CanWritePageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null)
            return NotFound($"セクション '{sectionId}' が見つかりません。");

        if (IsAdminOnlyMutation(pageName, section.TargetTable) && !UserIsAdmin())
            return Forbid();

        if (string.IsNullOrEmpty(section.TargetTable) || string.IsNullOrEmpty(section.TargetPrimaryKey))
            return BadRequest("target_table または target_primary_key が設定されていません。");

        if (!int.TryParse(rowId, out var rowIdInt))
            return BadRequest("無効な行 ID です。");

        var allowed = (IReadOnlyList<string>?)section.UpdatableFields ?? section.Columns.Keys.ToList();
        if (!allowed.Contains(field, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"フィールド '{field}' は更新できません。");

        if (AdminOnlyFields.Contains(field) && !UserIsAdmin())
            return Forbid();
        if (!await _pagePermission.CanWriteFieldAsync(proj.Name, pageName, field, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var updateResult = await _rowMutationService.UpdateRowAsync(
            proj.Name,
            section,
            rowIdInt,
            field,
            value,
            User.Identity?.Name,
            User.FindFirst(ClaimTypes.GivenName)?.Value);
        if (!updateResult.ok)
            return BadRequest(updateResult.message ?? "更新ルール違反です。");

        await TryWritePageAuditAsync(
            action: "page_update",
            entity: section.TargetTable,
            detail: $"Page={pageName},Section={sectionId},Field={field},RowId={rowIdInt},Value={value}");

        return Content("", "text/html");
    }

    // POST /{project}/Page/{pageName}/section/{sectionId}/delete-row
    [Authorize]
    [HttpPost("{pageName}/section/{sectionId}/delete-row")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRow(
        string project, string pageName, string sectionId,
        [FromForm] string rowId)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound($"ページ '{pageName}' が見つかりません。");
        if (!await _pagePermission.CanWritePageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null)
            return NotFound($"セクション '{sectionId}' が見つかりません。");

        if (IsAdminOnlyMutation(pageName, section.TargetTable) && !UserIsAdmin())
            return Forbid();

        if (string.IsNullOrEmpty(section.TargetTable) || string.IsNullOrEmpty(section.TargetPrimaryKey))
            return BadRequest("target_table または target_primary_key が設定されていません。");

        if (!int.TryParse(rowId, out var rowIdInt))
            return BadRequest("無効な行 ID です。");

        var deleteResult = await _rowMutationService.DeleteRowAsync(proj.Name, section, rowIdInt, User.Identity?.Name);
        if (!deleteResult.ok)
            return BadRequest(deleteResult.message ?? "削除ルール違反です。");
        await TryWritePageAuditAsync(
            action: "page_delete",
            entity: section.TargetTable,
            detail: $"Page={pageName},Section={sectionId},RowId={rowIdInt}");

        return await ReturnSectionOrRedirectAsync(proj, section, pageName, sectionId);
    }

    [HttpGet("{pageName}/action/{actionName}")]
    public async Task<IActionResult> DispatchAction(string project, string pageName, string actionName)
    {
        var queryDict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Request.Query.Keys)
        {
            queryDict[key] = Request.Query[key].ToString();
        }
        var ctx = new PageActionContext(project, pageName, queryDict, HttpContext.RequestServices, User, HttpContext);
        
        var result = await _pageActionDispatcher.DispatchAsync(project, pageName, actionName, ctx);
        if (result == null)
        {
            return NotFound($"未找到动作处理器: {actionName}");
        }
        return result;
    }

    // GET /{project}/Page/{pageName}/switch-provider?type=annotation&provider=lmstudio
    [Authorize]
    [HttpGet("{pageName}/switch-provider")]
    [Obsolete("Use /action/switch-provider instead")]
    public async Task<IActionResult> SwitchProvider(string project, string pageName, string type, string provider)
    {
        _logger.LogWarning("SwitchProvider is obsolete. Please use /action/switch-provider instead.");
        return await DispatchAction(project, pageName, "switch-provider");
    }

    // GET /{project}/Page/{pageName}/annotate-photo?photo_id=xxx
    [HttpGet("{pageName}/annotate-photo")]
    [Obsolete("Use /action/annotate-photo instead")]
    public async Task<IActionResult> AnnotatePhoto(string project, string pageName, [FromQuery] string photo_id)
    {
        _logger.LogWarning("AnnotatePhoto is obsolete. Please use /action/annotate-photo instead.");
        return await DispatchAction(project, pageName, "annotate-photo");
    }

    // GET /{project}/Page/{pageName}/embed-photo?photo_id=xxx
    [HttpGet("{pageName}/embed-photo")]
    [Obsolete("Use /action/embed-photo instead")]
    public async Task<IActionResult> EmbedPhoto(string project, string pageName, [FromQuery] string photo_id)
    {
        _logger.LogWarning("EmbedPhoto is obsolete. Please use /action/embed-photo instead.");
        return await DispatchAction(project, pageName, "embed-photo");
    }

    [Authorize]
    [HttpPost("{pageName}/save-view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveView(string project, string pageName, [FromForm] string viewName, [FromForm] string? isDefault)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            return BadRequest("ビュー名は必須です。");

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Forbid();
        if (!await _pagePermission.CanWritePageAsync(_projectScope.Current.Name, pageName, userName, UserIsAdmin()))
            return Forbid();

        var filters = Request.Form
            .Where(kv =>
                !string.Equals(kv.Key, "__RequestVerificationToken", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kv.Key, "viewName", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kv.Key, "isDefault", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(kv.Value.ToString()))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        var makeDefault = string.Equals(isDefault, "true", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(isDefault, "on", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(isDefault, "1", StringComparison.OrdinalIgnoreCase);

        var result = await _pageViewPreferenceService.SaveViewAsync(
            _projectScope.Current.Name,
            pageName,
            userName,
            viewName,
            filters,
            makeDefault);
        if (!result.ok)
            return BadRequest(result.message ?? "ビュー保存に失敗しました。");

        return RedirectToAction(nameof(Index), new { project = _projectScope.Current.Name, pageName });
    }

    [Authorize]
    [HttpPost("{pageName}/delete-view")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteView(string project, string pageName, [FromForm] string viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            return BadRequest("ビュー名は必須です。");

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Forbid();
        if (!await _pagePermission.CanWritePageAsync(_projectScope.Current.Name, pageName, userName, UserIsAdmin()))
            return Forbid();

        await _pageViewPreferenceService.DeleteViewAsync(
            _projectScope.Current.Name,
            pageName,
            userName,
            viewName);

        await TryWritePageAuditAsync(
            action: "page_view_delete",
            entity: pageName,
            detail: $"Deleted page view. page={pageName}, view={viewName.Trim()}");

        return RedirectToAction(nameof(Index), new { project = _projectScope.Current.Name, pageName });
    }
}
