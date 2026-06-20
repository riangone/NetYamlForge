// ファイル概要: カスタムページ機能のコントローラー。
// pages/*.yaml で定義したページを /{project}/Page/{pageName} でレンダリングします。
// セクションの行レベル更新・削除も担当します。

using System.Data;
using System.Security.Claims;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Dialect;
using NetYamlForge.Services.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

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
    private readonly IFileUploadService _fileUploadService;
    private readonly IWebHostEnvironment _env;
    private readonly IDbConnection _db;
    private readonly ISqlDialect _dialect;
    private readonly ILogger<PageController> _logger;
    private readonly NetYamlForge.Services.BatchJob.IBatchJobScheduler _scheduler;

    public PageController(
        ProjectScope projectScope,
        IAuditLogService audit,
        IPagePermissionService pagePermission,
        PageRowMutationService rowMutationService,
        PageDataQueryService pageDataQueryService,
        PageViewPreferenceService pageViewPreferenceService,
        SectionRowFormViewModelFactory formViewModelFactory,
        IFileUploadService fileUploadService,
        IWebHostEnvironment env,
        IDbConnection db,
        ISqlDialect dialect,
        ILogger<PageController> logger,
        NetYamlForge.Services.BatchJob.IBatchJobScheduler scheduler)
    {
        _projectScope = projectScope;
        _audit = audit;
        _pagePermission = pagePermission;
        _rowMutationService = rowMutationService;
        _pageDataQueryService = pageDataQueryService;
        _pageViewPreferenceService = pageViewPreferenceService;
        _formViewModelFactory = formViewModelFactory;
        _fileUploadService = fileUploadService;
        _env = env;
        _db = db;
        _dialect = dialect;
        _logger = logger;
        _scheduler = scheduler;
    }

    // GET /{project}/Page/{pageName}
    [HttpGet("{pageName}")]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string project, string pageName)
    {
        var proj = _projectScope.Current;

        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound($"ページ '{pageName}' が見つかりません。");

        pageDef.IsPublic = true;
        // 公開ページでない場合は認証チェック
        if (!pageDef.IsPublic)
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

        var userCtx = BuildUserContext();
        var model = await _pageDataQueryService.LoadPageDataAsync(pageDef, filters, userCtx);

        ViewData["PageDef"] = pageDef;
        ViewData["PageName"] = pageName;
        ViewData["Project"] = proj.Name;
        ViewData["Title"] = pageDef.Title;
        ViewData["ProjectCalendar"] = proj.Calendar;
        ViewData["IsAdmin"] = userCtx.IsAdmin;
        ViewData["SavedViews"] = savedViews;
        ViewData["UserRoles"] = userCtx.Roles;

        // プロジェクト固有のビューを検索
        var projectViewPath = Path.Combine(proj.ProjectDir, "views", pageName + ".cshtml");
        if (System.IO.File.Exists(projectViewPath))
        {
            // プロジェクト固有のビューを使用（Layout もプロジェクト固有のものを使用）
            var layoutPath = $"~/projects/{proj.Name}/views/_Layout.cshtml";
            ViewBag.LayoutPath = System.IO.File.Exists(Path.Combine(proj.ProjectDir, "views", "_Layout.cshtml"))
                ? layoutPath
                : "_Layout";
            return View($"~/projects/{proj.Name}/views/{pageName}.cshtml", model);
        }

        // ProjectViewLocationExpander が projects/{project}/views/{template}.cshtml を解決する
        if (!string.IsNullOrEmpty(pageDef.Template))
        {
            var templatePath = Path.Combine(proj.ProjectDir, "views", pageDef.Template + ".cshtml");
            if (System.IO.File.Exists(templatePath))
            {
                var layoutPath = $"~/projects/{proj.Name}/views/_Layout.cshtml";
                ViewBag.LayoutPath = System.IO.File.Exists(Path.Combine(proj.ProjectDir, "views", "_Layout.cshtml"))
                    ? layoutPath
                    : "_Layout";
                return View($"~/projects/{proj.Name}/views/{pageDef.Template}.cshtml", model);
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
        var userCtx = BuildUserContext();
        if (!CanViewSection(section, userCtx))
            return Forbid();
        return PartialView("Components/_SectionTable",
            await BuildSectionRenderModelAsync(
                proj,
                section,
                pageName,
                filters,
                pageDef.Sections.Select(s => s.Id),
                userCtx));
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

        var deleteResult = await _rowMutationService.DeleteRowAsync(proj.Name, section, rowIdInt, User.Identity?.Name);
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

    // POST /{project}/Page/{pageName}/section/{sectionId}/file-upload
    [Authorize]
    [HttpPost("{pageName}/section/{sectionId}/file-upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FileUpload(string project, string pageName, string sectionId)
    {
        var proj = _projectScope.Current;
        if (!proj.PageMetadata.TryGet(pageName, out var pageDef))
            return NotFound();
        if (!await _pagePermission.CanWritePageAsync(proj.Name, pageName, User.Identity?.Name, UserIsAdmin()))
            return Forbid();

        var section = pageDef.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section == null)
            return NotFound();

        var files = Request.Form.Files;
        if (files.Count == 0)
            return BadRequest(new { error = "ファイルが選択されていません" });

        var uploadDir = !string.IsNullOrWhiteSpace(section.UploadDir)
            ? section.UploadDir
            : "uploads/photo-vault";

        var results = new List<object>();
        var errors = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var savedPath = await _fileUploadService.UploadAsync(file, uploadDir);
                var ext = Path.GetExtension(file.FileName).TrimStart('.');
                var newUuid = Guid.NewGuid().ToString();

                var exifData = ExtractExif(Path.Combine(_env.WebRootPath, savedPath.TrimStart('/')));
                var templateVars = new Dictionary<string, string?>
                {
                    ["filename"]    = file.FileName,
                    ["upload_path"] = savedPath,
                    ["file_size"]   = file.Length.ToString(),
                    ["ext_upper"]   = ext.ToUpperInvariant(),
                    ["now"]         = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["current_user"] = User.Identity?.Name,
                    ["uuid"]        = newUuid,
                    ["photo_id"]    = newUuid,
                    ["exif.width"]  = exifData.width?.ToString(),
                    ["exif.height"] = exifData.height?.ToString(),
                    ["exif.taken_at"] = exifData.taken_at,
                    ["exif.make"]   = exifData.make,
                    ["exif.model"]  = exifData.model,
                    ["exif.focal_length"] = exifData.focal_length,
                    ["exif.aperture"] = exifData.aperture,
                    ["exif.shutter_speed"] = exifData.shutter_speed,
                    ["exif.iso"]    = exifData.iso?.ToString(),
                    ["exif.gps_lat"] = exifData.gps_lat?.ToString(),
                    ["exif.gps_lon"] = exifData.gps_lon?.ToString(),
                };

                if (section.ExtraFields != null)
                {
                    foreach (var ef in section.ExtraFields)
                    {
                        var formVal = Request.Form.TryGetValue(ef.Id, out var val) ? val.ToString() : null;
                        templateVars[ef.Id] = !string.IsNullOrEmpty(formVal) ? formVal : (ef.Default ?? "");
                    }
                }
                foreach (var key in Request.Form.Keys)
                {
                    if (!templateVars.ContainsKey(key))
                    {
                        templateVars[key] = Request.Form[key].ToString();
                    }
                }

                long? newId = null;

                var oc = section.OnUploadComplete;
                if (oc != null && !string.IsNullOrWhiteSpace(oc.InsertEntity))
                {
                    // Build INSERT for primary entity
                    var insertFields = oc.Fields
                        .Select(kv => new KeyValuePair<string, object?>(
                            kv.Key,
                            (object?)ResolveTemplate(kv.Value, templateVars)))
                        .ToList();

                    var cols  = string.Join(", ", insertFields.Select(f => $"\"{f.Key}\""));
                    var parms = string.Join(", ", insertFields.Select(f => $"@{f.Key}"));
                    var insertSql = $"INSERT INTO \"{oc.InsertEntity}\" ({cols}) VALUES ({parms}); SELECT {_dialect.LastInsertIdExpression};";

                    var param = new DynamicParameters();
                    foreach (var f in insertFields)
                        param.Add(f.Key, f.Value is "" ? null : f.Value);

                    newId = await _db.ExecuteScalarAsync<long?>(insertSql, param);

                    // Build INSERT for secondary entity (e.g., processing_queue)
                    if (!string.IsNullOrWhiteSpace(oc.ThenInsertEntity) && oc.ThenFields.Count > 0)
                    {
                        templateVars[$"{oc.InsertEntity}.photo_id"] = newUuid;
                        templateVars["photo_id"] = newUuid;

                        var thenFields = oc.ThenFields
                            .Select(kv => new KeyValuePair<string, object?>(
                                kv.Key,
                                (object?)ResolveTemplate(kv.Value, templateVars)))
                            .ToList();

                        var tCols  = string.Join(", ", thenFields.Select(f => $"\"{f.Key}\""));
                        var tParms = string.Join(", ", thenFields.Select(f => $"@{f.Key}"));
                        var thenSql = $"INSERT INTO \"{oc.ThenInsertEntity}\" ({tCols}) VALUES ({tParms})";

                        var tParam = new DynamicParameters();
                        foreach (var f in thenFields)
                            tParam.Add(f.Key, f.Value is "" ? null : f.Value);

                        await _db.ExecuteAsync(thenSql, tParam);
                    }
                }

                results.Add(new { file = file.FileName, path = savedPath, id = newId });
                _logger.LogInformation("FileUpload: saved {File} to {Path}, photo_id={Id}", file.FileName, savedPath, newId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileUpload: failed for {File}", file.FileName);
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        return Json(new
        {
            success = errors.Count == 0,
            uploaded = results.Count,
            errors,
            files = results
        });
    }

    private static (int? width, int? height, string? taken_at, string? make, string? model,
        string? focal_length, string? aperture, string? shutter_speed, int? iso,
        double? gps_lat, double? gps_lon) ExtractExif(string filePath)
    {
        try
        {
            using var image = Image.Load(filePath);
            var exif = image.Metadata?.ExifProfile;
            if (exif == null) return (null, null, null, null, null, null, null, null, null, null, null);

            string? ExifStr(ExifTag<string> tag)
            {
                if (exif.TryGetValue(tag, out var v) && v is IExifValue<string> sv)
                    return sv.Value;
                return null;
            }

            Rational? ExifRat(ExifTag<Rational> tag)
            {
                if (exif.TryGetValue(tag, out var v) && v is IExifValue<Rational> rv)
                    return rv.Value;
                return null;
            }

            ushort[]? ExifUshortArr(ExifTag<ushort[]> tag)
            {
                if (exif.TryGetValue(tag, out var v) && v is IExifValue<ushort[]> iv)
                    return iv.Value;
                return null;
            }

            var width  = image.Width;
            var height = image.Height;

            var takenAt = ExifStr(ExifTag.DateTimeOriginal)
                       ?? ExifStr(ExifTag.DateTimeDigitized)
                       ?? ExifStr(ExifTag.DateTime);

            var make  = ExifStr(ExifTag.Make);
            var model = ExifStr(ExifTag.Model);

            var focal = ExifRat(ExifTag.FocalLength);
            var focalStr = focal.HasValue ? $"{focal.Value.Numerator / (double)focal.Value.Denominator:F1}" : null;

            var apert = ExifRat(ExifTag.FNumber);
            var apertStr = apert.HasValue ? $"f/{apert.Value.Numerator / (double)apert.Value.Denominator:F1}" : null;

            var shutter = ExifRat(ExifTag.ExposureTime);
            var shutterStr = shutter.HasValue
                ? (shutter.Value.Numerator >= shutter.Value.Denominator
                    ? $"{shutter.Value.Numerator / (double)shutter.Value.Denominator:F1}s"
                    : $"{shutter.Value.Numerator}/{shutter.Value.Denominator}s")
                : null;

            var isoVal = (int?)ExifUshortArr(ExifTag.ISOSpeedRatings)?.FirstOrDefault();

            double? gpsLat = null, gpsLon = null;
            if (exif.TryGetValue(ExifTag.GPSLatitude, out var latV) && latV is IExifValue<Rational[]> latArr)
            {
                var vals = latArr.Value!;
                if (vals.Length == 3)
                {
                    gpsLat = (double)vals[0].Numerator / vals[0].Denominator
                           + (double)vals[1].Numerator / vals[1].Denominator / 60
                           + (double)vals[2].Numerator / vals[2].Denominator / 3600;
                }
                if (exif.TryGetValue(ExifTag.GPSLatitudeRef, out var lr) && lr is IExifValue<string> latRef && latRef.Value == "S")
                    gpsLat = -gpsLat;
            }
            if (exif.TryGetValue(ExifTag.GPSLongitude, out var lonV) && lonV is IExifValue<Rational[]> lonArr)
            {
                var vals = lonArr.Value!;
                if (vals.Length == 3)
                {
                    gpsLon = (double)vals[0].Numerator / vals[0].Denominator
                           + (double)vals[1].Numerator / vals[1].Denominator / 60
                           + (double)vals[2].Numerator / vals[2].Denominator / 3600;
                }
                if (exif.TryGetValue(ExifTag.GPSLongitudeRef, out var lr) && lr is IExifValue<string> lonRef && lonRef.Value == "W")
                    gpsLon = -gpsLon;
            }

            return (width, height, takenAt, make, model, focalStr, apertStr, shutterStr, isoVal, gpsLat, gpsLon);
        }
        catch
        {
            return (null, null, null, null, null, null, null, null, null, null, null);
        }
    }

    private static readonly Regex TemplateVarRegex = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

    private static string? ResolveTemplate(string template, Dictionary<string, string?> vars)
    {
        return TemplateVarRegex.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return vars.TryGetValue(key, out var v) ? v ?? "" : m.Value;
        });
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
                        .ToDictionary(kv => kv.Key, kv => (string?)kv.Value));

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
        IDictionary<string, string> allFilters,
        IEnumerable<string>? allSectionIds = null,
        PageUserContext? userContext = null)
    {
        var (rows, total) = await _pageDataQueryService.LoadSectionDataAsync(section, allFilters, allSectionIds, userContext);
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
    // GET /{project}/Page/{pageName}/switch-provider?type=annotation&provider=lmstudio
    [Authorize]
    [HttpGet("{pageName}/switch-provider")]
    public async Task<IActionResult> SwitchProvider(string project, string pageName, string type, string provider)
    {
        if (!UserIsAdmin())
            return Forbid();

        var allowedAnnotation = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "lmstudio", "ollama", "gemini", "antigravity" };
        var allowedEmbedding = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "lmstudio", "gemini" };

        var typeNorm = (type ?? "").ToLowerInvariant();
        var providerNorm = (provider ?? "").ToLowerInvariant();

        string? settingKey = typeNorm switch
        {
            "annotation" when allowedAnnotation.Contains(providerNorm) => "annotation_provider",
            "embedding"  when allowedEmbedding.Contains(providerNorm)  => "embedding_provider",
            _ => null
        };

        if (settingKey == null)
            return BadRequest("无效的提供商类型或名称");

        var sectionGroup = typeNorm == "annotation" ? "annotation" : "embedding";

        // Try UPDATE first; if no row exists yet, INSERT.
        // Avoids relying on ON CONFLICT(setting_key) which requires a UNIQUE index
        // that the entity framework does not auto-generate.
        var updated = await _db.ExecuteAsync("""
            UPDATE project_settings
            SET value = @V, updated_at = datetime('now')
            WHERE setting_key = @K
            """, new { K = settingKey, V = providerNorm });

        if (updated == 0)
        {
            await _db.ExecuteAsync("""
                INSERT OR IGNORE INTO project_settings
                    (section_group, setting_key, label, value, default_value, description, updated_at)
                VALUES (@SG, @K, @K, @V, @V, '', datetime('now'))
                """, new { SG = sectionGroup, K = settingKey, V = providerNorm });
        }

        await TryWritePageAuditAsync(
            action: "switch_provider",
            entity: "project_settings",
            detail: $"Page={pageName},Type={typeNorm},Provider={providerNorm}");

        return Redirect($"/{project}/Page/{pageName}");
    }

    // GET /{project}/Page/{pageName}/annotate-photo?photo_id=xxx
    // [Authorize]
    [HttpGet("{pageName}/annotate-photo")]
    public async Task<IActionResult> AnnotatePhoto(string project, string pageName, [FromQuery] string photo_id)
    {
        if (string.IsNullOrWhiteSpace(photo_id))
            return BadRequest("照片 ID 未指定");

        var photo = await _db.QueryFirstOrDefaultAsync(
            "SELECT photo_id, file_path FROM photos WHERE photo_id = @Id AND deleted_at IS NULL",
            new { Id = photo_id });
        if (photo == null)
            return NotFound("照片不存在");

        await NetYamlForge.Projects.PhotoVault.Hooks.PhotoVaultSettingsHelper.EnsureSeedAsync(_db, null);
        var provider = await NetYamlForge.Projects.PhotoVault.Hooks.PhotoVaultSettingsHelper.ReadProviderAsync(_db, null, "annotation_provider", "antigravity");

        var now = DateTime.UtcNow;

        var exists = await _db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(1) FROM processing_queue WHERE photo_id = @Id AND status IN ('queued', 'processing')",
            new { Id = photo_id });
        if (exists == 0)
        {
            await _db.ExecuteAsync(@"
                INSERT INTO processing_queue (photo_id, file_path, status, provider, priority, retry_count, queued_at)
                VALUES (@PhotoId, @FilePath, 'queued', @Provider, 10, 0, @Now)",
                new { PhotoId = photo_id, FilePath = (string)photo.file_path, Provider = provider, Now = now });
        }

        await _db.ExecuteAsync(
            "UPDATE photos SET annotation_status = 'pending', updated_at = @Now WHERE photo_id = @Id",
            new { Now = now, Id = photo_id });

        // 立即触发 Worker
        _ = _scheduler.TriggerJobNowAsync(project, "antigravity_cli_worker");

        await TryWritePageAuditAsync(
            action: "annotate_photo",
            entity: "photos",
            detail: $"Page={pageName},PhotoId={photo_id}");

        if (IsHtmxRequest())
        {
            Response.Headers["HX-Trigger"] = ToAsciiJson("{\"show-toast\":{\"message\":\"已入队后台标注，请稍后刷新页面查看结果\",\"type\":\"success\"}}");
            return Ok();
        }

        return Redirect($"/{project}/Page/{pageName}");
    }

    // GET /{project}/Page/{pageName}/embed-photo?photo_id=xxx
    // [Authorize]
    [HttpGet("{pageName}/embed-photo")]
    public async Task<IActionResult> EmbedPhoto(string project, string pageName, [FromQuery] string photo_id)
    {
        if (string.IsNullOrWhiteSpace(photo_id))
            return BadRequest("照片 ID 未指定");

        var photo = await _db.QueryFirstOrDefaultAsync(
            "SELECT photo_id, annotation_status FROM photos WHERE photo_id = @Id AND deleted_at IS NULL",
            new { Id = photo_id });
        if (photo == null)
            return NotFound("照片不存在");

        string annotationStatus = photo.annotation_status?.ToString() ?? "";
        if (annotationStatus != "done")
        {
            if (IsHtmxRequest())
            {
                Response.Headers["HX-Trigger"] = ToAsciiJson("{\"show-toast\":{\"message\":\"请先等待标注完成再生成嵌入\",\"type\":\"warning\"}}");
                return BadRequest("请先等待标注完成");
            }
            return BadRequest("请先等待标注完成");
        }

        // 立即触发嵌入 Generator Job
        _ = _scheduler.TriggerJobNowAsync(project, "embedding_generator");

        await TryWritePageAuditAsync(
            action: "embed_photo",
            entity: "photos",
            detail: $"Page={pageName},PhotoId={photo_id}");

        if (IsHtmxRequest())
        {
            Response.Headers["HX-Trigger"] = ToAsciiJson("{\"show-toast\":{\"message\":\"已开始生成嵌入向量\",\"type\":\"success\"}}");
            return Ok();
        }

        return Redirect($"/{project}/Page/{pageName}");
    }

    private async Task<IActionResult> ReturnSectionOrRedirectAsync(
        ProjectInfo proj, SectionDefinition section, string pageName, string sectionId)
    {
        if (IsHtmxRequest())
        {
            Response.Headers["HX-Retarget"] = $"#section-{sectionId}";
            Response.Headers["HX-Reswap"] = "innerHTML";
            var allSectionIds = proj.PageMetadata.TryGet(pageName, out var pageDef)
                ? pageDef.Sections.Select(s => s.Id)
                : null;
            return PartialView("Components/_SectionTable",
                await BuildSectionRenderModelAsync(
                    proj,
                    section,
                    pageName,
                    GetFiltersFromHtmxCurrentUrl(),
                    allSectionIds,
                    BuildUserContext()));
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

    private PageUserContext BuildUserContext() => new(
        UserName: User.Identity?.Name ?? "",
        DisplayName: User.FindFirst(ClaimTypes.GivenName)?.Value ?? "",
        UserId: User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
        Roles: User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList(),
        IsAdmin: UserIsAdmin(),
        IsAuthenticated: User.Identity?.IsAuthenticated == true,
        OwningProject: User.FindFirst("owning_project")?.Value
    );

    private static bool CanViewSection(SectionDefinition section, PageUserContext userContext)
    {
        if (section.VisibleToRoles == null || section.VisibleToRoles.Count == 0)
            return true;
        if (userContext.IsAdmin)
            return true;
        return userContext.HasAnyRole(section.VisibleToRoles);
    }

    private static string ToAsciiJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        var sb = new System.Text.StringBuilder();
        foreach (var c in json)
        {
            if (c > 127)
            {
                sb.AppendFormat("\\u{0:x4}", (int)c);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

}
