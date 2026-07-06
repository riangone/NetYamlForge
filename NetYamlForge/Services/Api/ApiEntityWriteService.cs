using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.Auth;
using NetYamlForge.Controllers;

namespace NetYamlForge.Services.Api;

public class WriteResult
{
    public bool Success { get; set; }
    public bool NotFound { get; set; }
    public Dictionary<string, string> Errors { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public object? CreatedId { get; set; }
    public ApiDto? Entity { get; set; }
}

public class ApiEntityWriteService
{
    private readonly IDynamicCrudRepository _repo;
    private readonly DynamicEntityCommandService _commandService;
    private readonly DynamicEntityFormValidationService _formValidationService;
    private readonly IEntityHooksService _entityHooks;
    private readonly IAuditLogService _audit;
    private readonly ILogger<ApiEntityWriteService> _logger;

    public ApiEntityWriteService(
        IDynamicCrudRepository repo,
        DynamicEntityCommandService commandService,
        DynamicEntityFormValidationService formValidationService,
        IEntityHooksService entityHooks,
        IAuditLogService audit,
        ILogger<ApiEntityWriteService> logger)
    {
        _repo = repo;
        _commandService = commandService;
        _formValidationService = formValidationService;
        _entityHooks = entityHooks;
        _audit = audit;
        _logger = logger;
    }

    public async Task<WriteResult> CreateAsync(
        string entity,
        Dictionary<string, object?> body,
        EntityDefinition meta,
        string projectName,
        string? username)
    {
        var stringForm = body.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var (values, errors) = await _formValidationService.ConvertAndValidateAsync(meta, stringForm, projectName);
        if (errors.Any())
        {
            return new WriteResult { Success = false, Errors = errors };
        }

        var beforeHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeCreate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterHooks  = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterCreate,  msg => _logger.LogWarning("{Message}", msg)) : null;

        var result = await _commandService.CreateAsync(
            entity, values, beforeHooks, afterHooks, username);

        if (!result.Ok)
        {
            return new WriteResult { Success = false, ErrorMessage = result.Error?.Message ?? "Failed to create entity" };
        }

        var created = await _repo.GetByIdAsync(entity, result.Value.ToString()!);
        await _audit.WriteAsync("api.create", entity, $"id={result.Value}", username);

        return new WriteResult
        {
            Success = true,
            CreatedId = result.Value,
            Entity = ApiDtoMapper.ToApiDto(created!, meta)
        };
    }

    public async Task<WriteResult> UpdateAsync(
        string entity,
        string id,
        Dictionary<string, object?> body,
        EntityDefinition meta,
        string projectName,
        string? username)
    {
        var existing = await _repo.GetByIdAsync(entity, id);
        if (existing == null)
        {
            return new WriteResult { Success = false, NotFound = true };
        }

        var stringForm = body.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var (values, errors) = await _formValidationService.ConvertAndValidateAsync(meta, stringForm, projectName);
        if (errors.Any())
        {
            return new WriteResult { Success = false, Errors = errors };
        }

        var beforeHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeUpdate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterHooks  = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterUpdate,  msg => _logger.LogWarning("{Message}", msg)) : null;

        var result = await _commandService.UpdateAsync(
            entity, meta.GetPrimaryKeyColumns()[0], id, values, beforeHooks, afterHooks, username);

        if (!result.Ok)
        {
            return new WriteResult { Success = false, ErrorMessage = result.Error?.Message ?? "Failed to update entity" };
        }

        var updated = await _repo.GetByIdAsync(entity, id);
        await _audit.WriteAsync("api.update", entity, $"id={id}", username);

        return new WriteResult
        {
            Success = true,
            Entity = ApiDtoMapper.ToApiDto(updated!, meta)
        };
    }

    public async Task<WriteResult> PartialUpdateAsync(
        string entity,
        string id,
        Dictionary<string, object?> body,
        EntityDefinition meta,
        string projectName,
        string? username)
    {
        var existing = await _repo.GetByIdAsync(entity, id);
        if (existing == null)
        {
            return new WriteResult { Success = false, NotFound = true };
        }

        var stringForm = body.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        var (values, errors) = await _formValidationService.ConvertAndValidateAsync(meta, stringForm, projectName, isPartial: true);
        if (errors.Any())
        {
            return new WriteResult { Success = false, Errors = errors };
        }

        var beforeHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeUpdate, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterHooks  = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterUpdate,  msg => _logger.LogWarning("{Message}", msg)) : null;

        var result = await _commandService.UpdateAsync(
            entity, meta.GetPrimaryKeyColumns()[0], id, values, beforeHooks, afterHooks, username);

        if (!result.Ok)
        {
            return new WriteResult { Success = false, ErrorMessage = result.Error?.Message ?? "Failed to update entity" };
        }

        var updated = await _repo.GetByIdAsync(entity, id);
        await _audit.WriteAsync("api.patch", entity, $"id={id}", username);

        return new WriteResult
        {
            Success = true,
            Entity = ApiDtoMapper.ToApiDto(updated!, meta)
        };
    }

    public async Task<WriteResult> DeleteAsync(
        string entity,
        string id,
        EntityDefinition meta,
        string? username)
    {
        var existing = await _repo.GetByIdAsync(entity, id);
        if (existing == null)
        {
            return new WriteResult { Success = false, NotFound = true };
        }

        var beforeHooks = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.BeforeDelete, msg => _logger.LogWarning("{Message}", msg)) : null;
        var afterHooks  = meta.Hooks != null ? _entityHooks.GetExpandedHookList(meta.Hooks, h => h.AfterDelete,  msg => _logger.LogWarning("{Message}", msg)) : null;

        var result = await _commandService.DeleteAsync(
            entity, meta.GetPrimaryKeyColumns()[0], id, beforeHooks, afterHooks, username);

        if (!result.Ok)
        {
            return new WriteResult { Success = false, ErrorMessage = result.Error?.Message ?? "Failed to delete entity" };
        }

        await _audit.WriteAsync("api.delete", entity, $"id={id}", username);

        return new WriteResult
        {
            Success = true
        };
    }
}
