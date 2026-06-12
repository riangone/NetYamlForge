// ファイル概要：動的エンティティ向けの汎用 REST API コントローラー。
// 全エンティティに対して CRUD 操作を提供します。
// ルート: /api/{project}/{entity}[/{id}]
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Models;
using NetYamlForge.Services;
using NetYamlForge.Services.Hooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

[Authorize(AuthenticationSchemes = $"{Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme},ApiToken")]
[ApiController]
[Route("api/{project}/{entity}")]
public class ApiEntityController : BaseProjectController
{
    private readonly IDynamicCrudRepository              _repo;
    private readonly IEntityMetadataProvider             _meta;
    private readonly DynamicEntityCommandService         _commandService;
    private readonly DynamicEntityFormValidationService  _formValidationService;
    private readonly ProjectScope                        _projectScope;
    private readonly IProjectActionRegistry              _actionRegistry;
    private readonly ILogger<ApiEntityController>        _logger;

    public ApiEntityController(
        IDynamicCrudRepository             repo,
        IEntityMetadataProvider            meta,
        DynamicEntityCommandService        commandService,
        DynamicEntityFormValidationService formValidationService,
        ProjectScope                       projectScope,
        IProjectActionRegistry             actionRegistry,
        ILogger<ApiEntityController>       logger)
    {
        _repo                 = repo;
        _meta                 = meta;
        _commandService       = commandService;
        _formValidationService = formValidationService;
        _projectScope         = projectScope;
        _actionRegistry       = actionRegistry;
        _logger               = logger;
    }

    private IActionResult? ValidateApiAccess(EntityDefinition meta, bool writeRequired)
    {
        var apiMode = (meta.Api ?? "disabled").ToLowerInvariant();
        if (apiMode == "disabled")
        {
            return Problem(
                title: "API Access Disabled",
                detail: $"API access to entity '{meta.Table}' is disabled by configuration.",
                statusCode: StatusCodes.Status403Forbidden
            );
        }
        if (writeRequired && apiMode == "readonly")
        {
            return Problem(
                title: "API Read-Only",
                detail: $"API access to entity '{meta.Table}' is read-only.",
                statusCode: StatusCodes.Status403Forbidden
            );
        }
        return null;
    }

    // ─── GET /api/{project}/{entity} ─────────────────────────────────

    /// <summary>エンティティ一覧を取得（ページネーション・検索・ソート対応）</summary>
    [HttpGet("")]
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
        var denied = RejectIfNotVisible(meta) ?? ValidateApiAccess(meta, writeRequired: false);
        if (denied != null) return denied;

        var filterDict = filters ?? new Dictionary<string, string?>();

        var items = await _repo.GetAllAsync(
            entity:   entity,
            search:   search,
            sort:     sort,
            dir:      dir ?? "asc",
            filters:  filterDict,
            page:     page,
            pageSize: pageSize);

        var total = await _repo.CountAsync(entity, search, filterDict);

        var data = items.Select(item => ToApiDto((IDictionary<string, object?>)item, meta)).ToList();

        return Ok(new ApiListResponse
        {
            Data       = data,
            Page       = page,
            PageSize   = pageSize,
            Total      = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    // ─── GET /api/{project}/{entity}/meta ────────────────────────────

    /// <summary>エンティティのメタデータを取得</summary>
    [HttpGet("meta")]
    public IActionResult GetMeta(string entity)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? ValidateApiAccess(meta, writeRequired: false);
        if (denied != null) return denied;

        var columns = meta.Columns.ToDictionary(
            kv => kv.Key,
            kv => new ApiColumnMeta
            {
                Type     = kv.Value.Type,
                Label    = kv.Value.Label ?? string.Empty,
                Required = kv.Value.Required,
                Editable = kv.Value.Editable,
                Identity = kv.Value.Identity,
                Options  = kv.Value.Options ?? new List<string>()
            });

        var forms = meta.Forms.ToDictionary(
            kv => kv.Key,
            kv => new ApiFormMeta
            {
                Type     = kv.Value.Type,
                Label    = kv.Value.Label ?? string.Empty,
                Required = kv.Value.Required,
                Editable = kv.Value.Editable,
                Options  = kv.Value.Options ?? new List<string>()
            });

        return Ok(new ApiEntityMeta
        {
            Entity          = entity,
            Table           = meta.Table,
            DisplayName     = meta.DisplayName,
            PrimaryKeyColumns = meta.GetPrimaryKeyColumns().ToList(),
            Columns         = columns,
            Forms           = forms
        });
    }

    // ─── GET /api/{project}/{entity}/{id} ────────────────────────────

    /// <summary>単一エンティティを取得</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string entity, string id)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? ValidateApiAccess(meta, writeRequired: false);
        if (denied != null) return denied;

        var item = await _repo.GetByIdAsync(entity, id);
        if (item == null)
            return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });

        return Ok(ToApiDto(item, meta));
    }

    // ─── POST /api/{project}/{entity} ────────────────────────────────

    /// <summary>単一エンティティを作成</summary>
    [HttpPost("")]
    public async Task<IActionResult> Create(
        string entity,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        var stringForm = body.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var (values, errors) = _formValidationService.ConvertAndValidate(meta, stringForm);
        if (errors.Any())
            return BadRequest(new { errors });

        var beforeHooks = meta.Hooks?.GetExpandedHookList(h => h.BeforeCreate, msg => _logger.LogWarning("{Message}", msg));
        var afterHooks  = meta.Hooks?.GetExpandedHookList(h => h.AfterCreate,  msg => _logger.LogWarning("{Message}", msg));

        var result = await _commandService.CreateAsync(
            entity, values, beforeHooks, afterHooks, User.Identity?.Name);

        if (!result.Ok)
            return BadRequest(new { error = result.Error?.Message ?? "Failed to create entity" });

        var created = await _repo.GetByIdAsync(entity, result.Value.ToString()!);
        return CreatedAtAction(
            nameof(GetById),
            new { project = _projectScope.Current.Name, entity, id = result.Value.ToString() },
            ToApiDto(created!, meta));
    }

    // ─── PUT /api/{project}/{entity}/{id} ────────────────────────────

    /// <summary>単一エンティティを完全更新</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        string entity,
        string id,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        if (await _repo.GetByIdAsync(entity, id) == null)
            return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });

        var stringForm = body.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var (values, errors) = _formValidationService.ConvertAndValidate(meta, stringForm);
        if (errors.Any())
            return BadRequest(new { errors });

        var beforeHooks = meta.Hooks?.GetExpandedHookList(h => h.BeforeUpdate, msg => _logger.LogWarning("{Message}", msg));
        var afterHooks  = meta.Hooks?.GetExpandedHookList(h => h.AfterUpdate,  msg => _logger.LogWarning("{Message}", msg));

        var result = await _commandService.UpdateAsync(
            entity, meta.GetPrimaryKeyColumns()[0], id, values, beforeHooks, afterHooks, User.Identity?.Name);

        if (!result.Ok)
            return BadRequest(new { error = result.Error?.Message ?? "Failed to update entity" });

        var updated = await _repo.GetByIdAsync(entity, id);
        return Ok(ToApiDto(updated!, meta));
    }

    // ─── PATCH /api/{project}/{entity}/{id} ──────────────────────────

    /// <summary>単一エンティティを部分更新（送信フィールドのみ更新）</summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> PartialUpdate(
        string entity,
        string id,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        if (await _repo.GetByIdAsync(entity, id) == null)
            return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });

        var stringForm = body.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var (values, errors) = _formValidationService.ConvertAndValidate(meta, stringForm);
        if (errors.Any())
            return BadRequest(new { errors });

        var beforeHooks = meta.Hooks?.GetExpandedHookList(h => h.BeforeUpdate, msg => _logger.LogWarning("{Message}", msg));
        var afterHooks  = meta.Hooks?.GetExpandedHookList(h => h.AfterUpdate,  msg => _logger.LogWarning("{Message}", msg));

        var result = await _commandService.UpdateAsync(
            entity, meta.GetPrimaryKeyColumns()[0], id, values, beforeHooks, afterHooks, User.Identity?.Name);

        if (!result.Ok)
            return BadRequest(new { error = result.Error?.Message ?? "Failed to update entity" });

        var updated = await _repo.GetByIdAsync(entity, id);
        return Ok(ToApiDto(updated!, meta));
    }

    // ─── DELETE /api/{project}/{entity}/{id} ─────────────────────────

    /// <summary>単一エンティティを削除</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string entity, string id)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        if (await _repo.GetByIdAsync(entity, id) == null)
            return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });

        var beforeHooks = meta.Hooks?.GetExpandedHookList(h => h.BeforeDelete, msg => _logger.LogWarning("{Message}", msg));
        var afterHooks  = meta.Hooks?.GetExpandedHookList(h => h.AfterDelete,  msg => _logger.LogWarning("{Message}", msg));

        var result = await _commandService.DeleteAsync(
            entity, meta.GetPrimaryKeyColumns()[0], id, beforeHooks, afterHooks, User.Identity?.Name);

        if (!result.Ok)
            return BadRequest(new { error = result.Error?.Message ?? "Failed to delete entity" });

        return NoContent();
    }

    // ─── POST /api/{project}/{entity}/{id}/actions/{actionKey} ────────

    /// <summary>カスタムアクションを実行</summary>
    [HttpPost("{id}/actions/{actionKey}")]
    public async Task<IActionResult> InvokeAction(
        string entity,
        string id,
        string actionKey,
        [FromBody] Dictionary<string, object?>? body)
    {
        var meta = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta) ?? ValidateApiAccess(meta, writeRequired: true);
        if (denied != null) return denied;

        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
            return NotFound(new { error = $"Action '{actionKey}' not found." });

        var projectName = _projectScope.Current?.Name ?? "";
        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
        {
            _logger.LogWarning("Action handler '{Handler}' not found for project '{Project}'", handlerName, projectName);
            return BadRequest(new { error = $"Action handler '{handlerName}' not found." });
        }

        var inputs = body ?? new Dictionary<string, object?>();

        var ctx = new NetYamlForge.Services.Hooks.CustomActionContext
        {
            Project = projectName,
            Entity = entity,
            Action = actionKey,
            RecordId = id,
            Inputs = inputs,
            Files = new Dictionary<string, string>(),
            MultipleFiles = new Dictionary<string, List<string>>(),
            UserName = User.Identity?.Name
        };

        var beforeHooks = actionDef.Hooks?.Before;
        if (beforeHooks != null && beforeHooks.Count > 0)
        {
            var hookCtx = new NetYamlForge.Services.Hooks.EntityHookContext
            {
                Entity = entity,
                Operation = NetYamlForge.Services.Hooks.CrudOperation.CustomAction,
                Id = int.TryParse(id, out var intId) ? intId : null,
                Values = inputs,
                UserName = User.Identity?.Name
            };
            var beforeResult = await _commandService.RunBeforeHooksForActionAsync(beforeHooks, hookCtx);
            if (beforeResult.Cancel)
                return BadRequest(new { error = beforeResult.CancelMessage ?? "Cancelled by pre-action hook." });
        }

        ActionHandlerResult result;
        try
        {
            result = await _commandService.ExecuteActionAsync(handler, ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action '{Action}'", actionKey);
            return StatusCode(500, new { error = "An error occurred during action execution." });
        }

        if (!result.Ok)
        {
            return BadRequest(new { error = result.ErrorMessage ?? "Action execution failed." });
        }

        return Ok(new { success = true, message = result.ErrorMessage ?? "Action executed successfully." });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static ApiDto ToApiDto(IDictionary<string, object?> item, EntityDefinition meta)
    {
        var pkCol = meta.GetPrimaryKeyColumns()[0];
        var dto   = new ApiDto
        {
            Id   = item.TryGetValue(pkCol, out var idVal) ? idVal?.ToString() : null,
            Data = new Dictionary<string, object?>()
        };

        foreach (var kv in item)
        {
            if (meta.Columns.TryGetValue(kv.Key, out var col))
                dto.Data[kv.Key] = ConvertValue(kv.Value, col.Type);
        }

        return dto;
    }

    private static object? ConvertValue(object? value, string type)
    {
        if (value == null || value == DBNull.Value) return null;
        return type.ToLowerInvariant() switch
        {
            "int" or "integer" or "long"               => Convert.ToInt64(value),
            "double" or "decimal" or "float" or "number" => Convert.ToDecimal(value),
            "bool" or "boolean"                        => Convert.ToBoolean(value),
            _                                          => value.ToString()
        };
    }
}

// ─── Response / Meta DTOs ─────────────────────────────────────────────────────

public class ApiListResponse
{
    public List<ApiDto> Data     { get; set; } = new();
    public int Page              { get; set; }
    public int PageSize          { get; set; }
    public int Total             { get; set; }
    public int TotalPages        { get; set; }
}

public class ApiDto
{
    public string?                     Id   { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
}

public class ApiEntityMeta
{
    public string                          Entity            { get; set; } = string.Empty;
    public string                          Table             { get; set; } = string.Empty;
    public string                          DisplayName       { get; set; } = string.Empty;
    public List<string>                    PrimaryKeyColumns { get; set; } = new();
    public Dictionary<string, ApiColumnMeta> Columns         { get; set; } = new();
    public Dictionary<string, ApiFormMeta>   Forms           { get; set; } = new();
}

public class ApiColumnMeta
{
    public string      Type     { get; set; } = string.Empty;
    public string      Label    { get; set; } = string.Empty;
    public bool        Required { get; set; }
    public bool        Editable { get; set; }
    public bool        Identity { get; set; }
    public List<string> Options { get; set; } = new();
}

public class ApiFormMeta
{
    public string      Type     { get; set; } = string.Empty;
    public string      Label    { get; set; } = string.Empty;
    public bool        Required { get; set; }
    public bool        Editable { get; set; }
    public List<string> Options { get; set; } = new();
}
