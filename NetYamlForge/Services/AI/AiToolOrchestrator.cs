using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.Logging;
using NetYamlForge.Services.AI.ToolValidation;

namespace NetYamlForge.Services.AI;

/// <summary>
/// AI Tool 调用编排服务
/// 
/// 功能:
/// 1. 验证 Tool 调用请求 (ToolCallValidator)
/// 2. 检查 FSM 状态允许性
/// 3. 执行 Tool 并返回结果
/// 4. 记录审计日志
/// 
/// 这个服务作为 AutoDealerChatService 和其他服务之间的中间层,
/// 在不修改现有庞大代码的情况下集成验证逻辑。
/// </summary>
public interface IAiToolOrchestrator
{
    /// <summary>
    /// 验证并执行 Tool 调用
    /// </summary>
    /// <param name="toolCall">LLM 输出的 Tool 调用 JSON</param>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="projectId">项目 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Tool 执行结果</returns>
    Task<ToolExecutionResult> ValidateAndExecuteToolAsync(
        JsonNode toolCall,
        string conversationId,
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// 获取当前会话的状态信息和允许的 Tool 列表
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="projectId">
    /// テナント ID。未指定時は ProjectScope（リクエストスコープ）から解決する。
    /// マルチテナント文脈で呼び出す場合は明示的に渡すことを推奨。
    /// </param>
    Task<SessionStateInfo> GetSessionStateAsync(string conversationId, string? projectId = null);
}

/// <summary>
/// Tool 执行结果
/// </summary>
public class ToolExecutionResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Data { get; set; }
    public string? ValidationFailedReason { get; set; }
    public string? FsmState { get; set; }
    public bool IsEscalated { get; set; }
}

/// <summary>
/// 会话状态信息
/// </summary>
public class SessionStateInfo
{
    public string ConversationId { get; set; } = string.Empty;
    public string FsmState { get; set; } = string.Empty;
    public HashSet<string> AllowedTools { get; set; } = new();
    public Dictionary<string, string> CollectedSlots { get; set; } = new();
    public int LowConfidenceCount { get; set; }
    public bool IsEscalated { get; set; }
}

/// <summary>
/// AI Tool 调用编排服务实现
/// </summary>
public class AiToolOrchestrator : IAiToolOrchestrator
{
    private readonly ToolCallValidator _validator;
    private readonly ISlotFillingManager _slotFillingManager;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<AiToolOrchestrator> _logger;
    private readonly ProjectScope? _projectScope;

    public AiToolOrchestrator(
        ToolCallValidator validator,
        ISlotFillingManager slotFillingManager,
        IToolRegistry toolRegistry,
        ILogger<AiToolOrchestrator> logger,
        ProjectScope? projectScope = null)
    {
        _validator = validator;
        _slotFillingManager = slotFillingManager;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _projectScope = projectScope;
    }

    /// <summary>
    /// 验证并执行 Tool 调用
    /// </summary>
    public async Task<ToolExecutionResult> ValidateAndExecuteToolAsync(
        JsonNode toolCall,
        string conversationId,
        string projectId,
        CancellationToken ct = default)
    {
        var result = new ToolExecutionResult();

        // ⚠️ 租户防越权校验与解析
        var resolvedProjectId = projectId;
        if (_projectScope != null && _projectScope.IsSet)
        {
            var activeProject = _projectScope.Current.Name;
            if (string.IsNullOrEmpty(resolvedProjectId))
            {
                resolvedProjectId = activeProject;
            }
            else if (!string.Equals(resolvedProjectId, activeProject, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[ToolOrchestrator] 检测到跨租户越权调用试图！Conv={ConvId}, ActiveTenant={Active}, RequestTenant={Request}",
                    conversationId, activeProject, resolvedProjectId);
                
                result.IsSuccess = false;
                result.ValidationFailedReason = "访问被拒绝：不允许跨租户调用 Tool";
                result.ErrorMessage = result.ValidationFailedReason;
                return result;
            }
        }

        if (string.IsNullOrEmpty(resolvedProjectId))
        {
            // ⚠️ ProjectScope 未設定かつ呼び出し元も projectId を指定しなかった場合、
            // "default" テナントへ暗黙的にフォールバックすると他テナントとの串話リスクがあるため、
            // ここでは処理を中断し監査ログを記録する（fail-closed）。
            _logger.LogWarning(
                "[ToolOrchestrator] projectId が未指定かつ ProjectScope 未設定のため Tool 呼び出しを拒否しました。Conv={ConvId}",
                conversationId);

            result.IsSuccess = false;
            result.ValidationFailedReason = "访问被拒绝：无法确定租户上下文（projectId 未指定）";
            result.ErrorMessage = result.ValidationFailedReason;
            return result;
        }

        try
        {
            // [1] 获取当前 FSM 状态
            var currentState = await _slotFillingManager.GetCurrentFsmStateAsync(conversationId, resolvedProjectId);
            result.FsmState = currentState;

            _logger.LogInformation(
                "[ToolOrchestrator] Tool 调用开始 Conv={ConvId}, State={State}, Project={Project}",
                conversationId,
                currentState,
                resolvedProjectId);

            // [2] Tool 验证 (JSON Schema + Entity 白名单 + SqlSafetyGuard)
            var validationResult = await _validator.ValidateAsync(
                toolCall,
                resolvedProjectId,
                currentState);

            if (!validationResult.IsValid)
            {
                result.IsSuccess = false;
                result.ValidationFailedReason = validationResult.ErrorMessage;
                result.ErrorMessage = $"Tool 验证失败: {validationResult.ErrorMessage}";

                _logger.LogWarning(
                    "[ToolOrchestrator] Tool 验证失败 Conv={ConvId}, Reason={Reason}",
                    conversationId,
                    validationResult.ErrorMessage);

                return result;
            }

            // [3] FSM 状态允许性检查
            var toolName = toolCall["tool_call"]?.ToString();
            if (!string.IsNullOrEmpty(toolName))
            {
                var isAllowed = await _slotFillingManager.IsToolAllowedAsync(conversationId, toolName, resolvedProjectId);
                if (!isAllowed)
                {
                    result.IsSuccess = false;
                    result.ValidationFailedReason = $"当前状态 '{currentState}' 不允许使用 Tool '{toolName}'";
                    result.ErrorMessage = result.ValidationFailedReason;

                    _logger.LogWarning(
                        "[ToolOrchestrator] Tool 状态不允许 Conv={ConvId}, Tool={Tool}, State={State}",
                        conversationId,
                        toolName,
                        currentState);

                    return result;
                }
            }

            // [4] 通过 IToolRegistry 查找并执行 Tool
            var toolDef = !string.IsNullOrEmpty(toolName) ? _toolRegistry.Get(resolvedProjectId, toolName) : null;
            if (toolDef?.ExecuteAsync != null)
            {
                var toolCallResult = await toolDef.ExecuteAsync(toolCall);
                result.IsSuccess = toolCallResult.IsSuccess;
                result.Data = toolCallResult.Data;
                if (!toolCallResult.IsSuccess)
                    result.ErrorMessage = toolCallResult.ErrorMessage;
            }
            else
            {
                // Tool 已通过验证但未注册执行器，视为成功（仅验证模式）
                result.IsSuccess = true;
                result.Data = null;
            }

            _logger.LogInformation(
                "[ToolOrchestrator] Tool 执行成功 Conv={ConvId}, Tool={Tool}",
                conversationId,
                toolName);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[ToolOrchestrator] Tool 执行异常 Conv={ConvId}",
                conversationId);

            result.IsSuccess = false;
            result.ErrorMessage = $"Tool 执行异常: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// 获取当前会话的状态信息和允许的 Tool 列表
    /// </summary>
    public async Task<SessionStateInfo> GetSessionStateAsync(string conversationId, string? projectId = null)
    {
        var resolvedProjectId = projectId;
        if (_projectScope != null && _projectScope.IsSet)
        {
            var activeProject = _projectScope.Current.Name;
            if (string.IsNullOrEmpty(resolvedProjectId))
            {
                resolvedProjectId = activeProject;
            }
            else if (!string.Equals(resolvedProjectId, activeProject, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[ToolOrchestrator] 检测到跨租户越权调用试图！Conv={ConvId}, ActiveTenant={Active}, RequestTenant={Request}",
                    conversationId, activeProject, resolvedProjectId);
                throw new InvalidOperationException("访问被拒绝：不允许跨租户获取会话状态");
            }
        }

        var fsmState = await _slotFillingManager.GetCurrentFsmStateAsync(conversationId, resolvedProjectId);
        var allowedTools = await _slotFillingManager.GetAllowedToolsAsync(conversationId, resolvedProjectId);
        var collectedSlots = await _slotFillingManager.GetCollectedSlotsAsync(conversationId, resolvedProjectId);

        var stateStr = fsmState?.ToString() ?? "Init";
        return new SessionStateInfo
        {
            ConversationId = conversationId,
            FsmState = stateStr,
            IsEscalated = stateStr == "Escalate",
            AllowedTools = allowedTools,
            CollectedSlots = collectedSlots,
            LowConfidenceCount = 0
        };
    }
}
