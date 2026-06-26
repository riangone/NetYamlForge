using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks;

/// <summary>
/// AI 对话后自动更新销售线索评分钩子
/// 
/// 功能:
/// 1. AI 对话完成后自动更新线索的 ai_touch_count
/// 2. 根据意图类型和情感分析调整 lead_score
/// 3. 记录首次/末次触达对话 ID
/// 
/// YAML 配置示例:
///   hooks:
///     afterCreate: "ai_lead_scoring"
/// </summary>
public class AiLeadScoringHook : IEntityHook
{
    public string Name => "ai_lead_scoring";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.FromResult(HookResult.Continue());

    public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        try
        {
            // 获取关联的线索 ID(从 ctx.Data 或 ctx.Values)
            ctx.Data.TryGetValue("related_lead_id", out var leadIdObj);
            var leadId = leadIdObj?.ToString();
            if (string.IsNullOrEmpty(leadId) || !int.TryParse(leadId, out var leadIdInt))
            {
                return; // 无关联线索,跳过
            }

            // 计算评分增量
            var scoreDelta = CalculateScoreDelta(ctx);
            ctx.Data.TryGetValue("previous_touch_count", out var touchCountObj);
            var touchCount = touchCountObj as int? ?? 0;

            // 更新线索表
            const string sql = @"
UPDATE sales_leads
SET
    ai_touch_count = COALESCE(ai_touch_count, 0) + 1,
    lead_score = MIN(100, MAX(0, COALESCE(lead_score, 50) + @ScoreDelta)),
    ai_last_touch_conversation_id = @ConversationId,
    ai_first_touch_conversation_id = COALESCE(ai_first_touch_conversation_id, @ConversationId),
    updated_at = @UpdatedAt
WHERE id = @LeadId";

            var param = new
            {
                ScoreDelta = scoreDelta,
                ConversationId = ctx.Id?.ToString() ?? (ctx.Values.TryGetValue("conversation_id", out var conv) ? conv : null),
                UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                LeadId = leadIdInt
            };

            await db.ExecuteAsync(sql, param, tx);
        }
        catch (Exception)
        {
            // 评分失败不阻断主流程
            // 实际项目中应记录到错误队列或重试队列
        }
    }

    /// <summary>
    /// 根据对话内容计算评分增量
    /// </summary>
    private static int CalculateScoreDelta(EntityHookContext ctx)
    {
        var delta = 0;

        // 意图评分
        ctx.Values.TryGetValue("detected_intent", out var intentVal);
        var intent = intentVal?.ToString();
        delta += intent switch
        {
            "test_drive_request" => 15,
            "price_inquiry" => 10,
            "vehicle_comparison" => 12,
            "finance_inquiry" => 8,
            "general_inquiry" => 3,
            _ => 0
        };

        // 情感分析加分
        if (ctx.Data.TryGetValue("sentiment_score", out var sentiment) &&
            double.TryParse(sentiment?.ToString(), out var score))
        {
            if (score > 0.7) delta += 5;  // 正面情感
            else if (score < 0.3) delta -= 3; // 负面情感
        }

        // 槽位完成度加分
        ctx.Data.TryGetValue("slots_filled", out var slotsObj);
        var slotsCount = slotsObj as int? ?? 0;
        if (slotsCount >= 5) delta += 10; // 信息收集完整

        return delta;
    }
}
