// ファイル概要: 管理者向けユーザー管理画面の一覧・作成・更新を処理します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Data;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("{project}/Users/{action=Index}/{id?}")]
public class UsersController : Controller
{
    private readonly IDbConnection _db;
    private readonly IUserAuthService _users;
    private readonly IAuditLogService _audit;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IDbConnection db, IUserAuthService users, IAuditLogService audit, ILogger<UsersController> logger)
    {
        _db = db;
        _users = users;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _users.GetAllAsync();
        return View(users);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View("Edit", new UserEditViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(UserEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        try
        {
            await ExecuteUserTransactionAsync(async tx =>
            {
                await _users.CreateAsync(model, _db, tx);
                await _audit.WriteAsync("user_create", "AppUser", $"Created user {model.UserName}", User.Identity?.Name, _db, tx);
            });
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Edit", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _users.GetByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        return View(new UserEditViewModel
        {
            Id = user.Id,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            PreferredLanguage = user.PreferredLanguage,
            IsAdmin = user.IsAdmin,
            IsActive = user.IsActive
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UserEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await ExecuteUserTransactionAsync(async tx =>
            {
                await _users.UpdateAsync(model, _db, tx);
                await _audit.WriteAsync("user_update", "AppUser", $"Updated user {model.UserName}", User.Identity?.Name, _db, tx);
            });
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    private async Task ExecuteUserTransactionAsync(Func<IDbTransaction, Task> action)
    {
        if (_db.State != ConnectionState.Open)
        {
            _db.Open();
        }

        using var tx = _db.BeginTransaction();
        try
        {
            await action(tx);
            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User transaction failed");
            tx.Rollback();
            throw;
        }
    }
}
