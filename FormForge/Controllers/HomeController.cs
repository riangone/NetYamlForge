using FormForge.Services;
using Microsoft.AspNetCore.Mvc;

namespace FormForge.Controllers;

public class HomeController(FormRepository forms) : Controller
{
    public async Task<IActionResult> Index()
    {
        var list = await forms.GetAllAsync();
        return View(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var form = await forms.CreateAsync();
        return RedirectToAction("Builder", "Forms", new { id = form.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        await forms.DeleteAsync(id);
        return RedirectToAction("Index");
    }
}
