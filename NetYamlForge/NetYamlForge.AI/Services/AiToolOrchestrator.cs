using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Threading;
using Json.Schema;
using Microsoft.Extensions.Logging;
using NetYamlForge.AI.Services.ToolValidation;

namespace NetYamlForge.AI.Services;

/// <summary>
/// AI Tool 调用编排服务
/// </summary>
public interface IAiToolOrchestrator
{
    Task<ToolExecutionResult> ValidateAndExecuteToolAsync(
        JsonNode toolCall,
        string conversationId,
        string projectId,
        CancellationToken ct = default);

    Task<SessionStateInfo> GetSessionStateAsync(string conversationId);
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
    public AppointmentStateMachine.State? FsmState { get; set; }
}

/// <summary>
/// 会话状态信息
/// </summary>
public class SessionStateInfo
{
    public string ConversationId { get; set; } = string.Empty;
    public AppointmentStateMachine.State FsmState { get; set; }
    public HashSet<string> AllowedTools { get; set; } = new();
    public Dictionary<string, string> CollectedSlots { get; set; } = new();
    public int LowConfidenceCount { get; set; }
    public bool IsEscalated => FsmState == AppointmentStateMachine.State.Escalate;
}

/// <summary>
/// AI Tool 调用编排服务实现
/// </summary>
public class AiToolOrchestrator : IAiToolOrchestrator
{
    private readonly ToolCallValidator _validator;
    private readonly ISlotFillingManager _slotFillingManager;
    private readonly ILogger<AiToolOrchestrator> _logger;

    public AiToolOrchestrator(
        ToolCallValidator validator,
        ISlotFillingManager slotFillingManager,
        ILogger<AiToolOrchestrator> logger)
    {
        _validator = validator;
        _slotFillingManager = slotFillingManager;
        _logger = logger;
    }

    public async Task<ToolExecutionResult> ValidateAndExecuteToolAsync(
        JsonNode toolCall,
        string conversationId,
        string projectId,
        CancellationToken ct = default)
    {
        var result = new ToolExecutionResult();

        try
        {
            var currentState = await _slotFillingManager.GetCurrentFsmStateAsync(conversationId);
            result.FsmState = currentState;

            _logger.LogInformation(
                "[ToolOrchestrator] Tool 调用开始 Conv={ConvId}, State={State}",
                conversationId,
                currentState);

            var validationResult = await _validator.ValidateAsync(
                toolCall,
                projectId,
                currentState);

            if (!validationResult.IsValid)
            {
                result.IsSuccess = false;
                result.ValidationFailedReason = validationResult.ErrorMessage;
                result.ErrorMessage = $"Tool 验证失败: {validationResult.ErrorMessage}";
                return result;
            }

            var toolName = toolCall["tool_call"]?.ToString();
            if (!string.IsNullOrEmpty(toolName))
            {
                var isAllowed = await _slotFillingManager.IsToolAllowedAsync(conversationId, toolName);
                if (!isAllowed)
                {
                    result.IsSuccess = false;
                    result.ValidationFailedReason = $"当前状态 '{currentState}' 不允许使用 Tool '{toolName}'";
                    result.ErrorMessage = result.ValidationFailedReason;
                    return result;
                }
            }

            if (toolName == "query_data")
            {
                var toolParams = toolCall["tool_params"]?.AsObject()?.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value?.ToString() ?? string.Empty) ?? new Dictionary<string, string>();

                result.Data = await ExecuteQueryToolAsync(toolParams, projectId, ct);
            }
            else if (toolName == "send_email")
            {
                var toolParams = toolCall["tool_params"]?.AsObject()?.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value?.ToString() ?? string.Empty) ?? new Dictionary<string, string>();

                await ExecuteSendEmailToolAsync(toolParams, projectId, ct);
            }
            else
            {
                _logger.LogWarning("未知工具: {ToolName}", toolName);
            }

            result.IsSuccess = true;

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

    public async Task<SessionStateInfo> GetSessionStateAsync(string conversationId)
    {
        var fsmState = await _slotFillingManager.GetCurrentFsmStateAsync(conversationId);
        var allowedTools = await _slotFillingManager.GetAllowedToolsAsync(conversationId);
        var collectedSlots = await _slotFillingManager.GetCollectedSlotsAsync(conversationId);

        return new SessionStateInfo
        {
            ConversationId = conversationId,
            FsmState = fsmState ?? AppointmentStateMachine.State.Init,
            AllowedTools = allowedTools,
            CollectedSlots = collectedSlots,
            LowConfidenceCount = _slotFillingManager != null ? await _slotFillingManager.GetLowConfidenceCountAsync(conversationId) : 0
        };
    }

    private async Task<object> ExecuteQueryToolAsync(Dictionary<string, string> toolParams, string projectId, CancellationToken ct)
    {
        // TODO: 实现实际的查询逻辑，调用 QueryExecutionService
        return new { success = false, message = "ExecuteQueryToolAsync not implemented" };
    }

    private async Task ExecuteSendEmailToolAsync(Dictionary<string, string> toolParams, string projectId, CancellationToken ct)
    {
        // TODO: 实现实际的邮件发送逻辑，调用 EmailChannelService
        await Task.CompletedTask;
    }
}
