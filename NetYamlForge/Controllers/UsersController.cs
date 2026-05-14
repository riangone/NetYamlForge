// ファイル概要: 管理者向けユーザー管理画面の一覧・作成・更新を処理します。
// このファイルはアプリの重要な構成要素を定義します。
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System.Data;
using NetYamlForge.Models.Auth;
using NetYamlForge.Services;
using NetYamlForge.Services.Auth;
using NetYamlForge.Services.Connection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

[Authorize(Policy = "AdminOnly")]
[Route("{project}/Users/{action=Index}/{id?}")]
public class UsersController : Controller
{
    private readonly IConnectionManager _connectionManager;
    private readonly IUserAuthService _users;
    private readonly IJpcsUserSyncService _syncService;
    private readonly IAuditLogService _audit;
    private readonly ProjectScope _projectScope;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IConnectionManager connectionManager, IUserAuthService users, IJpcsUserSyncService syncService, IAuditLogService audit, ProjectScope projectScope, ILogger<UsersController> logger)
    {
        _connectionManager = connectionManager;
        _users = users;
        _syncService = syncService;
        _audit = audit;
        _projectScope = projectScope;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var projectName = _projectScope.IsSet ? _projectScope.Current.Name : RouteData.Values["project"]?.ToString();
        // 如果是 framework 项目，显示所有用户；否则只显示当前项目的用户
        var filterProject = projectName == "framework" ? null : projectName;
        var users = await _users.GetAllAsync(filterProject);
        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncJpcsUsers()
    {
        var result = await _syncService.SyncUsersAsync();
        if (result.Errors.Any())
        {
            TempData["Error"] = string.Join("; ", result.Errors);
        }
        else
        {
            TempData["Message"] = $"Sync completed. Found: {result.TotalFound}, Created: {result.CreatedCount}, Updated: {result.UpdatedCount}";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Create()
    {
        var projectName = _projectScope.IsSet ? _projectScope.Current.Name : RouteData.Values["project"]?.ToString();
        return View("Edit", new UserEditViewModel 
        { 
            OwningProject = projectName == "framework" ? null : projectName 
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        try
        {
            var projectName = _projectScope.IsSet ? _projectScope.Current.Name : RouteData.Values["project"]?.ToString();
            if (string.IsNullOrEmpty(model.OwningProject))
            {
                model.OwningProject = projectName == "framework" ? null : projectName;
            }

            await _users.CreateAsync(model);
            await _audit.WriteAsync("user_create", "AppUser", $"Created user {model.UserName} for project {model.OwningProject ?? "global"}", User.Identity?.Name);
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
            IsActive = user.IsActive,
            OwningProject = user.OwningProject
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await _users.UpdateAsync(model);
            await _audit.WriteAsync("user_update", "AppUser", $"Updated user {model.UserName}", User.Identity?.Name);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _users.DeleteAsync(id);
            await _audit.WriteAsync("user_delete", "AppUser", $"Deleted user id={id}", User.Identity?.Name);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete user {UserId}", id);
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task ExecuteUserTransactionAsync(Func<IDbTransaction, Task> action)
    {
        var conn = await _connectionManager.GetConnectionAsync();
        try
        {
            using var tx = conn.BeginTransaction();
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
        finally
        {
            _connectionManager.ReleaseConnection(conn);
        }
    }
}
