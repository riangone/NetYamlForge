// ファイル概要: カスタムページ機能のコントローラー。
// pages/*.yaml で定義したページを /{project}/Page/{pageName} でレンダリングします。
// セクションの行レベル更新・削除も担当します。

using System.Security.Claims;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace NetYamlForge.Controllers;

[Route("{project}/Page")]
public class PageController : BaseProjectController
{
    /// <summary>閲覧・更新ともに Admin 専用のページ名セット。</summary>
    private static readonly HashSet<string> AdminOnlyPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApprovalInbox",
        "AssignmentRules",
        "DuplicateRules",
        "AutomationRules",
        "DataImportExport",
        "ObjectManager",
        "RoleAccessMatrix",
        "UserRoleProfile",
        "AuditTrail",
        "AuditMetrics",
        "IntegrationHub",
        "WebhookDeliveryMonitor"
    };
    private static readonly HashSet<string> AdminOnlyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "IsAdmin",
        "IsActive",
        "PreferredLanguage",
        "PasswordHash",
        "Role"
    };

    private readonly ProjectScope _projectScope;
    private readonly IAuditLogService _audit;
    private readonly IPagePermissionService _pagePermission;
    private readonly PageRowMutationService _rowMutationService;
    private readonly PageDataQueryService _pageDataQueryService;
    private readonly PageViewPreferenceService _pageViewPreferenceService;
    private readonly SectionRowFormViewModelFactory _formViewModelFactory;
    private readonly ILogger<PageController> _logger;

    public PageController(
        ProjectScope projectScope,
        IAuditLogService audit,
        IPagePermissionService pagePermission,
        PageRowMutationService rowMutationService,
        PageDataQueryService pageDataQueryService,
        PageViewPreferenceService pageViewPreferenceService,
        SectionRowFormViewModelFactory formViewModelFactory,
        ILogger<PageController> logger)
    {
        _projectScope = projectScope;
        _audit = audit;
        _pagePermission = pagePermission;
        _rowMutationService = rowMutationService;
        _pageDataQueryService = pageDataQueryService;
        _pageViewPreferenceService = pageViewPreferenceService;
        _formViewModelFactory = formViewModelFactory;
        _logger = logger;
    }

    // GET /{project}/Page/{pageName}
    [HttpGet("{pageName}")]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string project, string pageName)
    {
        var proj = _projectScope.Current;

        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound($"ページ '{pageName}' が見つかりません。");

        // 公開ページでない場合は認証チェック
        // Note: IsPublic may not exist in all PageDefinition implementations
        var isPublic = pageDef.GetType().GetProperty("IsPublic")?.GetValue(pageDef) as bool? ?? false;
        if (!isPublic)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Challenge(); // ログインページへリダイレクト
            }

            if (AdminOnlyPages.Contains(pageName) && !UserIsAdmin())
                return Forbid();
            if (!await _pagePermission.CanReadPageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
                return Forbid();
        }

        var filters = Request.Query
            .ToDictionary(k => k.Key, v => v.Value.ToString());
        var savedViews = await LoadSavedViewsAsync(pageName);
        if (filters.Count == 0)
        {
            var defaultView = savedViews.FirstOrDefault(v =>
                v.TryGetValue("IsDefault", out var isDefault) && isDefault == "1");
            if (defaultView != null &&
                defaultView.TryGetValue("Url", out var defaultUrl) &&
                !string.IsNullOrWhiteSpace(defaultUrl) &&
                defaultUrl.Contains('?', StringComparison.Ordinal))
            {
                return Redirect(defaultUrl);
            }
        }

        var model = await _pageDataQueryService.LoadPageDataAsync(pageDef, filters);

        ViewData["PageDef"] = pageDef;
        ViewData["PageName"] = pageName;
        ViewData["Project"] = proj.Name;
        ViewData["Title"] = pageDef.Title;
        ViewData["ProjectCalendar"] = proj.Calendar;
        ViewData["IsAdmin"] = UserIsAdmin();
        ViewData["SavedViews"] = savedViews;

        // プロジェクト固有のビューを検索
        var projectViewPath = Path.Combine(proj.ProjectDir, "views", pageName + ".cshtml");
        if (System.IO.File.Exists(projectViewPath))
        {
            // プロジェクト固有のビューを使用（Layout もプロジェクト固有のものを使用）
            var layoutPath = $"/projects/{proj.Name}/views/_Layout.cshtml";
            ViewBag.LayoutPath = System.IO.File.Exists(Path.Combine(proj.ProjectDir, "views", "_Layout.cshtml"))
                ? layoutPath
                : "_Layout";
            return View($"/projects/{proj.Name}/views/{pageName}.cshtml", model);
        }

        // ProjectViewLocationExpander が projects/{project}/views/{template}.cshtml を解決する
        if (!string.IsNullOrEmpty(pageDef.Template))
        {
            var templatePath = Path.Combine(proj.ProjectDir, "views", pageDef.Template + ".cshtml");
            if (System.IO.File.Exists(templatePath))
            {
                var layoutPath = $"/projects/{proj.Name}/views/_Layout.cshtml";
                ViewBag.LayoutPath = System.IO.File.Exists(Path.Combine(proj.ProjectDir, "views", "_Layout.cshtml"))
                    ? layoutPath
                    : "_Layout";
                return View($"/projects/{proj.Name}/views/{pageDef.Template}.cshtml", model);
            }
        }

        return View("PageView", model);
    }

    // GET /{project}/Page/{pageName}/section/{sectionId}  ── HTMX セクション部分更新
    [Authorize]
    [HttpGet("{pageName}/section/{sectionId}")]
    public async Task<IActionResult> SectionTable(string project, string pageName, string sectionId)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null)
            return NotFound();

        var filters = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());
        return PartialView("Components/_SectionTable",
            await BuildSectionRenderModelAsync(proj, section, pageName, filters));
    }

    // GET /{project}/Page/{pageName}/section/{sectionId}/row-form
    [Authorize]
    [HttpGet("{pageName}/section/{sectionId}/row-form")]
    public async Task<IActionResult> SectionRowForm(string project, string pageName, string sectionId, [FromQuery] string? rowId)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null || !section.Editable || section.ReadOnly || string.IsNullOrEmpty(section.TargetTable))
            return Forbid();

        Dictionary<string, object>? row = null;
        if (!string.IsNullOrEmpty(rowId) && int.TryParse(rowId, out var rowIdInt))
            row = await _pageDataQueryService.GetRowByIdAsync(section, rowIdInt);

        return PartialView("Components/_SectionRowForm", _formViewModelFactory.BuildEdit(section, row, proj.Name, pageName));
    }

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

        var result = await _rowMutationService.InsertRowAsync(proj.Name, section, fields);
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

        var result = await _rowMutationService.UpdateAllFieldsAsync(proj.Name, section, rowIdInt, fields);
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

        var deleteResult = await _rowMutationService.DeleteRowAsync(proj.Name, section, rowIdInt);
        if (!deleteResult.ok)
            return BadRequest(deleteResult.message ?? "削除ルール違反です。");
        await TryWritePageAuditAsync(
            action: "page_delete",
            entity: section.TargetTable,
            detail: $"Page={pageName},Section={sectionId},RowId={rowIdInt}");

        return await ReturnSectionOrRedirectAsync(proj, section, pageName, sectionId);
    }

    private async Task TryWritePageAuditAsync(string action, string? entity, string detail)
    {
        try
        {
            await _audit.WriteAsync(action, entity, detail, User.Identity?.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit write failed for action={Action}, entity={Entity}", action, entity);
        }
    }

    private async Task<List<Dictionary<string, string>>> LoadSavedViewsAsync(string pageName)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return new List<Dictionary<string, string>>();

        try
        {
            var rows = await _pageViewPreferenceService.LoadSavedViewsAsync(
                _projectScope.Current.Name,
                pageName,
                userName);

            var result = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                var url = QueryHelpers.AddQueryString(
                    $"/{_projectScope.Current.Name}/Page/{pageName}",
                    row.Filters.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                        .ToDictionary(kv => kv.Key, kv => kv.Value));

                result.Add(new Dictionary<string, string>
                {
                    ["ViewName"] = row.ViewName,
                    ["Url"] = url,
                    ["IsDefault"] = row.IsDefault ? "1" : "0"
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LoadSavedViews skipped for page={Page}", pageName);
            return new List<Dictionary<string, string>>();
        }
    }

    /// <summary>フォームから allowed フィールドのみを抽出する。__RequestVerificationToken は自動除外。</summary>
    private Dictionary<string, string> FilterFormFields(HashSet<string> allowed)
        => Request.Form
            .Where(kv => allowed.Contains(kv.Key) &&
                         !string.Equals(kv.Key, "__RequestVerificationToken", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

    private async Task<SectionRenderModel> BuildSectionRenderModelAsync(
        ProjectInfo proj,
        SectionDefinition section,
        string pageName,
        IDictionary<string, string> allFilters)
    {
        var (rows, total) = await _pageDataQueryService.LoadSectionDataAsync(section, allFilters);
        return new SectionRenderModel
        {
            Sec = section,
            Rows = rows.ToList(),
            Total = total,
            Project = proj.Name,
            PageName = pageName,
            AllQueryParams = allFilters
        };
    }

    /// <summary>
    /// HTMX リクエストならセクションを部分更新して返し、通常リクエストならページ全体にリダイレクトする。
    /// InsertRow / UpdateAllFields / DeleteRow で共通して使用する。
    /// </summary>
    private async Task<IActionResult> ReturnSectionOrRedirectAsync(
        ProjectInfo proj, SectionDefinition section, string pageName, string sectionId)
    {
        if (IsHtmxRequest())
        {
            Response.Headers["HX-Retarget"] = $"#section-{sectionId}";
            Response.Headers["HX-Reswap"] = "innerHTML";
            return PartialView("Components/_SectionTable",
                await BuildSectionRenderModelAsync(proj, section, pageName, GetFiltersFromHtmxCurrentUrl()));
        }
        return Redirect($"/{proj.Name}/Page/{pageName}");
    }

    private static bool IsAdminOnlyMutation(string pageName, string? targetTable)
    {
        if (AdminOnlyPages.Contains(pageName))
            return true;

        return string.Equals(targetTable, "AppUser", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetTable, "AuditLog", StringComparison.OrdinalIgnoreCase);
    }

}
