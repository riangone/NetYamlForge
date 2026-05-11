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

        try
        {
            // [1] 获取当前 FSM 状态
            var currentState = await _slotFillingManager.GetCurrentFsmStateAsync(conversationId);
            result.FsmState = currentState;

            _logger.LogInformation(
                "[ToolOrchestrator] Tool 调用开始 Conv={ConvId}, State={State}",
                conversationId,
                currentState);

            // [2] Tool 验证 (JSON Schema + Entity 白名单 + SqlSafetyGuard)
            var validationResult = await _validator.ValidateAsync(
                toolCall,
                projectId,
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
                var isAllowed = await _slotFillingManager.IsToolAllowedAsync(conversationId, toolName);
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

            // [4] 执行 Tool
            if (toolName == "query_data")
            {
                // 执行查询 - TODO: 集成 QueryExecutionService
                result.Data = new { success = false, message = "query_data 工具暂未实现" };
                _logger.LogWarning("query_data 工具调用但未实现，ToolName={ToolName}", toolName);
            }
            else if (toolName == "send_email")
            {
                // 发送邮件 - TODO: 集成邮件服务
                _logger.LogWarning("send_email 工具调用但未实现，ToolName={ToolName}", toolName);
            }
            else
            {
                // 其他工具
                _logger.LogWarning("未知工具: {ToolName}", toolName);
            }
            // 目前返回验证通过的结果,实际执行需要调用现有的 QueryExecutionService 等
            result.IsSuccess = true;
            result.Data = null; // TODO: 实际的 Tool 执行结果

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
            LowConfidenceCount = 0 // TODO: 从 FSM 获取
        };
    }
}
