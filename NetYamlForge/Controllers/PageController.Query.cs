using System.Data;
using System.Linq;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

public partial class PageController
{
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

        // プロジェクト固有のレイアウトを設定（PageView やカスタムビューで共有）
        var layoutPath = $"~/projects/{proj.Name}/views/_Layout.cshtml";
        ViewBag.LayoutPath = System.IO.File.Exists(Path.Combine(proj.ProjectDir, "views", "_Layout.cshtml"))
            ? layoutPath
            : "_Layout";

        // プロジェクト固有のビューを検索
        var projectViewPath = Path.Combine(proj.ProjectDir, "views", pageName + ".cshtml");
        if (System.IO.File.Exists(projectViewPath))
        {
            return View($"~/projects/{proj.Name}/views/{pageName}.cshtml", model);
        }

        // ProjectViewLocationExpander が projects/{project}/views/{template}.cshtml を解決する
        if (!string.IsNullOrEmpty(pageDef.Template))
        {
            var templatePath = Path.Combine(proj.ProjectDir, "views", pageDef.Template + ".cshtml");
            if (System.IO.File.Exists(templatePath))
            {
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
}
