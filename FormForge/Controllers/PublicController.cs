using FormForge.Models;
using FormForge.Services;
using Microsoft.AspNetCore.Mvc;

namespace FormForge.Controllers;

public class PublicController(FormRepository forms, ResponseRepository responses) : Controller
{
    [Route("/f/{id}")]
    public async Task<IActionResult> Fill(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        if (!form.IsPublished)
            return View("Closed", new FormFillViewModel { Form = form, Error = "This form is not published yet." });
        if (!form.AcceptsResponses)
            return View("Closed", new FormFillViewModel { Form = form, Error = "This form is no longer accepting responses." });
        return View(new FormFillViewModel { Form = form });
    }

    [HttpPost("/f/{id}/submit")]
    public async Task<IActionResult> Submit(string id)
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
            return RedirectToAction("Fill", new { id });
        }

        await responses.SubmitAsync(id, answers);
        return RedirectToAction("Thanks", new { id });
    }

    [Route("/f/{id}/thanks")]
    public async Task<IActionResult> Thanks(string id)
    {
        var form = await forms.GetByIdAsync(id);
        if (form == null) return NotFound();
        return View(form);
    }
}
