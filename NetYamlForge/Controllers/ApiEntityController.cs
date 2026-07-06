// ファイル概要：動的エンティティ向けの汎用 REST API コントローラー。
// 全エンティティに対して CRUD 操作を提供します。
// ルート: /api/{project}/{entity}[/{id}]
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Api;

namespace NetYamlForge.Controllers;

/// <summary>動的エンティティ向け汎用 REST API。CRUD + カスタムアクションを提供します。</summary>
[Authorize(AuthenticationSchemes = $"{Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme},ApiToken")]
[ApiController]
[Produces("application/json")]
[Route("api/{project}/{entity}")]
public class ApiEntityController : BaseProjectController
{
    private readonly IEntityMetadataProvider _meta;
    private readonly ProjectScope _projectScope;
    private readonly ApiEntityAccessGuard _accessGuard;
    private readonly ApiEntityQueryService _queryService;
    private readonly ApiEntityWriteService _writeService;
    private readonly ApiEntityActionService _actionService;

    public ApiEntityController(
        IEntityMetadataProvider meta,
        ProjectScope projectScope,
        ApiEntityAccessGuard accessGuard,
        ApiEntityQueryService queryService,
        ApiEntityWriteService writeService,
        ApiEntityActionService actionService)
    {
        _meta = meta;
        _projectScope = projectScope;
        _accessGuard = accessGuard;
        _queryService = queryService;
        _writeService = writeService;
        _actionService = actionService;
    }

    // ─── GET /api/{project}/{entity} ─────────────────────────────────

    /// <summary>エンティティ一覧を取得（ページネーション・検索・ソート・フィルタ対応）</summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(ApiListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        string entity,
        [FromQuery] string? search   = null,
        [FromQuery] string? sort     = null,
        [FromQuery] string? dir      = null,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20,
        [FromQuery] Dictionary<string, string?>? filters = null)
    {
        var meta = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? _accessGuard.ValidateApiAccess(meta, writeRequired: false);
        if (denied != null) return denied;

        var result = await _queryService.GetListAsync(entity, search, sort, dir, page, pageSize, filters, meta);
        return Ok(result);
    }

    // ─── GET /api/{project}/{entity}/meta ────────────────────────────

    /// <summary>エンティティのカラム定義・フォーム定義・主キー情報を取得</summary>
    [HttpGet("meta")]
    [ProducesResponseType(typeof(ApiEntityMeta), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetMeta(string entity)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? _accessGuard.ValidateApiAccess(meta, writeRequired: false);
        if (denied != null) return denied;

        var result = _queryService.GetMeta(entity, meta);
        return Ok(result);
    }

    // ─── GET /api/{project}/{entity}/{id} ────────────────────────────

    /// <summary>主キーを指定して単一エンティティを取得</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string entity, string id)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? _accessGuard.ValidateApiAccess(meta, writeRequired: false);
        if (denied != null) return denied;

        var item = await _queryService.GetByIdAsync(entity, id, meta);
        if (item == null)
            return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });

        return Ok(item);
    }

    // ─── POST /api/{project}/{entity} ────────────────────────────────

    /// <summary>エンティティレコードを新規作成</summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(ApiDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        string entity,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? _accessGuard.ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        var projectName = _projectScope.Current?.Name ?? "DefaultProject";
        var result = await _writeService.CreateAsync(entity, body, meta, projectName, User.Identity?.Name);

        if (!result.Success)
        {
            if (result.Errors.Any())
                return BadRequest(new { errors = result.Errors });
            return BadRequest(new { error = result.ErrorMessage ?? "Failed to create entity" });
        }

        return CreatedAtAction(
            nameof(GetById),
            new { project = _projectScope.Current.Name, entity, id = result.CreatedId!.ToString() },
            result.Entity);
    }

    // ─── PUT /api/{project}/{entity}/{id} ────────────────────────────

    /// <summary>エンティティレコードを完全更新（PUT）</summary>
    [ProducesResponseType(typeof(ApiDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        string entity,
        string id,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? _accessGuard.ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        var projectName = _projectScope.Current?.Name ?? "DefaultProject";
        var result = await _writeService.UpdateAsync(entity, id, body, meta, projectName, User.Identity?.Name);

        if (!result.Success)
        {
            if (result.NotFound)
                return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });
            if (result.Errors.Any())
                return BadRequest(new { errors = result.Errors });
            return BadRequest(new { error = result.ErrorMessage ?? "Failed to update entity" });
        }

        return Ok(result.Entity);
    }

    // ─── PATCH /api/{project}/{entity}/{id} ──────────────────────────

    /// <summary>エンティティレコードを部分更新（送信フィールドのみ更新）</summary>
    [ProducesResponseType(typeof(ApiDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPatch("{id}")]
    public async Task<IActionResult> PartialUpdate(
        string entity,
        string id,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? _accessGuard.ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        var projectName = _projectScope.Current?.Name ?? "DefaultProject";
        var result = await _writeService.PartialUpdateAsync(entity, id, body, meta, projectName, User.Identity?.Name);

        if (!result.Success)
        {
            if (result.NotFound)
                return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });
            if (result.Errors.Any())
                return BadRequest(new { errors = result.Errors });
            return BadRequest(new { error = result.ErrorMessage ?? "Failed to update entity" });
        }

        return Ok(result.Entity);
    }

    // ─── DELETE /api/{project}/{entity}/{id} ─────────────────────────

    /// <summary>主キーを指定してエンティティレコードを削除</summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string entity, string id)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? _accessGuard.ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        var result = await _writeService.DeleteAsync(entity, id, meta, User.Identity?.Name);

        if (!result.Success)
        {
            if (result.NotFound)
                return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });
            return BadRequest(new { error = result.ErrorMessage ?? "Failed to delete entity" });
        }

        return NoContent();
    }

    // ─── POST /api/{project}/{entity}/{id}/actions/{actionKey} ────────

    /// <summary>エンティティに定义されたカスタムアクションを実行</summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id}/actions/{actionKey}")]
    public async Task<IActionResult> InvokeAction(
        string entity,
        string id,
        string actionKey,
        [FromBody] Dictionary<string, object?>? body)
    {
        var meta = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? _accessGuard.ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        var projectName = _projectScope.Current?.Name ?? "";
        var result = await _actionService.InvokeActionAsync(entity, id, actionKey, body, meta, projectName, User.Identity?.Name);

        if (!result.Ok)
        {
            if (result.NotFound)
                return NotFound(new { error = result.ErrorMessage });
            if (result.StatusCode == 500)
                return StatusCode(500, new { error = result.ErrorMessage });
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new { success = true, message = result.ErrorMessage });
    }
}
