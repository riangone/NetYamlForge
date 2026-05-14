// ファイル概要：ホーム系の静的ページ表示を担当します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Models;
using NetYamlForge.Services;

namespace NetYamlForge.Controllers;

public class HomeController : Controller
{
    private readonly ProjectManager _projectManager;
    private readonly IHomePageConfigProvider _homePageConfigProvider;

    public HomeController(ProjectManager projectManager, IHomePageConfigProvider homePageConfigProvider)
    {
        _projectManager = projectManager;
        _homePageConfigProvider = homePageConfigProvider;
    }

    public IActionResult Index()
    {
        // 已登录用户自动跳转到个人主页
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect("/userhome");
        }

        if (_projectManager.TryGet("home", out var homeProject) && homeProject != null)
        {
            return RedirectToAction(nameof(Project), new { project = homeProject.Name });
        }

        var projects = _projectManager.GetAll();
        var vm = new HomeIndexViewModel
        {
            Projects = projects,
            Config = _homePageConfigProvider.GetConfig()
        };
        return View(vm);
    }

    public IActionResult Project(string project)
    {
        if (!_projectManager.TryGet(project, out var projectInfo) || projectInfo == null)
        {
            return NotFound();
        }

        var projects = string.Equals(projectInfo.Name, "home", StringComparison.OrdinalIgnoreCase)
            ? _projectManager.GetAll()
            : new[] { projectInfo };

        var vm = new HomeIndexViewModel
        {
            Projects = projects,
            Config = _homePageConfigProvider.GetConfig(projectInfo.Name)
        };
        return View("Index", vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
