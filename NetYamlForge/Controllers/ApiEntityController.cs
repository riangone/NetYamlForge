// ファイル概要：動的エンティティ向けの汎用 REST API コントローラー。
// 全エンティティに対して CRUD 操作を提供します。
// ルート: /{project}/api/entities/{entity}[/{id}]
// 保守時は副作用を避けるため、公開シグネチャと呼び出し関係の整合性を維持してください。

using NetYamlForge.Models;
using NetYamlForge.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Controllers;

[Authorize]
[ApiController]
[Route("{project}/api/entities")]
public class ApiEntityController : ControllerBase
{
    private readonly IDynamicCrudRepository              _repo;
    private readonly IEntityMetadataProvider             _meta;
    private readonly DynamicEntityCommandService         _commandService;
    private readonly DynamicEntityFormValidationService  _formValidationService;
    private readonly ProjectScope                        _projectScope;
    private readonly ILogger<ApiEntityController>        _logger;

    public ApiEntityController(
        IDynamicCrudRepository             repo,
        IEntityMetadataProvider            meta,
        DynamicEntityCommandService        commandService,
        DynamicEntityFormValidationService formValidationService,
        ProjectScope                       projectScope,
        ILogger<ApiEntityController>       logger)
    {
        _repo                 = repo;
        _meta                 = meta;
        _commandService       = commandService;
        _formValidationService = formValidationService;
        _projectScope         = projectScope;
        _logger               = logger;
    }

    // ─── GET /{project}/api/entities/{entity} ─────────────────────────────────

    /// <summary>エンティティ一覧を取得（ページネーション・検索・ソート対応）</summary>
    [HttpGet("{entity}")]
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
        var denied = RejectIfNotVisible(meta);
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

    // ─── GET /{project}/api/entities/{entity}/meta ────────────────────────────

    /// <summary>エンティティのメタデータを取得</summary>
    [HttpGet("{entity}/meta")]
    public IActionResult GetMeta(string entity)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta);
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

    // ─── GET /{project}/api/entities/{entity}/{id} ────────────────────────────

    /// <summary>単一エンティティを取得</summary>
    [HttpGet("{entity}/{id}")]
    public async Task<IActionResult> GetById(string entity, string id)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta);
        if (denied != null) return denied;

        var item = await _repo.GetByIdAsync(entity, id);
        if (item == null)
            return NotFound(new { error = $"Entity '{entity}' with id '{id}' not found" });

        return Ok(ToApiDto(item, meta));
    }

    // ─── POST /{project}/api/entities/{entity} ────────────────────────────────

    /// <summary>単一エンティティを作成</summary>
    [HttpPost("{entity}")]
    public async Task<IActionResult> Create(
        string entity,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta);
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
            new { entity, id = result.Value.ToString() },
            ToApiDto(created!, meta));
    }

    // ─── PUT /{project}/api/entities/{entity}/{id} ────────────────────────────

    /// <summary>単一エンティティを完全更新</summary>
    [HttpPut("{entity}/{id}")]
    public async Task<IActionResult> Update(
        string entity,
        string id,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta);
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

    // ─── PATCH /{project}/api/entities/{entity}/{id} ──────────────────────────

    /// <summary>単一エンティティを部分更新（送信フィールドのみ更新）</summary>
    [HttpPatch("{entity}/{id}")]
    public async Task<IActionResult> PartialUpdate(
        string entity,
        string id,
        [FromBody] Dictionary<string, object?> body)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta);
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

    // ─── DELETE /{project}/api/entities/{entity}/{id} ─────────────────────────

    /// <summary>単一エンティティを削除</summary>
    [HttpDelete("{entity}/{id}")]
    public async Task<IActionResult> Delete(string entity, string id)
    {
        var meta   = _meta.Get(entity);
        var denied = RejectIfNotVisible(meta);
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

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private IActionResult? RejectIfNotVisible(EntityDefinition meta) =>
        meta.IsPublic || User?.IsInRole("Admin") == true ? null : Forbid();

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
