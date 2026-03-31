# 新增实体定义 - 汽车销售 AI 增强功能

## 1. 线索培育任务 (lead_nurturing_tasks.yml)

```yaml
imports: []
entities:
  lead_nurturing_tasks:
    table: lead_nurturing_tasks
    key: task_id
    displayName: 线索培育任务
    description: AI 自动生成的客户跟进任务，帮助销售团队提高转化率
    softDelete: false
    isPublic: true

    columns:
      task_id:
        type: string
        length: 50
        required: true
        label: 任务 ID
        searchable: true
        sortable: true
      lead_id:
        type: string
        length: 50
        required: true
        label: 线索 ID
        searchable: true
        sortable: true
      customer_id:
        type: string
        length: 50
        required: true
        label: 客户 ID
        searchable: true
      task_type:
        type: string
        length: 30
        required: true
        label: 任务类型
        options:
          - followup_call        # 跟进电话
          - send_info           # 发送资料
          - test_drive_invite   # 试驾邀请
          - special_offer       # 特别优惠
          - competitor_counter  # 竞品应对
          - birthday_greeting   # 生日祝福
          - maintenance_reminder # 保养提醒
        searchable: true
        sortable: true
      trigger_reason:
        type: string
        length: 500
        required: false
        label: 触发原因
        description: AI 分析的任务触发原因
      priority_score:
        type: int
        required: true
        label: 优先级评分
        default: 50
        description: 0-100，AI 根据客户价值、紧急度计算
        sortable: true
      status:
        type: string
        length: 20
        required: true
        label: 状态
        default: pending
        options:
          - pending
          - in_progress
          - completed
          - cancelled
        searchable: true
        sortable: true
      assigned_to:
        type: string
        length: 50
        required: false
        label: 负责人
        description: 负责执行的销售人员 ID
        searchable: true
      ai_recommendation:
        type: text
        required: false
        label: AI 建议话术
        description: AI 生成的推荐沟通话术
      context_data:
        type: text
        required: false
        label: 上下文数据
        description: JSON 格式，包含相关车辆、客户偏好等
      completed_at:
        type: datetime
        required: false
        label: 完成时间
        sortable: true
      completed_by:
        type: string
        length: 50
        required: false
        label: 完成人
      created_at:
        type: datetime
        required: true
        label: 创建时间
        sortable: true
      updated_at:
        type: datetime
        required: true
        label: 更新时间
        sortable: true

    hooks:
      beforeCreate:
        - set_task_timestamps
        - calculate_task_priority
        - generate_ai_recommendation
      beforeUpdate:
        - update_task_timestamps

    forms:
      task_id:
        type: string
        required: true
        label: 任务 ID
        editable: false
      lead_id:
        type: string
        required: true
        label: 线索 ID
        editable: true
      task_type:
        type: select
        required: true
        label: 任务类型
        editable: true
      priority_score:
        type: number
        required: true
        label: 优先级
        editable: true
      status:
        type: select
        required: true
        label: 状态
        editable: true
      assigned_to:
        type: string
        required: false
        label: 负责人
        editable: true
      ai_recommendation:
        type: textarea
        required: false
        label: AI 建议
        editable: true
        rows: 5

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true

    list:
      columns:
        - task_id
        - task_type
        - priority_score
        - status
        - assigned_to
        - created_at
      defaultSort:
        field: priority_score
        dir: desc
```

---

## 2. 销售话术库 (ai_sales_scripts.yml)

```yaml
imports: []
entities:
  ai_sales_scripts:
    table: ai_sales_scripts
    key: script_id
    displayName: AI 销售话术库
    description: AI 生成和优化的销售话术，包含场景、对象、车型等维度
    softDelete: false
    isPublic: true

    columns:
      script_id:
        type: string
        length: 50
        required: true
        label: 话术 ID
        searchable: true
      scenario:
        type: string
        length: 30
        required: true
        label: 销售场景
        options:
          - initial_contact      # 初次接触
          - needs_analysis       # 需求分析
          - vehicle_presentation # 车辆介绍
          - test_drive_invite    # 试驾邀请
          - price_negotiation    # 价格谈判
          - competitor_comparison # 竞品对比
          - objection_handling   # 异议处理
          - closing              # 成交促成
          - follow_up            # 跟进
        searchable: true
        sortable: true
      customer_persona:
        type: string
        length: 30
        required: false
        label: 客户画像
        options:
          - first_time_buyer     # 首次购车
          - trade_in             # 置换购车
          - competitor_owner     # 竞品车主
          - luxury_buyer         # 豪华车买家
          - budget_conscious     # 预算敏感
          - family_buyer         # 家庭用户
          - business_buyer       # 商务用户
        searchable: true
      vehicle_category:
        type: string
        length: 30
        required: false
        label: 车辆类型
        options:
          - sedan
          - suv
          - minivan
          - kei_car
          - ev
          - hybrid
          - luxury
        searchable: true
      script_content:
        type: text
        required: true
        label: 话术内容
        description: 完整的销售话术文本
      key_points:
        type: text
        required: false
        label: 关键要点
        description: 话术的核心要点列表
      objection_handling:
        type: text
        required: false
        label: 异议处理
        description: 常见异议的应对方法
      success_count:
        type: int
        required: true
        label: 成功次数
        default: 0
        sortable: true
      failure_count:
        type: int
        required: true
        label: 失败次数
        default: 0
      effectiveness_rate:
        type: decimal
        precision: 5
        scale: 4
        required: false
        label: 有效率
        description: 成功次数/(成功 + 失败)
        sortable: true
      tags:
        type: text
        required: false
        label: 标签
        description: 逗号分隔的标签
      created_at:
        type: datetime
        required: true
        label: 创建时间
        sortable: true
      updated_at:
        type: datetime
        required: true
        label: 更新时间
        sortable: true
      last_used_at:
        type: datetime
        required: false
        label: 最后使用时间
        sortable: true

    hooks:
      beforeCreate:
        - set_script_timestamps
        - calculate_effectiveness_rate
      beforeUpdate:
        - update_script_timestamps
        - recalculate_effectiveness_rate

    forms:
      script_id:
        type: string
        required: true
        label: 话术 ID
        editable: false
      scenario:
        type: select
        required: true
        label: 销售场景
        editable: true
      script_content:
        type: textarea
        required: true
        label: 话术内容
        editable: true
        rows: 8
      key_points:
        type: textarea
        required: false
        label: 关键要点
        editable: true
        rows: 4
      objection_handling:
        type: textarea
        required: false
        label: 异议处理
        editable: true
        rows: 4

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true

    list:
      columns:
        - script_id
        - scenario
        - customer_persona
        - vehicle_category
        - effectiveness_rate
        - success_count
        - last_used_at
      defaultSort:
        field: effectiveness_rate
        dir: desc
```

---

## 3. 客户行为分析 (customer_behavior_scores.yml)

```yaml
imports: []
entities:
  customer_behavior_scores:
    table: customer_behavior_scores
    key: score_id
    displayName: 客户行为评分
    description: AI 分析客户行为生成的多维度评分，用于精准营销
    softDelete: false
    isPublic: true

    columns:
      score_id:
        type: string
        length: 50
        required: true
        label: 评分 ID
        searchable: true
      customer_id:
        type: string
        length: 50
        required: true
        label: 客户 ID
        searchable: true
        sortable: true
      engagement_score:
        type: int
        required: true
        label: 参与度评分
        default: 0
        description: 0-100，基于浏览、点击、咨询等行为
        sortable: true
      purchase_intent_score:
        type: int
        required: true
        label: 购买意向评分
        default: 0
        description: 0-100，基于询价、试驾、对比等行为
        sortable: true
      price_sensitivity_score:
        type: int
        required: true
        label: 价格敏感度
        default: 50
        description: 0-100，越高表示越关注价格
        sortable: true
      brand_loyalty_score:
        type: int
        required: true
        label: 品牌忠诚度
        default: 50
        description: 0-100，基于历史购买和浏览偏好
        sortable: true
      churn_risk_score:
        type: int
        required: true
        label: 流失风险
        default: 0
        description: 0-100，越高风险越大
        sortable: true
      preferred_channel:
        type: string
        length: 20
        required: false
        label: 偏好渠道
        options:
          - phone
          - email
          - line
          - sms
          - web_chat
      preferred_contact_time:
        type: string
        length: 50
        required: false
        label: 偏好联系时间
        description: 如「工作日 14:00-16:00」
      vehicle_interests:
        type: text
        required: false
        label: 关注车型
        description: JSON 数组，包含关注的车型 ID
      browsing_patterns:
        type: text
        required: false
        label: 浏览模式
        description: JSON 对象，包含浏览习惯分析
      last_calculated_at:
        type: datetime
        required: true
        label: 最后计算时间
        sortable: true
      created_at:
        type: datetime
        required: true
        label: 创建时间
        sortable: true
      updated_at:
        type: datetime
        required: true
        label: 更新时间
        sortable: true

    hooks:
      beforeCreate:
        - set_score_timestamps
        - validate_score_ranges
      beforeUpdate:
        - update_score_timestamps
        - validate_score_ranges

    forms:
      customer_id:
        type: string
        required: true
        label: 客户 ID
        editable: true
      engagement_score:
        type: number
        required: true
        label: 参与度
        editable: true
      purchase_intent_score:
        type: number
        required: true
        label: 购买意向
        editable: true
      churn_risk_score:
        type: number
        required: true
        label: 流失风险
        editable: true

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true

    list:
      columns:
        - customer_id
        - engagement_score
        - purchase_intent_score
        - churn_risk_score
        - last_calculated_at
      defaultSort:
        field: purchase_intent_score
        dir: desc
```

---

## 4. 竞品情报 (competitor_intelligence.yml)

```yaml
imports: []
entities:
  competitor_intelligence:
    table: competitor_intelligence
    key: intel_id
    displayName: 竞品情报库
    description: 竞争对手车型信息、价格、促销活动等情报
    softDelete: false
    isPublic: true

    columns:
      intel_id:
        type: string
        length: 50
        required: true
        label: 情报 ID
        searchable: true
      competitor_brand:
        type: string
        length: 50
        required: true
        label: 竞争品牌
        searchable: true
        sortable: true
      competitor_model:
        type: string
        length: 50
        required: true
        label: 竞争车型
        searchable: true
        sortable: true
      our_counter_model:
        type: string
        length: 50
        required: false
        label: 我方对应车型
        description: 推荐用来对抗的车型
      price_difference:
        type: decimal
        precision: 10
        scale: 2
        required: false
        label: 价格差异
        description: 竞品价格 - 我方价格
        sortable: true
      advantage_points:
        type: text
        required: false
        label: 我方优势
        description: JSON 数组，列出我方优势点
      disadvantage_points:
        type: text
        required: false
        label: 我方劣势
        description: JSON 数组，列出我方劣势
      counter_arguments:
        type: text
        required: false
        label: 应对话术
        description: 针对竞品优势的应对策略
      promotion_info:
        type: text
        required: false
        label: 促销信息
        description: 竞品当前促销活动
      spec_comparison:
        type: text
        required: false
        label: 规格对比
        description: JSON 对象，详细规格对比
      source_url:
        type: string
        length: 500
        required: false
        label: 信息来源
      confidence_level:
        type: string
        length: 20
        required: false
        label: 可信度
        options:
          - high
          - medium
          - low
      collected_at:
        type: datetime
        required: true
        label: 收集时间
        sortable: true
      created_at:
        type: datetime
        required: true
        label: 创建时间
        sortable: true
      updated_at:
        type: datetime
        required: true
        label: 更新时间
        sortable: true

    hooks:
      beforeCreate:
        - set_intel_timestamps
      beforeUpdate:
        - update_intel_timestamps

    forms:
      competitor_brand:
        type: string
        required: true
        label: 竞争品牌
        editable: true
      competitor_model:
        type: string
        required: true
        label: 竞争车型
        editable: true
      our_counter_model:
        type: string
        required: false
        label: 对应车型
        editable: true
      counter_arguments:
        type: textarea
        required: false
        label: 应对话术
        editable: true
        rows: 5

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true

    list:
      columns:
        - competitor_brand
        - competitor_model
        - our_counter_model
        - price_difference
        - updated_at
      defaultSort:
        field: updated_at
        dir: desc
```

---

## 5. 试驾管理增强 (test_drive_tracking.yml)

```yaml
imports: []
entities:
  test_drive_tracking:
    table: test_drive_tracking
    key: tracking_id
    displayName: 试驾追踪记录
    description: 详细记录试驾过程和客户反馈，用于后续跟进
    softDelete: false
    isPublic: true

    columns:
      tracking_id:
        type: string
        length: 50
        required: true
        label: 追踪 ID
        searchable: true
      appointment_id:
        type: string
        length: 50
        required: true
        label: 预约 ID
        searchable: true
      customer_id:
        type: string
        length: 50
        required: true
        label: 客户 ID
        searchable: true
      vehicle_id:
        type: string
        length: 50
        required: true
        label: 试驾车辆 ID
        searchable: true
      test_drive_date:
        type: datetime
        required: true
        label: 试驾日期
        sortable: true
      route_taken:
        type: string
        length: 200
        required: false
        label: 试驾路线
      duration_minutes:
        type: int
        required: false
        label: 试驾时长（分钟）
      sales_rep_id:
        type: string
        length: 50
        required: false
        label: 陪同销售
        searchable: true
      customer_feedback:
        type: text
        required: false
        label: 客户反馈
        description: 客户对试驾的评价
      positive_points:
        type: text
        required: false
        label: 满意点
        description: JSON 数组，客户满意的方面
      concerns:
        type: text
        required: false
        label: 顾虑点
        description: JSON 数组，客户的顾虑
      purchase_intent_after:
        type: string
        length: 20
        required: false
        label: 试驾后意向
        options:
          - very_high
          - high
          - medium
          - low
          - very_low
      next_action:
        type: string
        length: 200
        required: false
        label: 下一步行动
      follow_up_date:
        type: datetime
        required: false
        label: 跟进日期
      created_at:
        type: datetime
        required: true
        label: 创建时间
        sortable: true
      updated_at:
        type: datetime
        required: true
        label: 更新时间
        sortable: true

    hooks:
      beforeCreate:
        - set_tracking_timestamps
        - auto_create_followup_task
      beforeUpdate:
        - update_tracking_timestamps

    forms:
      appointment_id:
        type: string
        required: true
        label: 预约 ID
        editable: true
      customer_id:
        type: string
        required: true
        label: 客户 ID
        editable: true
      vehicle_id:
        type: string
        required: true
        label: 试驾车辆
        editable: true
      customer_feedback:
        type: textarea
        required: false
        label: 客户反馈
        editable: true
        rows: 4
      purchase_intent_after:
        type: select
        required: false
        label: 试驾后意向
        editable: true
      next_action:
        type: string
        required: false
        label: 下一步行动
        editable: true

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true

    list:
      columns:
        - tracking_id
        - customer_id
        - vehicle_id
        - test_drive_date
        - purchase_intent_after
        - follow_up_date
      defaultSort:
        field: test_drive_date
        dir: desc
```

---

## 使用说明

### 创建实体文件

将上述 YAML 内容分别保存为：

```
NetYamlForge/projects/auto-dealer-demo/entities/
├── lead_nurturing_tasks.yml
├── ai_sales_scripts.yml
├── customer_behavior_scores.yml
├── competitor_intelligence.yml
└── test_drive_tracking.yml
```

### 创建钩子代码

在 `NetYamlForge/projects/auto-dealer-demo/Hooks/AutoDealerHooks.cs` 中添加：

```csharp
/// <summary>
/// 线索培育任务时间戳设置
/// </summary>
public class SetTaskTimestampsHook : IEntityHook
{
    public string Name => "set_task_timestamps";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        if (!ctx.Values.ContainsKey("created_at") || ctx.Values["created_at"] == null)
            ctx.Values["created_at"] = now;
        if (!ctx.Values.ContainsKey("updated_at") || ctx.Values["updated_at"] == null)
            ctx.Values["updated_at"] = now;
        if (!ctx.Values.ContainsKey("status") || ctx.Values["status"] == null)
            ctx.Values["status"] = "pending";

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 计算任务优先级（AI 逻辑）
/// </summary>
public class CalculateTaskPriorityHook : IEntityHook
{
    public string Name => "calculate_task_priority";

    public Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 从线索获取基础评分
        if (ctx.Values.TryGetValue("lead_id", out var leadId) && leadId != null)
        {
            var lead = db.QueryFirstOrDefault(@"
                SELECT lead_score, status, created_at 
                FROM sales_leads 
                WHERE lead_id = @leadId", 
                new { leadId });
            
            if (lead != null)
            {
                var baseScore = lead.lead_score ?? 50;
                var daysOld = (DateTime.Now - DateTime.Parse(lead.created_at)).Days;
                
                // 时间越久优先级越高
                var timeBonus = Math.Min(daysOld * 2, 30);
                
                // 根据任务类型调整
                var taskType = ctx.Values.GetValueOrDefault("task_type")?.ToString();
                var typeMultiplier = taskType switch
                {
                    "test_drive_invite" => 1.2,
                    "special_offer" => 1.1,
                    "followup_call" => 1.0,
                    _ => 1.0
                };
                
                var finalScore = (int)Math.Min(100, (baseScore + timeBonus) * typeMultiplier);
                ctx.Values["priority_score"] = finalScore;
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}

/// <summary>
/// 生成 AI 建议话术
/// </summary>
public class GenerateAiRecommendationHook : IEntityHook
{
    public string Name => "generate_ai_recommendation";

    public async Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
    {
        // 这里可以调用 AI 服务生成话术
        // 简化示例：根据任务类型返回模板
        
        if (ctx.Values.TryGetValue("task_type", out var taskType) && taskType != null)
        {
            var template = taskType switch
            {
                "followup_call" => "您好，我是 XX 车行的 XXX。上次您咨询的车型，最近有特别优惠活动，想跟您分享一下...",
                "test_drive_invite" => "您好，您关注的车型已经到店了。本周六有试驾会，可以亲身体验一下，您方便参加吗？",
                "special_offer" => "好消息！您关注的车型本月有特别优惠，首付 10% 即可提车，还有 3 年免息贷款...",
                _ => null
            };
            
            if (!string.IsNullOrEmpty(template))
            {
                ctx.Values["ai_recommendation"] = template;
            }
        }

        return await Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
        => Task.CompletedTask;
}
```

### 注册钩子

在 YAML 的 `hooks` 部分引用这些钩子（已在 YAML 中定义）。
