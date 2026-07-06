using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetYamlForge.Models;
using NetYamlForge.Services.Hooks;
using NetYamlForge.Services.Auth;

namespace NetYamlForge.Services.Api;

public class ActionResultDto
{
    public bool Ok { get; set; }
    public string? ErrorMessage { get; set; }
    public bool NotFound { get; set; }
    public int? StatusCode { get; set; }
}

public class ApiEntityActionService
{
    private readonly DynamicEntityCommandService _commandService;
    private readonly IProjectActionRegistry _actionRegistry;
    private readonly IAuditLogService _audit;
    private readonly ILogger<ApiEntityActionService> _logger;

    public ApiEntityActionService(
        DynamicEntityCommandService commandService,
        IProjectActionRegistry actionRegistry,
        IAuditLogService audit,
        ILogger<ApiEntityActionService> logger)
    {
        _commandService = commandService;
        _actionRegistry = actionRegistry;
        _audit = audit;
        _logger = logger;
    }

    public async Task<ActionResultDto> InvokeActionAsync(
        string entity,
        string id,
        string actionKey,
        Dictionary<string, object?>? body,
        EntityDefinition meta,
        string projectName,
        string? username)
    {
        if (!meta.Actions.TryGetValue(actionKey, out var actionDef))
        {
            return new ActionResultDto { Ok = false, NotFound = true, ErrorMessage = $"Action '{actionKey}' not found." };
        }

        var handlerName = string.IsNullOrWhiteSpace(actionDef.Handler) ? actionKey : actionDef.Handler;
        var handler = _actionRegistry.Find(projectName, handlerName);
        if (handler == null)
        {
            _logger.LogWarning("Action handler '{Handler}' not found for project '{Project}'", handlerName, projectName);
            return new ActionResultDto { Ok = false, ErrorMessage = $"Action handler '{handlerName}' not found." };
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
            UserName = username
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
                UserName = username
            };
            var beforeResult = await _commandService.RunBeforeHooksForActionAsync(beforeHooks, hookCtx);
            if (beforeResult.Cancel)
            {
                return new ActionResultDto { Ok = false, ErrorMessage = beforeResult.CancelMessage ?? "Cancelled by pre-action hook." };
            }
        }

        ActionHandlerResult result;
        try
        {
            result = await _commandService.ExecuteActionAsync(handler, ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing action '{Action}'", actionKey);
            return new ActionResultDto { Ok = false, StatusCode = 500, ErrorMessage = "An error occurred during action execution." };
        }

        if (!result.Ok)
        {
            return new ActionResultDto { Ok = false, ErrorMessage = result.ErrorMessage ?? "Action execution failed." };
        }

        await _audit.WriteAsync("api.action", entity, $"id={id} action={actionKey}", username);
        return new ActionResultDto { Ok = true, ErrorMessage = result.ErrorMessage ?? "Action executed successfully." };
    }
}
