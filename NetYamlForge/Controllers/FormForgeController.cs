// ファイル概要: FormForge プロジェクト専用コントローラー。
// フォーム管理 (一覧・作成・削除・ビルダー・結果) と公開フォーム記入を処理します。
// ルート: /{project}/FormForge/...

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Projects.FormForge;
using NetYamlForge.Services;

namespace NetYamlForge.Controllers;

[Route("{project}/FormForge")]
[Authorize]
public class FormForgeController(
    FormForgeRepository forms,
    FormForgeResponseRepository responses,
    ProjectScope scope) : BaseProjectController
{
    private string ProjectName => scope.IsSet ? scope.Current.Name : "form-forge";

    // GET /{project}/FormForge
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var list = await forms.GetAllAsync();
        return View($"~/projects/{ProjectName}/views/FormList.cshtml",
            new FfFormListViewModel { Forms = list, ProjectName = ProjectName });
    }

    // GET /{project}/FormForge/Create — nav link shortcut
    [HttpGet("Create")]
    public async Task<IActionResult> CreateGet()
    {
        var form = await forms.CreateAsync();
        return RedirectToAction(nameof(Builder), new { project = ProjectName, id = form.Id });
    }

    // POST /{project}/FormForge/Create
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create()
    {
        var form = await forms.CreateAsync();
        return RedirectToAction(nameof(Builder), new { project = ProjectName, id = form.Id });
    }

    // POST /{project}/FormForge/Delete
    [HttpPost("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await forms.DeleteAsync(id);
        return RedirectToAction(nameof(Index), new { project = ProjectName });
    }

    // GET /{project}/FormForge/Builder/{id}
    [HttpGet("Builder/{id}")]
    public async Task<IActionResult> Builder(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        return View($"~/projects/{ProjectName}/views/FormBuilder.cshtml",
            new FfBuilderViewModel { Form = form, ProjectName = ProjectName });
    }

    // GET /{project}/FormForge/Results/{id}
    [HttpGet("Results/{id}")]
    public async Task<IActionResult> Results(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        var summary = await responses.GetSummaryAsync(form);
        var allResponses = await responses.GetByFormAsync(id);
        return View($"~/projects/{ProjectName}/views/FormResults.cshtml",
            new FfResultsViewModel
            {
                Form = form, Summary = summary, Responses = allResponses,
                ProjectName = ProjectName
            });
    }

    // ── Public fill routes (no auth required) ──

    // GET /{project}/FormForge/Fill/{id}
    [HttpGet("Fill/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Fill(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        if (!form.IsPublished)
            return View($"~/projects/{ProjectName}/views/FormFill.cshtml",
                new FfFillViewModel { Form = form, Error = "This form is not published yet.", ProjectName = ProjectName });
        if (!form.AcceptsResponses)
            return View($"~/projects/{ProjectName}/views/FormFill.cshtml",
                new FfFillViewModel { Form = form, Error = "This form is no longer accepting responses.", ProjectName = ProjectName });
        return View($"~/projects/{ProjectName}/views/FormFill.cshtml",
            new FfFillViewModel { Form = form, ProjectName = ProjectName });
    }

    // POST /{project}/FormForge/Fill/{id}/Submit
    [HttpPost("Fill/{id}/Submit")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string id, string project)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null || !form.IsPublished || !form.AcceptsResponses)
            return BadRequest();

        var answers = new Dictionary<string, string>();
        var errors = new List<string>();

        foreach (var q in form.Questions)
        {
            var value = Request.Form[$"q_{q.Id}"].ToString();
            if (q.Required && string.IsNullOrWhiteSpace(value))
                errors.Add(q.Title);
            if (!string.IsNullOrEmpty(value))
                answers[q.Id] = value;
        }

        if (errors.Count > 0)
        {
            TempData["Errors"] = string.Join("|", errors);
            return RedirectToAction(nameof(Fill), new { project = ProjectName, id });
        }

        await responses.SubmitAsync(id, answers);
        return RedirectToAction(nameof(Thanks), new { project = ProjectName, id });
    }

    // GET /{project}/FormForge/Thanks/{id}
    [HttpGet("Thanks/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Thanks(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        return View($"~/projects/{ProjectName}/views/FormThanks.cshtml",
            new FfFillViewModel { Form = form, ProjectName = ProjectName });
    }

    // ── JSON API for the form builder ──

    // GET /{project}/FormForge/api/forms/{id}
    [HttpGet("api/forms/{id}")]
    public async Task<IActionResult> ApiGet(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        return Json(form);
    }

    // PUT /{project}/FormForge/api/forms/{id}
    [HttpPut("api/forms/{id}")]
    public async Task<IActionResult> ApiSave(string id, [FromBody] FfSaveFormRequest req)
    {
        var existing = await forms.GetByIdAsync(id);
        if (existing == null) return NotFound();
        await forms.SaveAsync(id, req);
        return Ok(new { ok = true });
    }

    // POST /{project}/FormForge/api/forms/{id}/publish
    [HttpPost("api/forms/{id}/publish")]
    public async Task<IActionResult> ApiPublish(string id, [FromBody] FfPublishRequest req)
    {
        await forms.PublishAsync(id, req.Published);
        return Ok(new { ok = true });
    }
}
