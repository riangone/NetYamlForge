using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Projects.KbForge;
using NetYamlForge.Services;

namespace NetYamlForge.Controllers;

[Route("{project}/Kb")]
public class KbForgeController(KbForgeRepository kb, ProjectScope scope) : BaseProjectController
{
    private string ProjectName => scope.IsSet ? scope.Current.Name : "kb-forge";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? tag = null, string? status = null)
    {
        var articles = await kb.GetAllAsync(tag, status);
        var tags = await kb.GetAllTagsAsync();
        return View($"~/projects/{ProjectName}/views/ArticleList.cshtml",
            new KbArticleListViewModel
            {
                Articles = articles,
                Tags = tags,
                FilterTag = tag,
                FilterStatus = status,
                ProjectName = ProjectName
            });
    }

    [HttpGet("New")]
    public async Task<IActionResult> New()
    {
        var tags = await kb.GetAllTagsAsync();
        return View($"~/projects/{ProjectName}/views/ArticleEdit.cshtml",
            new KbArticleEditViewModel
            {
                Article = new KbArticle(),
                AllTags = tags,
                IsNew = true,
                ProjectName = ProjectName
            });
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] KbSaveArticleRequest req)
    {
        var author = User.Identity?.Name ?? "anonymous";
        var article = await kb.CreateAsync(req, author);
        return RedirectToAction(nameof(View), new { project = ProjectName, slug = article.Slug });
    }

    [HttpGet("View/{slug}")]
    [ActionName("View")]
    public async Task<IActionResult> ViewArticle(string slug)
    {
        var article = await kb.GetBySlugAsync(slug);
        if (article == null) return NotFound();
        await kb.IncrementViewCountAsync(article.Id);
        var revisions = await kb.GetRevisionsAsync(article.Id);
        return View($"~/projects/{ProjectName}/views/ArticleView.cshtml",
            new KbArticleViewViewModel
            {
                Article = article,
                Revisions = revisions,
                ProjectName = ProjectName
            });
    }

    [HttpGet("Edit/{id}")]
    public async Task<IActionResult> Edit(string id)
    {
        var article = await kb.GetByIdAsync(id);
        if (article == null) return NotFound();
        var tags = await kb.GetAllTagsAsync();
        return View($"~/projects/{ProjectName}/views/ArticleEdit.cshtml",
            new KbArticleEditViewModel
            {
                Article = article,
                AllTags = tags,
                IsNew = false,
                ProjectName = ProjectName
            });
    }

    [HttpPost("Edit/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(string id, [FromForm] KbSaveArticleRequest req)
    {
        var changedBy = User.Identity?.Name ?? "anonymous";
        var article = await kb.UpdateAsync(id, req, changedBy);
        if (article == null) return NotFound();
        return RedirectToAction(nameof(View), new { project = ProjectName, slug = article.Slug });
    }

    [HttpPost("Delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        await kb.DeleteAsync(id);
        return RedirectToAction(nameof(Index), new { project = ProjectName });
    }

    [HttpGet("Search")]
    public async Task<IActionResult> Search(string? q = null, bool semantic = false)
    {
        var results = new List<KbSearchResult>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            results = semantic
                ? await kb.SemanticSearchAsync(q)
                : await kb.KeywordSearchAsync(q);
        }
        return View($"~/projects/{ProjectName}/views/SemanticSearch.cshtml",
            new KbSearchViewModel
            {
                Query = q,
                Semantic = semantic,
                Results = results,
                ProjectName = ProjectName
            });
    }

    [HttpPost("Summarize/{id}")]
    public async Task<IActionResult> Summarize(string id)
    {
        var summary = await kb.SummarizeAsync(id);
        if (summary == null) return StatusCode(500, new { error = "AI summarization failed" });
        return Ok(new { summary });
    }
}
