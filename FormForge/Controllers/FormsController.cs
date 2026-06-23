using FormForge.Models;
using FormForge.Services;
using Microsoft.AspNetCore.Mvc;

namespace FormForge.Controllers;

public class FormsController(FormRepository forms, ResponseRepository responses) : Controller
{
    public async Task<IActionResult> Builder(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        return View(new FormBuilderViewModel { Form = form });
    }

    public async Task<IActionResult> Results(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        var summary = await responses.GetSummaryAsync(form);
        var allResponses = await responses.GetByFormAsync(id);
        return View(new FormResultsViewModel { Form = form, Summary = summary, Responses = allResponses });
    }

    [HttpGet("/api/forms/{id}")]
    public async Task<IActionResult> ApiGet(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        return Json(form);
    }

    [HttpPut("/api/forms/{id}")]
    public async Task<IActionResult> ApiSave(string id, [FromBody] SaveFormRequest req)
    {
        var existing = await forms.GetByIdAsync(id);
        if (existing == null) return NotFound();
        await forms.SaveAsync(id, req);
        return Ok(new { ok = true });
    }

    [HttpPost("/api/forms/{id}/publish")]
    public async Task<IActionResult> ApiPublish(string id, [FromBody] PublishRequest req)
    {
        await forms.PublishAsync(id, req.Published);
        return Ok(new { ok = true });
    }
}

public record PublishRequest(bool Published);
