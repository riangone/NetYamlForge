using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.AI.ToolValidation;

/// <summary>
/// Tool 调用验证器
/// 
/// 三重安全网关:
/// 1. JSON Schema 结构校验
/// 2. Entity/Action 白名单校验
/// 3. SqlSafetyGuard 标识符过滤
/// </summary>
public class ToolCallValidator
{
    private readonly ILogger<ToolCallValidator> _logger;

    public ToolCallValidator(
        ILogger<ToolCallValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 验证 Tool 调用请求
    /// </summary>
    /// <param name="toolCall">LLM 输出的 Tool 调用 JSON</param>
    /// <param name="projectId">项目 ID</param>
    /// <param name="currentState">当前 FSM 状态</param>
    /// <returns>验证结果</returns>
    public async Task<ToolValidationResult> ValidateAsync(
        JsonNode toolCall,
        string projectId,
        AppointmentStateMachine.State? currentState = null)
    {
        var entityName = toolCall["entity"]?.ToString();
        var action = toolCall["action"]?.ToString();

        if (string.IsNullOrEmpty(entityName))
        {
            return ToolValidationResult.Fail("entity 字段不能为空");
        }

        if (string.IsNullOrEmpty(action))
        {
            return ToolValidationResult.Fail("action 字段不能为空");
        }

        // [1] JSON Schema 校验
        var schemaResult = await ValidateSchemaAsync(toolCall);
        if (!schemaResult.IsValid)
        {
            return schemaResult;
        }

        // [2] SqlSafetyGuard 标识符过滤 (安全校验前置)
        var safetyResult = ValidateSqlSafety(toolCall);
        if (!safetyResult.IsValid)
        {
            return safetyResult;
        }

        // [3] Entity/Action 白名单校验
        var entityResult = await ValidateEntityActionAsync(entityName, action, projectId);
        if (!entityResult.IsValid)
        {
            return entityResult;
        }

        // [4] 状态白名单检查(如果提供了当前状态)
        if (currentState.HasValue)
        {
            var stateResult = ValidateStateAllowlist(toolCall, currentState.Value);
            if (!stateResult.IsValid)
            {
                return stateResult;
            }
        }

        return ToolValidationResult.Success();
    }

    /// <summary>
    /// [1] JSON Schema 校验
    /// </summary>
    private async Task<ToolValidationResult> ValidateSchemaAsync(JsonNode toolCall)
    {
        var toolName = toolCall["tool_call"]?.ToString();
        var schema = await LoadToolSchemaAsync(toolName);

        if (schema == null)
        {
            // 如果没有 schema 文件,跳过此校验
            return ToolValidationResult.Success();
        }

        var toolCallJson = toolCall.ToJsonString();
        var jsonElement = JsonDocument.Parse(toolCallJson).RootElement;
        var validationResult = schema.Evaluate(jsonElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });
        
        if (!validationResult.IsValid)
        {
            var errors = string.Join("; ", 
                validationResult.Details?
                    .Where(d => !d.IsValid && d.Errors != null)
                    .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key}={e.Value}")) 
                    ?? Array.Empty<string>());
            return ToolValidationResult.Fail($"JSON Schema 校验失败:{errors}");
        }

        return ToolValidationResult.Success();
    }

    /// <summary>
    /// [2] Entity/Action 白名单校验
    /// </summary>
    private Task<ToolValidationResult> ValidateEntityActionAsync(
        string? entityName,
        string? action,
        string projectId)
    {
        if (string.IsNullOrEmpty(entityName))
        {
            return Task.FromResult(ToolValidationResult.Fail("entity 字段不能为空"));
        }

        if (string.IsNullOrEmpty(action))
        {
            return Task.FromResult(ToolValidationResult.Fail("action 字段不能为空"));
        }

        // 允许的实体列表(默认)
        var allowedEntities = new[]
        {
            "vehicles",
            "sales_leads",
            "customers",
            "service_appointments",
            "test_drives"
        };

        bool hasEntity = false;
        foreach (var ent in allowedEntities)
        {
            if (string.Equals(ent, entityName, StringComparison.OrdinalIgnoreCase))
            {
                hasEntity = true;
                break;
            }
        }

        if (!hasEntity)
        {
            return Task.FromResult(ToolValidationResult.Fail(
                $"实体 '{entityName}' 不在项目 '{projectId}' 的白名单中。" +
                $"允许的实体:{string.Join(", ", allowedEntities)}"));
        }

        // 验证 Action 是否合法
        var allowedActions = new[] { "list", "count", "get", "create", "update" };
        bool hasAction = false;
        foreach (var act in allowedActions)
        {
            if (string.Equals(act, action, StringComparison.OrdinalIgnoreCase))
            {
                hasAction = true;
                break;
            }
        }

        if (!hasAction)
        {
            return Task.FromResult(ToolValidationResult.Fail(
                $"操作 '{action}' 不允许。" +
                $"允许的操作:{string.Join(", ", allowedActions)}"));
        }

        return Task.FromResult(ToolValidationResult.Success());
    }

    /// <summary>
    /// [3] SqlSafetyGuard 标识符过滤 ⚠️ 核心安全层
    /// </summary>
    private ToolValidationResult ValidateSqlSafety(JsonNode toolCall)
    {
        var entityName = toolCall["entity"]?.ToString();
        var action = toolCall["action"]?.ToString();

        // 3.1 验证实体名(防止 SQL 注入表名)
        try
        {
            SqlSafetyGuard.EnsureIdentifier(entityName, "tool_call.entity");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[SqlSafetyGuard] 实体名校验失败:{Entity}", entityName);
            return ToolValidationResult.Fail($"实体名包含不安全字符:{entityName}");
        }

        // 3.2 验证 Action(防止包含特殊注入字符)
        if (string.IsNullOrEmpty(action) || !System.Text.RegularExpressions.Regex.IsMatch(action, "^[a-zA-Z0-9_]+$"))
        {
            return ToolValidationResult.Fail($"操作名包含非法字符:{action}");
        }

        // 3.3 验证过滤器字段(防止 SQL 注入列名)
        if (toolCall["filters"] is JsonArray filtersArray)
        {
            foreach (var filter in filtersArray)
            {
                if (filter is JsonObject filterObj && filterObj.TryGetPropertyValue("field", out var fieldNode))
                {
                    var fieldName = fieldNode?.ToString();
                    if (!string.IsNullOrEmpty(fieldName))
                    {
                        try
                        {
                            // ⚠️ 核心:使用 SqlSafetyGuard 验证列名
                            SqlSafetyGuard.EnsureIdentifier(fieldName, "tool_call.filter.field");
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.LogWarning(ex, "[SqlSafetyGuard] 过滤器字段校验失败:{Field}", fieldName);
                            return ToolValidationResult.Fail($"过滤字段名包含不安全字符:{fieldName}");
                        }
                    }
                }

                // 验证排序字段
                if (filter is JsonObject filterObj2 && filterObj2.TryGetPropertyValue("orderBy", out var orderByNode))
                {
                    if (orderByNode is JsonObject orderByObj && orderByObj.TryGetPropertyValue("field", out var orderFieldNode))
                    {
                        var orderField = orderFieldNode?.ToString();
                        if (!string.IsNullOrEmpty(orderField))
                        {
                            try
                            {
                                // ⚠️ 核心:使用 SqlSafetyGuard 验证排序列名
                                SqlSafetyGuard.EnsureIdentifier(orderField, "tool_call.orderBy.field");
                            }
                            catch (InvalidOperationException ex)
                            {
                                _logger.LogWarning(ex, "[SqlSafetyGuard] 排序字段校验失败:{Field}", orderField);
                                return ToolValidationResult.Fail($"排序字段名包含不安全字符:{orderField}");
                            }
                        }
                    }
                }
            }
        }

        // 3.4 检测危险 SQL 标记
        var fullJson = toolCall.ToJsonString();
        if (SqlSafetyGuard.IsUnsafeToken(fullJson))
        {
            _logger.LogWarning("[SqlSafetyGuard] 检测到潜在 SQL 注入标记");
            return ToolValidationResult.Fail("请求包含潜在的 SQL 注入攻击标记");
        }

        return ToolValidationResult.Success();
    }

    /// <summary>
    /// [4] 状态白名单检查
    /// </summary>
    private ToolValidationResult ValidateStateAllowlist(
        JsonNode toolCall,
        AppointmentStateMachine.State currentState)
    {
        var toolName = toolCall["tool_call"]?.ToString();

        if (string.IsNullOrEmpty(toolName))
        {
            return ToolValidationResult.Fail("tool_call 字段不能为空");
        }

        // 根据状态检查允许的 Tool
        var allowedTools = currentState switch
        {
            AppointmentStateMachine.State.Init => new HashSet<string> { "query_data" },
            AppointmentStateMachine.State.CollectVehicle => new HashSet<string> { "query_data" },
            AppointmentStateMachine.State.CollectDate => new HashSet<string>(),
            AppointmentStateMachine.State.CollectTime => new HashSet<string>(),
            AppointmentStateMachine.State.CollectName => new HashSet<string>(),
            AppointmentStateMachine.State.CollectPhone => new HashSet<string>(),
            AppointmentStateMachine.State.Confirming => new HashSet<string> { "create_appointment_request" },
            AppointmentStateMachine.State.Booked => new HashSet<string>(),
            AppointmentStateMachine.State.Escalate => new HashSet<string>(), // 人工接管
            _ => new HashSet<string>()
        };

        if (!allowedTools.Contains(toolName))
        {
            return ToolValidationResult.Fail(
                $"当前状态 '{currentState}' 不允许使用 Tool '{toolName}'。" +
                $"允许的 Tool:{string.Join(", ", allowedTools)}");
        }

        return ToolValidationResult.Success();
    }

    /// <summary>
    /// 加载 Tool Schema(从 skills/ 目录)
    /// </summary>
    private async Task<JsonSchema?> LoadToolSchemaAsync(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName))
        {
            return null;
        }

        var schemaPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "skills",
            "auto-dealer",
            $"_schema-{toolName}.json");

        if (!File.Exists(schemaPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(schemaPath);
        return JsonSchema.FromText(json);
    }
}

/// <summary>
/// Tool 验证结果
/// </summary>
public class ToolValidationResult
{
    public bool IsValid { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static ToolValidationResult Success() => new() { IsValid = true };

    public static ToolValidationResult Fail(string message) =>
        new() { IsValid = false, ErrorMessage = message };
}
