#pragma warning disable DCS001

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Dapper;
using NetYamlForge.Models;

namespace NetYamlForge.Services.Workflow;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly IEntityMetadataProvider _metadataProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDbConnection _db;
    private readonly ProjectScope _projectScope;
    private readonly HookExecutionService _hookExecutionService;
    private readonly ILogger<WorkflowEngine> _logger;

    private static readonly ConcurrentDictionary<string, IWorkflowGuard> _guardCache = new();

    public WorkflowEngine(
        IEntityMetadataProvider metadataProvider,
        IHttpContextAccessor httpContextAccessor,
        IDbConnection db,
        ProjectScope projectScope,
        HookExecutionService hookExecutionService,
        ILogger<WorkflowEngine> logger)
    {
        _metadataProvider = metadataProvider;
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _projectScope = projectScope;
        _hookExecutionService = hookExecutionService;
        _logger = logger;
    }

    public async Task<WorkflowTransitionResult> CanTransitionAsync(
        string entityName, 
        string recordId, 
        string actionName, 
        Dictionary<string, object> context)
    {
        try
        {
            if (!_metadataProvider.TryGet(entityName, out var meta))
            {
                return new WorkflowTransitionResult { Success = false, ErrorMessage = $"Entity metadata for \"{entityName}\" not found." };
            }

            var wf = meta.Workflow;
            if (wf == null || !wf.Enabled)
            {
                return new WorkflowTransitionResult { Success = true };
            }

            var transition = wf.Transitions.FirstOrDefault(t => string.Equals(t.Name, actionName, StringComparison.OrdinalIgnoreCase));
            if (transition == null)
            {
                return new WorkflowTransitionResult { Success = false, ErrorMessage = $"Workflow transition \"{actionName}\" not defined for entity \"{entityName}\"." };
            }

            var stateField = wf.StateField ?? "status";
            string currentState = await GetCurrentRecordStateAsync(meta.Table, meta.GetPrimaryKeyColumns()[0], recordId, stateField) ?? wf.InitialState;

            if (!transition.From.Any(s => string.Equals(s, currentState, StringComparison.OrdinalIgnoreCase)))
            {
                return new WorkflowTransitionResult 
                { 
                    Success = false, 
                    ErrorMessage = $"Cannot transition from current state \"{currentState}\" using transition \"{actionName}\". Allowed from states: {string.Join(", ", transition.From)}.",
                    FromState = currentState,
                    ToState = transition.To
                };
            }

            var user = _httpContextAccessor.HttpContext?.User;
            var isUserAdmin = user?.IsInRole("Admin") == true;
            if (transition.Roles != null && transition.Roles.Count > 0 && !isUserAdmin)
            {
                var userRoles = GetCurrentUserRoles(user);
                var hasRole = transition.Roles.Any(r => userRoles.Any(ur => string.Equals(ur, r, StringComparison.OrdinalIgnoreCase)));
                if (!hasRole)
                {
                    return new WorkflowTransitionResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"User roles [{string.Join(", ", userRoles)}] do not have permission for transition \"{actionName}\". Required roles: [{string.Join(", ", transition.Roles)}].",
                        FromState = currentState,
                        ToState = transition.To
                    };
                }
            }

            if (transition.Guards != null)
            {
                foreach (var guard in transition.Guards)
                {
                    if (string.Equals(guard.Type, "script", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(guard.ScriptPath))
                    {
                        var guardInstance = await GetOrCreateGuardInstanceAsync(guard.ScriptPath);
                        if (guardInstance != null)
                        {
                            var guardPassed = await guardInstance.EvaluateAsync(context);
                            if (!guardPassed)
                            {
                                return new WorkflowTransitionResult 
                                { 
                                    Success = false, 
                                    ErrorMessage = $"Workflow transition \"{actionName}\" rejected by guard script \"{guard.ScriptPath}\"._",
                                    FromState = currentState,
                                    ToState = transition.To
                                };
                            }
                        }
                    }
                }
            }

            return new WorkflowTransitionResult 
            { 
                Success = true, 
                FromState = currentState, 
                ToState = transition.To 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CanTransitionAsync for entity={Entity} recordId={RecordId} action={Action}", entityName, recordId, actionName);
            return new WorkflowTransitionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<WorkflowTransitionResult> TriggerTransitionAsync(
        string entityName, 
        string recordId, 
        string actionName, 
        Dictionary<string, object> context)
    {
        var checkResult = await CanTransitionAsync(entityName, recordId, actionName, context);
        if (!checkResult.Success)
        {
            return checkResult;
        }

        if (!_metadataProvider.TryGet(entityName, out var meta) || meta.Workflow == null)
        {
            return checkResult;
        }

        var wf = meta.Workflow;
        var transition = wf.Transitions.First(t => string.Equals(t.Name, actionName, StringComparison.OrdinalIgnoreCase));

        var stateField = wf.StateField ?? "status";
        var sql = $"UPDATE \"{meta.Table}\" SET \"{stateField}\" = @state WHERE \"{meta.GetPrimaryKeyColumns()[0]}\" = @id";
        await _db.ExecuteAsync(sql, new { state = checkResult.ToState, id = recordId });

        await EnsureWorkflowHistoryTableExistsAsync();
        var operatorName = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System";
        var historySql = @"
            INSERT INTO ""WorkflowHistory"" (""Id"", ""EntityName"", ""RecordId"", ""TransitionName"", ""FromState"", ""ToState"", ""Operator"", ""CreatedAt"")
            VALUES (@Id, @EntityName, @RecordId, @TransitionName, @FromState, @ToState, @Operator, @CreatedAt)";
        
        await _db.ExecuteAsync(historySql, new
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityName = entityName,
            RecordId = recordId,
            TransitionName = actionName,
            FromState = checkResult.FromState,
            ToState = checkResult.ToState,
            Operator = operatorName,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });

        if (transition.Actions != null)
        {
            foreach (var action in transition.Actions)
            {
                if (string.Equals(action.Type, "notification", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[Workflow Notification] Template: {Template} triggered for Entity: {Entity}, Record: {RecordId}", action.Template, entityName, recordId);
                }
                else if (string.Equals(action.Type, "hook", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(action.Name))
                {
                    var hookContext = new Hooks.EntityHookContext
                    {
                        Entity = entityName,
                        Operation = Hooks.CrudOperation.CustomAction,
                        UserName = operatorName
                    };
                    if (int.TryParse(recordId, out var idVal))
                    {
                        hookContext.Id = idVal;
                    }
                    var projectName = _projectScope?.IsSet == true ? _projectScope.Current.Name : null;
                    await _hookExecutionService.RunAfterAsync(
                        new List<string> { action.Name },
                        hookContext,
                        projectName,
                        _db,
                        null
                    );
                }
            }
        }

        return checkResult;
    }

    private async Task<string?> GetCurrentRecordStateAsync(string table, string keyCol, string recordId, string stateField)
    {
        var sql = $"SELECT \"{stateField}\" FROM \"{table}\" WHERE \"{keyCol}\" = @id";
        return await _db.QueryFirstOrDefaultAsync<string>(sql, new { id = recordId });
    }

    private List<string> GetCurrentUserRoles(System.Security.Claims.ClaimsPrincipal? user)
    {
        var roles = new List<string>();
        if (user == null || user.Identity?.IsAuthenticated != true) return roles;
        foreach (var claim in user.Claims)
        {
            if (claim.Type == System.Security.Claims.ClaimTypes.Role || claim.Type == "role")
            {
                roles.Add(claim.Value);
            }
        }
        return roles;
    }

    private async Task EnsureWorkflowHistoryTableExistsAsync()
    {
        var createSql = @"
            CREATE TABLE IF NOT EXISTS ""WorkflowHistory"" (
                ""Id"" TEXT PRIMARY KEY,
                ""EntityName"" TEXT NOT NULL,
                ""RecordId"" TEXT NOT NULL,
                ""TransitionName"" TEXT,
                ""FromState"" TEXT,
                ""ToState"" TEXT,
                ""Operator"" TEXT,
                ""CreatedAt"" TEXT NOT NULL
            );";
        await _db.ExecuteAsync(createSql);
    }

    private async Task<IWorkflowGuard?> GetOrCreateGuardInstanceAsync(string scriptPath)
    {
        if (_guardCache.TryGetValue(scriptPath, out var cached))
        {
            return cached;
        }

        var fullPath = scriptPath;
        if (!Path.IsPathRooted(fullPath))
        {
            fullPath = Path.Combine("/home/ubuntu/ws/NetYamlForge/NetYamlForge", scriptPath);
        }

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Workflow guard script not found at path: {Path}", fullPath);
            return null;
        }

        var code = await File.ReadAllTextAsync(fullPath);
        var syntaxTree = CSharpSyntaxTree.ParseText(code, path: fullPath);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            $"WorkflowGuard_{Guid.NewGuid():N}",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(false));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = string.Join("; ", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Failed to compile workflow guard script \"{scriptPath}\": {errors}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        var type = assembly.GetTypes().FirstOrDefault(t => typeof(IWorkflowGuard).IsAssignableFrom(t));
        if (type == null)
        {
            throw new InvalidOperationException($"No class implementing IWorkflowGuard found in script \"{scriptPath}\".");
        }

        var instance = (IWorkflowGuard)Activator.CreateInstance(type)!;
        _guardCache.TryAdd(scriptPath, instance);
        return instance;
    }
}
