using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace AutoDealer.Hooks;

/// <summary>
/// AI 对话审计日志钩子
/// 
/// 功能:
/// 1. 记录 AI 对话的创建/更新操作到 audit_log 表
/// 2. 捕获意图识别结果、槽位收集状态、Tool 调用元数据
/// 3. 记录用户会话 ID、操作时间、AI 工具类型
/// 
/// YAML 配置示例:
///   hooks:
///     afterCreate: "ai_audit_log"
///     afterUpdate: "ai_audit_log"
/// </summary>
public class AiAuditLogHook : IEntityHook
{
    private readonly ILogger<AiAuditLogHook> _logger;

    public AiAuditLogHook(ILogger<AiAuditLogHook> logger)
    {
        _logger = logger;
    }

    public string Name => "ai_audit_log";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        try
        {
            // 构建审计详情
            var detail = BuildAuditDetail(ctx);

            // 插入审计日志
            const string sql = @"
INSERT INTO audit_log (
    user_name,
    action,
    entity,
    entity_id,
    detail,
    ip_address,
    created_at
) VALUES (
    @UserName,
    @Action,
    @Entity,
    @EntityId,
    @Detail,
    @IpAddress,
    @CreatedAt
)";

            var param = new
            {
                UserName = ctx.UserName ?? "ai_system",
                Action = ctx.Operation.ToString(),
                Entity = ctx.Entity,
                EntityId = ctx.Id?.ToString() ?? ctx.Values.GetValueOrDefault("conversation_id")?.ToString(),
                Detail = detail,
                IpAddress = ctx.Data.GetValueOrDefault("ip_address")?.ToString() ?? "N/A",
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };

            await db.ExecuteAsync(sql, param, tx);

            _logger.LogInformation(
                "[Hook:ai_audit_log] AI 对话审计记录 - {Entity} {Operation}, ID={Id}",
                ctx.Entity,
                ctx.Operation,
                param.EntityId);
        }
        catch (Exception ex)
        {
            // 审计失败不应阻断主流程,仅记录错误
            _logger.LogError(ex, "[Hook:ai_audit_log] 审计日志记录失败:{Entity} {Operation}",
                ctx.Entity,
                ctx.Operation);
        }
    }

    /// <summary>
    /// 构建审计详情 JSON 字符串
    /// </summary>
    private static string BuildAuditDetail(EntityHookContext ctx)
    {
        var details = new Dictionary<string, object?>
        {
            ["operation"] = ctx.Operation.ToString(),
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            ["user"] = ctx.UserName ?? "ai_system"
        };

        // 根据操作类型捕获关键数据
        if (ctx.Operation == CrudOperation.Create)
        {
            details["conversation_id"] = ctx.Values.GetValueOrDefault("conversation_id");
            details["intent"] = ctx.Values.GetValueOrDefault("detected_intent");
            details["ai_tool"] = ctx.Values.GetValueOrDefault("ai_tool_used");
            details["confidence"] = ctx.Values.GetValueOrDefault("last_confidence");
        }
        else if (ctx.Operation == CrudOperation.Update)
        {
            details["conversation_id"] = ctx.Id?.ToString();
            details["state_transition"] = ctx.Data.GetValueOrDefault("state_transition");
            details["slots_collected"] = ctx.Data.GetValueOrDefault("slots_snapshot");
            details["tool_calls_count"] = ctx.Data.GetValueOrDefault("tool_calls_count");
        }

        // 序列化(简化版,实际可使用 System.Text.Json)
        return string.Join(", ", details.Select(kv => $"{kv.Key}: {kv.Value}"));
    }
}
