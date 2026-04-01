# 汽车销售 AI 智能化提升方案 - 分阶段实施计划

> **文档版本**: 1.0  
> **最后更新**: 2026 年 3 月 31 日  
> **项目**: 自動車ディーラー AI 窓口システム

---

## 📊 执行摘要

### 当前系统状态

**已有基础**：
- ✅ 11 个核心实体（客户、线索、车辆、预约等）
- ✅ 基础 AI 对话能力（ai_conversations, ai_messages）
- ✅ 线索评分和状态管理
- ✅ 客户行为追踪（lead_activities）
- ✅ 多角色仪表板系统

**改进方向**：
- ⚠️ AI 仅被动响应查询，缺少主动销售能力
- ⚠️ 缺少线索培育自动化机制
- ⚠️ 缺少个性化推荐引擎
- ⚠️ 缺少销售预测和流失预警

---

## 🎯 实施路线图总览

```
┌─────────────────────────────────────────────────────────────────────┐
│                        10 周智能化提升计划                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  第 1-2 周          第 3-4 周          第 5-7 周          第 8-10 周      │
│  ┌─────────┐       ┌─────────┐       ┌─────────┐       ┌─────────┐  │
│  │ 阶段一   │       │ 阶段二   │       │ 阶段三   │       │ 阶段四   │  │
│  │ 基础建设 │  ──→  │ 客户体验 │  ──→  │ 数据决策 │  ──→  │ AI 扩展   │  │
│  │         │       │         │       │         │       │         │  │
│  │ • 任务  │       │ • 试驾  │       │ • 预测  │       │ • 图像  │  │
│  │ • 推荐  │       │ • PDF   │       │ • 预警  │       │ • 语音  │  │
│  │ • 话术  │       │ • 估价  │       │ • 竞品  │       │ • 学习  │  │
│  └─────────┘       └─────────┘       └─────────┘       └─────────┘  │
│                                                                     │
│  投入：¥50 万          投入：¥40 万         投入：¥60 万        投入：¥80 万   │
│  ROI: 150%          ROI: 120%         ROI: 180%        ROI: 200%   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 📦 第一阶段：智能销售助手（第 1-2 周）

### 目标
将 AI 从"被动查询"升级为"主动建议"，实现线索培育自动化。

### 预期效果
- 线索跟进及时率：85% → 98%
- 线索转化率：15% → 22%
- 销售人均效率：+40%

---

### 任务 1.1：创建线索培育任务实体

**文件**: `entities/lead_nurturing_tasks.yml`

```yaml
imports: []
entities:
  lead_nurturing_tasks:
    table: lead_nurturing_tasks
    key: task_id
    displayName: 线索培育任务
    description: AI 自动生成的客户跟进任务，包含推荐话术和优先级
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
        sortable: true
      
      task_type:
        type: string
        length: 30
        required: true
        label: 任务类型
        options:
          - followup_call        # 跟进电话
          - send_info           # 发送资料
          - test_drive_invite   # 试驾邀请
          - special_offer       # 特别优惠通知
          - competitor_counter  # 竞品应对
          - price_alert         # 价格变动提醒
        searchable: true
        sortable: true
      
      trigger_reason:
        type: string
        length: 300
        required: false
        label: 触发原因
        description: AI 自动生成，说明为何创建此任务
        searchable: true
      
      priority_score:
        type: int
        required: true
        label: 优先级评分
        description: AI 计算 0-100 分，分数越高越紧急
        default: 50
        sortable: true
      
      status:
        type: string
        length: 20
        required: true
        label: 状态
        options:
          - pending      # 待处理
          - in_progress  # 进行中
          - completed    # 已完成
          - cancelled    # 已取消
        default: pending
        searchable: true
        sortable: true
      
      assigned_to:
        type: string
        length: 50
        required: false
        label: 负责人
        description: 销售人员 ID
        searchable: true
        sortable: true
      
      ai_recommendation:
        type: text
        required: false
        label: AI 推荐话术
        description: AI 生成的具体沟通话术和要点
      
      ai_reasoning:
        type: text
        required: false
        label: AI 推荐理由
        description: 解释为何推荐此行动
      
      due_date:
        type: datetime
        required: false
        label: 截止日期
        sortable: true
      
      completed_at:
        type: datetime
        required: false
        label: 完成时间
        sortable: true
      
      completed_by:
        type: string
        length: 50
        required: false
        label: 完成者
      
      result_notes:
        type: text
        required: false
        label: 结果记录
        description: 销售人员填写的任务执行结果
      
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
      
      customer_id:
        type: string
        required: true
        label: 客户 ID
        editable: true
      
      task_type:
        type: select
        required: true
        label: 任务类型
        editable: true
      
      priority_score:
        type: number
        required: true
        label: 优先级评分
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
        label: AI 推荐话术
        editable: true
        rows: 5
      
      due_date:
        type: datetime
        required: false
        label: 截止日期
        editable: true
      
      result_notes:
        type: textarea
        required: false
        label: 结果记录
        editable: true
        rows: 4

    hooks:
      beforeCreate:
        - set_nurturing_task_timestamps
        - calculate_nurturing_priority
      beforeUpdate:
        - update_nurturing_task_timestamps

    list:
      titleColumns:
        - task_id
        - task_type
        - priority_score
        - status
        - due_date
      defaultSort:
        field: priority_score
        dir: desc
      filters:
        - field: status
          default: pending
        - field: assigned_to
          type: select

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true
```

---

### 任务 1.2：创建批处理作业 - 线索培育生成器

**文件**: `jobs/lead_nurturing_generator.yml`

```yaml
name: lead_nurturing_generator
displayName: "线索培育任务生成器"
description: "每日 9:00 扫描客户行为，AI 自动生成跟进任务"
schedule: "0 9 * * *"  # 每天 9:00 执行
timeout: 300
enabled: true

# 执行环境配置
environment:
  LOG_LEVEL: Information
  BATCH_SIZE: 100

tasks:
  # ─── 任务 1: 检测高意向客户 ───
  - name: detect_high_intent_customers
    type: script
    description: "浏览特定车型 3 次以上的高意向客户"
    script: |
      -- 过去 7 天内浏览同一车型 3 次以上的客户
      INSERT OR IGNORE INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, due_date)
      SELECT
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sl.customer_id,
        'test_drive_invite',
        '客户在过去 7 天内浏览 ' || la.vehicle_interest || ' 达 ' || COUNT(*) || ' 次，意向度高',
        MIN(50 + COUNT(*) * 10, 95),
        'pending',
        datetime('now', '+1 day')
      FROM lead_activities la
      JOIN sales_leads sl ON la.lead_id = sl.lead_id
      WHERE la.activity_type = 'vehicle_view'
        AND la.created_at >= datetime('now', '-7 days')
        AND sl.status IN ('new', 'contacted')
      GROUP BY la.lead_id, la.vehicle_interest
      HAVING COUNT(*) >= 3
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt
          WHERE lnt.lead_id = la.lead_id
            AND lnt.task_type = 'test_drive_invite'
            AND lnt.status = 'pending'
        );

  # ─── 任务 2: 检测长时间未联系线索 ───
  - name: detect_stale_leads
    type: script
    description: "超过 3 天未联系的新线索"
    script: |
      INSERT OR IGNORE INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, due_date)
      SELECT
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        lead_id,
        customer_id,
        'followup_call',
        '新线索创建后 ' || CAST(julianday('now') - julianday(created_at) AS INT) || ' 天未联系',
        MIN(lead_score + 20, 90),
        'pending',
        datetime('now', '+1 day')
      FROM sales_leads
      WHERE status = 'new'
        AND last_contact_at IS NULL
        AND julianday('now') - julianday(created_at) >= 3
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt
          WHERE lnt.lead_id = sales_leads.lead_id
            AND lnt.status = 'pending'
        );

  # ─── 任务 3: 试驾后跟进 ───
  - name: post_test_drive_followup
    type: script
    description: "试驾完成后 7 天未跟进的客户"
    script: |
      INSERT OR IGNORE INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, due_date)
      SELECT
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sa.customer_id,
        'followup_call',
        '试驾完成于 ' || sa.completed_at || '，已过去 ' || 
          CAST(julianday('now') - julianday(sa.completed_at) AS INT) || ' 天未跟进',
        80,
        'pending',
        datetime('now', '+1 day')
      FROM service_appointments sa
      LEFT JOIN sales_leads sl ON sa.customer_id = sl.customer_id
      WHERE sa.appointment_type = 'test_drive'
        AND sa.status = 'completed'
        AND sa.completed_at >= datetime('now', '-14 days')
        AND sa.completed_at < datetime('now', '-7 days')
        AND (sl.status IS NULL OR sl.status IN ('new', 'contacted'))
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt
          WHERE lnt.customer_id = sa.customer_id
            AND lnt.status = 'pending'
        );

  # ─── 任务 4: 价格变动提醒 ───
  - name: price_change_alerts
    type: script
    description: "客户关注车型价格下降时发送提醒"
    script: |
      INSERT OR IGNORE INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, due_date)
      SELECT
        'TASK-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sl.customer_id,
        'price_alert',
        '关注车型 ' || sl.vehicle_interest || ' 价格下调，是联系的好时机',
        75,
        'pending',
        datetime('now', '+2 days')
      FROM sales_leads sl
      JOIN vehicles v ON sl.vehicle_interest LIKE '%' || v.model || '%'
      WHERE v.status = 'available'
        AND v.updated_at >= datetime('now', '-7 days')
        AND sl.status IN ('new', 'contacted', 'qualified')
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt
          WHERE lnt.lead_id = sl.lead_id
            AND lnt.task_type = 'price_alert'
            AND lnt.status = 'pending'
        );

  # ─── 任务 5: AI 生成推荐话术 ───
  - name: generate_ai_recommendations
    type: script
    description: "为新生成的任务 AI 推荐话术"
    script: |
      -- 更新任务，添加 AI 推荐话术
      -- 实际项目中这里会调用 AI API 生成个性化话术
      UPDATE lead_nurturing_tasks
      SET ai_recommendation = 
        CASE task_type
          WHEN 'followup_call' THEN 
            '【开场白】您好，我是 XX 车行的销售顾问 [姓名]。之前您咨询的 [车型] 最近有优惠活动...' || CHAR(10) ||
            '【要点】强调限时优惠、试驾邀请' || CHAR(10) ||
            '【异议处理】价格方面我们可以详细谈谈，今天来店还有额外礼品...'
          
          WHEN 'test_drive_invite' THEN
            '【邀请话术】您好，您关注的 [车型] 现车已到店，本周六/日有试驾会活动...' || CHAR(10) ||
            '【要点】强调现车紧张、试驾会专属优惠' || CHAR(10) ||
            '【时间建议】上午 10 点或下午 2 点客户到店率较高'
          
          WHEN 'price_alert' THEN
            '【通知话术】好消息！您关注的 [车型] 现在有特别优惠，直降 XX 万元...' || CHAR(10) ||
            '【要点】强调限时优惠、库存有限' || CHAR(10) ||
            '【促成】本周内签约可享受额外 XX 优惠'
          
          ELSE '请根据客户情况制定合适的沟通策略'
        END,
        updated_at = datetime('now')
      WHERE ai_recommendation IS NULL
        AND status = 'pending';

  # ─── 任务 6: 发送通知 ───
  - name: notify_sales_reps
    type: script
    description: "通知销售人员有新任务"
    script: |
      -- 统计每个销售人员的新任务数
      SELECT 
        assigned_to,
        COUNT(*) as new_task_count,
        GROUP_CONCAT(task_type) as task_types
      FROM lead_nurturing_tasks
      WHERE status = 'pending'
        AND created_at >= datetime('now', '-1 day')
      GROUP BY assigned_to;
      
      -- 实际项目中这里会发送站内信/邮件/短信通知

# 执行后处理
onComplete:
  - type: log
    message: "线索培育任务生成完成"
  
  - type: script
    description: "记录执行统计"
    script: |
      SELECT 
        '今日生成任务数：' || COUNT(*) as summary
      FROM lead_nurturing_tasks
      WHERE created_at >= datetime('now', '-1 day');

# 监控配置
monitoring:
  alertOnFailure: true
  alertEmail: sales_manager@example.com
  maxExecutionTime: 300
```

---

### 任务 1.3：创建钩子代码

**文件**: `Hooks/LeadNurturingHooks.cs`

```csharp
using NetYamlForge.Hooks;
using System;
using System.Threading.Tasks;

namespace NetYamlForge.Projects.AutoDealerDemo.Hooks
{
    /// <summary>
    /// 线索培育任务钩子
    /// </summary>
    public class LeadNurturingHooks
    {
        /// <summary>
        /// 设置任务时间戳
        /// </summary>
        [Hook("set_nurturing_task_timestamps")]
        public static async Task SetTimestampsAsync(HookContext context)
        {
            context.Data["created_at"] = DateTime.Now;
            context.Data["updated_at"] = DateTime.Now;
            
            // 如果未设置截止日期，默认为 3 天后
            if (!context.Data.ContainsKey("due_date"))
            {
                context.Data["due_date"] = DateTime.Now.AddDays(3);
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// 更新任务时间戳
        /// </summary>
        [Hook("update_nurturing_task_timestamps")]
        public static async Task UpdateTimestampsAsync(HookContext context)
        {
            context.Data["updated_at"] = DateTime.Now;
            
            // 如果状态变为 completed，记录完成时间
            if (context.Data.ContainsKey("status") && 
                context.Data["status"]?.ToString() == "completed")
            {
                context.Data["completed_at"] = DateTime.Now;
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// 计算任务优先级
        /// </summary>
        [Hook("calculate_nurturing_priority")]
        public static async Task CalculatePriorityAsync(HookContext context)
        {
            // 基于任务类型和线索评分计算优先级
            var taskType = context.Data["task_type"]?.ToString();
            var leadScore = context.Data.ContainsKey("lead_score") 
                ? Convert.ToInt32(context.Data["lead_score"]) 
                : 50;

            int basePriority = taskType switch
            {
                "test_drive_invite" => 70,
                "followup_call" => 60,
                "price_alert" => 65,
                "special_offer" => 55,
                "competitor_counter" => 75,
                _ => 50
            };

            // 结合线索评分调整
            var finalPriority = Math.Min(100, basePriority + (leadScore / 10));
            context.Data["priority_score"] = finalPriority;
            
            await Task.CompletedTask;
        }
    }
}
```

---

### 任务 1.4：更新 AI 系统提示词

**文件**: `config/ai_system_prompt.yml`

```yaml
# AI 系统提示词配置 - 增强版
# 将 AI 从"被动查询助手"升级为"主动销售伙伴"

system_prompt: |
  你是自動車ディーラーの AI 業務アシスタントです。

  ## 你的核心职责

  ### 1. 数据查询（基础能力）
  - 快速准确地检索车辆、客户、线索、预约数据
  - 每次查询结果必须包含：
    - 該当件数
    - 主要信息一覧
    - 各レコードに詳細リンク

  ### 2. 主动建议（新增核心能力）⭐
  每次回答查询后，必须主动提供：

  **📋 行动建议**
  - 基于数据发现，建议下一步行动
  - 例如：「该客户已浏览 3 次，建议立即联系」

  **⚠️ 风险提醒**
  - 发现异常数据时主动提醒
  - 例如：「该线索 7 天未联系，流失风险高」

  **💡 话术建议**
  - 生成个性化沟通话术
  - 例如：「可这样联系客户：...」

  **📈 趋势分析**
  - 解读数据背后的含义
  - 例如：「本周 SUV 咨询量上升 30%」

  ## 响应格式

  ```
  [查询结果]
  該当件数：X 件
  - レコード 1 信息 — [詳細リンク]
  - レコード 2 信息 — [詳細リンク]

  [分析洞察]
  - 发现 1...
  - 发现 2...

  [行动建议]
  1. **优先行动**: ...
     - 理由：...
     - 话术：「...」
  
  2. **次要行动**: ...
  ```

  ## 禁止事项

  - ❌ 不修改数据库（只读）
  - ❌ 不修改系统配置
  - ❌ 不生成代码
  - ❌ 不回答与汽车销售无关的问题

  ## 特殊场景处理

  ### 线索查询时
  必须检查：
  - 最后联系时间是否超过 3 天
  - 线索评分是否>70（高意向）
  - 是否有试驾记录但未跟进

  ### 客户查询时
  必须检查：
  - 浏览历史中的高频率车型
  - 距离上次到店是否超过 30 天
  - 是否有未完成的预约

  ### 车辆查询时
  必须检查：
  - 库存时间是否超过 90 天
  - 是否有降价历史
  - 是否有客户多次浏览
```

---

## 📦 第二阶段：客户体验增强（第 3-4 周）

### 目标
提升客户购车体验，实现个性化服务和自动化流程。

### 预期效果
- 试驾预约率：8% → 20%
- 客户满意度：3.8 → 4.5
- 购车指南下载率：+150%

---

### 任务 2.1：创建虚拟试驾助手实体

**文件**: `entities/virtual_test_drive.yml`

```yaml
imports: []
entities:
  virtual_test_drive:
    table: virtual_test_drive
    key: session_id
    displayName: 虚拟试驾会话
    description: AI 虚拟试驾助手的对话会话记录
    softDelete: false
    isPublic: true

    columns:
      session_id:
        type: string
        length: 50
        required: true
        label: 会话 ID
        searchable: true
        sortable: true
      
      customer_id:
        type: string
        length: 50
        required: false
        label: 客户 ID
        searchable: true
        sortable: true
      
      customer_name:
        type: string
        length: 100
        required: false
        label: 客户姓名
      
      customer_phone:
        type: string
        length: 20
        required: false
        label: 联系电话
      
      usage_scenario:
        type: string
        length: 50
        required: false
        label: 用车场景
        options:
          - commute         # 日常通勤
          - family          # 家庭用车
          - business        # 商务用途
          - hobby           # 兴趣爱好（越野、露营等）
          - first_car       # 首台车
      
      family_size:
        type: int
        required: false
        label: 家庭人数
        default: 1
      
      budget_min:
        type: decimal
        precision: 10
        scale: 0
        required: false
        label: 预算下限
      
      budget_max:
        type: decimal
        precision: 10
        scale: 0
        required: false
        label: 预算上限
      
      recommended_vehicles:
        type: text
        required: false
        label: 推荐车型
        description: AI 推荐的车型 ID 列表（JSON 格式）
      
      test_drive_invite_sent:
        type: boolean
        required: true
        label: 已发送试驾邀请
        default: false
      
      test_drive_appointment_id:
        type: string
        length: 50
        required: false
        label: 试驾预约 ID
      
      conversation_log:
        type: text
        required: false
        label: 对话记录
        description: 与客户的完整对话记录（JSON）
      
      status:
        type: string
        length: 20
        required: true
        label: 状态
        options:
          - in_progress   # 进行中
          - completed     # 已完成
          - abandoned     # 已放弃
        default: in_progress
      
      created_at:
        type: datetime
        required: true
        label: 创建时间
        sortable: true
      
      completed_at:
        type: datetime
        required: false
        label: 完成时间

    hooks:
      beforeCreate:
        - set_virtual_test_drive_timestamps

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true
```

---

### 任务 2.2：创建购车指南 PDF 生成服务

**文件**: `Services/Pdf/BuyingGuideService.cs`

```csharp
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace NetYamlForge.Projects.AutoDealerDemo.Services.Pdf
{
    /// <summary>
    /// 购车指南 PDF 生成服务
    /// </summary>
    public class BuyingGuideService
    {
        private readonly IDatabaseService _db;
        private readonly IAIService _ai;

        public BuyingGuideService(IDatabaseService db, IAIService ai)
        {
            _db = db;
            _ai = ai;
        }

        /// <summary>
        /// 生成个性化购车指南
        /// </summary>
        public async Task<PdfDocument> GenerateGuideAsync(
            string customerId,
            List<string> vehicleIds)
        {
            var customer = await _db.GetAsync<Customer>("customers", customerId);
            var vehicles = await _db.GetAsync<List<Vehicle>>("vehicles", 
                $"WHERE vehicle_id IN ({string.Join(",", vehicleIds)})");

            var pdf = new PdfDocument();
            pdf.Info.Title = $"购车指南 - {customer.Name} 先生/女士";
            pdf.Info.Author = "自動車ディーラー AI システム";
            pdf.Info.Subject = $"生成日：{DateTime.Now:yyyy/MM/dd}";

            // 第 1 页：封面
            AddCoverPage(pdf, customer);

            // 第 2 页：客户需求分析
            AddNeedsAnalysisPage(pdf, customer, vehicles);

            // 第 3-5 页：推荐车型详情
            foreach (var vehicle in vehicles)
            {
                AddVehicleDetailPage(pdf, vehicle);
            }

            // 第 6 页：价格对比
            AddPriceComparisonPage(pdf, vehicles);

            // 第 7 页：贷款方案
            await AddLoanOptionsPageAsync(pdf, customer, vehicles);

            // 第 8 页：保养成本
            AddMaintenanceCostPage(pdf, vehicles);

            // 第 9 页：下一步行动
            AddNextStepsPage(pdf, customer, vehicles);

            return pdf;
        }

        private void AddCoverPage(PdfDocument pdf, Customer customer)
        {
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("MS Mincho", 24, XFontStyle.Bold);

            gfx.DrawString(
                "购车指南",
                font,
                XBrushes.Black,
                new XRect(0, 100, page.Width, 100),
                XStringFormats.Center);

            font = new XFont("MS Mincho", 16);
            gfx.DrawString(
                $"{customer.Name} 先生/女士 専用",
                font,
                XBrushes.Gray,
                new XRect(0, 150, page.Width, 50),
                XStringFormats.Center);

            gfx.DrawString(
                $"生成日：{DateTime.Now:yyyy 年 MM 月 dd 日}",
                font,
                XBrushes.Gray,
                new XRect(0, 200, page.Width, 50),
                XStringFormats.Center);
        }

        private void AddNeedsAnalysisPage(PdfDocument pdf, Customer customer, List<Vehicle> vehicles)
        {
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            
            gfx.DrawString("您的需求分析", new XFont("MS Mincho", 18, XFontStyle.Bold), 
                XBrushes.Black, 50, 50);

            var y = 100;
            gfx.DrawString($"• 顧客ランク：{customer.TierLevel}", new XFont("MS Mincho", 12), 
                XBrushes.Black, 50, y);
            y += 25;
            gfx.DrawString($"• 購入回数：{customer.PurchaseCount}回", new XFont("MS Mincho", 12), 
                XBrushes.Black, 50, y);
            y += 25;
            gfx.DrawString($"• 累計購入金額：¥{customer.TotalPurchaseAmount:N0}", new XFont("MS Mincho", 12), 
                XBrushes.Black, 50, y);
        }

        private void AddVehicleDetailPage(PdfDocument pdf, Vehicle vehicle)
        {
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString($"{vehicle.Maker} {vehicle.Model}", 
                new XFont("MS Mincho", 18, XFontStyle.Bold), 
                XBrushes.Black, 50, 50);

            var y = 100;
            gfx.DrawString($"年式：{vehicle.Year}", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
            y += 25;
            gfx.DrawString($"価格：¥{vehicle.Price:N0}", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
            y += 25;
            gfx.DrawString($"走行距離：{vehicle.Mileage:N0}km", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
            y += 25;
            gfx.DrawString($"燃料：{vehicle.FuelType}", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
        }

        private void AddPriceComparisonPage(PdfDocument pdf, List<Vehicle> vehicles)
        {
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString("価格比較", new XFont("MS Mincho", 18, XFontStyle.Bold), 
                XBrushes.Black, 50, 50);

            var y = 100;
            foreach (var v in vehicles)
            {
                gfx.DrawString($"{v.Maker} {v.Model}: ¥{v.Price:N0}", 
                    new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
                y += 25;
            }
        }

        private async Task AddLoanOptionsPageAsync(PdfDocument pdf, Customer customer, List<Vehicle> vehicles)
        {
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString("ローンシミュレーション", new XFont("MS Mincho", 18, XFontStyle.Bold), 
                XBrushes.Black, 50, 50);

            // 实际项目中这里会调用贷款计算 API
            var y = 100;
            gfx.DrawString("※ 詳細なローン計画は担当者にご相談ください", 
                new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
        }

        private void AddMaintenanceCostPage(PdfDocument pdf, List<Vehicle> vehicles)
        {
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString("維持費目安", new XFont("MS Mincho", 18, XFontStyle.Bold), 
                XBrushes.Black, 50, 50);

            var y = 100;
            foreach (var v in vehicles)
            {
                var maintenanceCost = v.FuelType switch
                {
                    "電気" => "¥8,000/月",
                    "ハイブリッド" => "¥12,000/月",
                    "ガソリン" => "¥15,000/月",
                    "ディーゼル" => "¥13,000/月",
                    _ => "要相談"
                };

                gfx.DrawString($"{v.Maker} {v.Model}: {maintenanceCost}", 
                    new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
                y += 25;
            }
        }

        private void AddNextStepsPage(PdfDocument pdf, Customer customer, List<Vehicle> vehicles)
        {
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString("次のステップ", new XFont("MS Mincho", 18, XFontStyle.Bold), 
                XBrushes.Black, 50, 50);

            var y = 100;
            gfx.DrawString("1. 試乗予約", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
            y += 30;
            gfx.DrawString("2. 見積もり作成", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
            y += 30;
            gfx.DrawString("3. ご契約", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
            y += 30;
            gfx.DrawString("4. 納車", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);

            y += 50;
            gfx.DrawString("お問い合わせ", new XFont("MS Mincho", 14, XFontStyle.Bold), 
                XBrushes.Black, 50, y);
            y += 25;
            gfx.DrawString("電話番号：03-XXXX-XXXX", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
            y += 20;
            gfx.DrawString("営業時間：9:00-18:00（定休日：水曜）", new XFont("MS Mincho", 12), XBrushes.Black, 50, y);
        }
    }
}
```

---

## 📦 第三阶段：数据驱动决策（第 5-7 周）

### 目标
建立数据驱动的销售预测和预警系统。

### 预期效果
- 销售预测准确率：60% → 85%
- 流失客户挽回率：5% → 15%
- 目标达成率：+30%

---

### 任务 3.1：创建销售预测视图

**文件**: `entities/v_sales_forecast.yml`

```yaml
imports: []
entities:
  v_sales_forecast:
    table: v_sales_forecast
    key: sales_rep_id
    displayName: 销售预测视图
    description: 销售人员业绩预测和管道分析
    softDelete: false
    isPublic: false
    isView: true  # 标记为视图，不创建表

    columns:
      sales_rep_id:
        type: string
        length: 50
        required: true
        label: 销售人员 ID
        searchable: true
        sortable: true
      
      sales_rep_name:
        type: string
        length: 100
        required: false
        label: 销售人员姓名
      
      month:
        type: string
        length: 7
        required: true
        label: 月份
        format: "yyyy-MM"
      
      target_count:
        type: int
        required: true
        label: 目标台数
        default: 10
      
      actual_count:
        type: int
        required: false
        label: 实际台数
      
      new_leads:
        type: int
        required: false
        label: 新线索数
      
      qualified_leads:
        type: int
        required: false
        label: 合格线索数
      
      proposal_leads:
        type: int
        required: false
        label: 提案中线索数
      
      avg_lead_score:
        type: decimal
        precision: 5
        scale: 2
        required: false
        label: 平均线索评分
      
      pipeline_value:
        type: decimal
        precision: 15
        scale: 0
        required: false
        label: 管道金额
      
      predicted_conversion:
        type: decimal
        precision: 5
        scale: 2
        required: false
        label: 预测成约率
      
      predicted_sales:
        type: int
        required: false
        label: 预测销售台数
      
      target_achievement:
        type: decimal
        precision: 5
        scale: 2
        required: false
        label: 目标达成率

    # 视图定义 SQL
    viewDefinition: |
      CREATE VIEW v_sales_forecast AS
      SELECT 
          sl.assigned_to_user_id as sales_rep_id,
          u.name as sales_rep_name,
          strftime('%Y-%m', sl.created_at) as month,
          10 as target_count,
          COUNT(CASE WHEN sl.status = 'won' THEN 1 END) as actual_count,
          COUNT(CASE WHEN sl.status = 'new' THEN 1 END) as new_leads,
          COUNT(CASE WHEN sl.status = 'qualified' THEN 1 END) as qualified_leads,
          COUNT(CASE WHEN sl.status = 'proposal' THEN 1 END) as proposal_leads,
          AVG(sl.lead_score) as avg_lead_score,
          SUM(v.price) as pipeline_value,
          -- 简化版预测成约率
          AVG(sl.lead_score) / 100.0 * 0.6 as predicted_conversion,
          -- 预测销售台数
          CAST(
            (COUNT(*) * AVG(sl.lead_score) / 100.0 * 0.6) AS INT
          ) as predicted_sales,
          -- 目标达成率
          (COUNT(CASE WHEN sl.status = 'won' THEN 1 END) * 100.0 / 10) as target_achievement
      FROM sales_leads sl
      LEFT JOIN users u ON sl.assigned_to_user_id = u.user_id
      LEFT JOIN vehicles v ON sl.vehicle_interest LIKE '%' || v.model || '%'
      GROUP BY sl.assigned_to_user_id, strftime('%Y-%m', sl.created_at);

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true
```

---

### 任务 3.2：创建流失预警批处理作业

**文件**: `jobs/churn_prediction.yml`

```yaml
name: churn_prediction
displayName: "客户流失预警"
description: "每日 8:00 检测高流失风险客户，自动生成挽回任务"
schedule: "0 8 * * *"  # 每天 8:00 执行
timeout: 300
enabled: true

tasks:
  # ─── 风险 1: 高评分线索长时间未联系 ───
  - name: high_score_stale_lead
    type: script
    script: |
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, due_date, ai_recommendation)
      SELECT
        'CHURN-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sl.customer_id,
        'followup_call',
        '【流失风险】线索评分 ' || sl.lead_score || ' 但 ' || 
          CAST(julianday('now') - julianday(sl.last_contact_at) AS INT) || ' 天未联系',
        sl.lead_score,
        'pending',
        datetime('now', '+1 day'),
        '【緊急】高意向顧客の流失リスクがあります。' || CHAR(10) ||
        '【話術】「こんにちは、前回お話しした際はとても盛り上がりましたね。' || CHAR(10) ||
        '  ちょうど良い条件の車両が入ってきましたので、ご連絡させていただきました。」' || CHAR(10) ||
        '【提案】特別割引または優先試乗を提案'
      FROM sales_leads sl
      WHERE sl.status IN ('new', 'contacted', 'qualified')
        AND sl.lead_score >= 70
        AND (sl.last_contact_at IS NULL 
             OR julianday('now') - julianday(sl.last_contact_at) >= 14)
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt
          WHERE lnt.lead_id = sl.lead_id
            AND lnt.status = 'pending'
        );

  # ─── 风险 2: 试驾后未跟进 ───
  - name: post_drive_no_followup
    type: script
    script: |
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, due_date, ai_recommendation)
      SELECT
        'CHURN-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        COALESCE(sl.lead_id, 'LEAD-' || substr(hex(randomblob(4)), 1, 8)),
        sa.customer_id,
        'followup_call',
        '【流失风险】試乗完了から ' || 
          CAST(julianday('now') - julianday(sa.completed_at) AS INT) || ' 日経過',
        75,
        'pending',
        datetime('now', '+1 day'),
        '【試乗フォロー】試乗時の感想を伺い、成約を促す' || CHAR(10) ||
        '【話術】「先日は試乗ありがとうございました。' || CHAR(10) ||
        '  車両の調子はいかがでしたか？' || CHAR(10) ||
        '  今なら特別金利 1.9% のローンがご利用いただけます。」'
      FROM service_appointments sa
      LEFT JOIN sales_leads sl ON sa.customer_id = sl.customer_id
      WHERE sa.appointment_type = 'test_drive'
        AND sa.status = 'completed'
        AND julianday('now') - julianday(sa.completed_at) BETWEEN 7 AND 14
        AND (sl.status IS NULL OR sl.status IN ('new', 'contacted'))
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt
          WHERE lnt.customer_id = sa.customer_id
            AND lnt.status = 'pending'
        );

  # ─── 风险 3: 价格咨询后无下文 ───
  - name: price_inquiry_no_response
    type: script
    script: |
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, due_date, ai_recommendation)
      SELECT
        'CHURN-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sl.customer_id,
        'special_offer',
        '【価格相談】価格お問い合わせから ' || 
          CAST(julianday('now') - julianday(sl.created_at) AS INT) || ' 日経過',
        65,
        'pending',
        datetime('now', '+2 days'),
        '【特別オファー】期間限定割引を提示して成約を促す' || CHAR(10) ||
        '【話術】「お問い合わせいただいた車両ですが、' || CHAR(10) ||
        '  今月限りで 10 万円の特別割引がございます。' || CHAR(10) ||
        '  ご検討はいかがでしょうか。」'
      FROM sales_leads sl
      WHERE sl.vehicle_interest IS NOT NULL
        AND sl.status IN ('new', 'contacted')
        AND julianday('now') - julianday(sl.created_at) BETWEEN 10 AND 20
        AND NOT EXISTS (
          SELECT 1 FROM lead_activities la
          WHERE la.lead_id = sl.lead_id
            AND la.activity_type = 'proposal_sent'
        )
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt
          WHERE lnt.lead_id = sl.lead_id
            AND lnt.status = 'pending'
        );

  # ─── 风险 4: 老客户 90 天未到店 ───
  - name: loyal_customer_no_visit
    type: script
    script: |
      INSERT INTO lead_nurturing_tasks 
        (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score, status, due_date, ai_recommendation)
      SELECT
        'CHURN-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        'LEAD-' || substr(hex(randomblob(4)), 1, 8),
        c.customer_id,
        'special_offer',
        '【リピーター】最終来店から ' || 
          CAST(julianday('now') - julianday(c.last_visit_date) AS INT) || ' 日経過',
        CASE c.tier_level
          WHEN 'vip' THEN 90
          WHEN 'gold' THEN 80
          WHEN 'silver' THEN 70
          ELSE 60
        END,
        'pending',
        datetime('now', '+3 days'),
        '【ロイヤルカスタマー】特別招待状を送付' || CHAR(10) ||
        '【話術】「いつもありがとうございます。' || CHAR(10) ||
        '  貴社限定のプレビューイベントを開催いたします。' || CHAR(10) ||
        '  ぜひご参加ください。」' || CHAR(10) ||
        '【特典】試乗プレゼントまたは洗車券'
      FROM customers c
      WHERE c.tier_level IN ('vip', 'gold', 'silver')
        AND c.last_visit_date IS NOT NULL
        AND julianday('now') - julianday(c.last_visit_date) >= 90
        AND NOT EXISTS (
          SELECT 1 FROM sales_leads sl
          WHERE sl.customer_id = c.customer_id
            AND sl.status IN ('new', 'contacted', 'qualified')
        )
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt
          WHERE lnt.customer_id = c.customer_id
            AND lnt.status = 'pending'
        );

onComplete:
  - type: log
    message: "流失预警检测完成"

monitoring:
  alertOnFailure: true
  alertEmail: sales_manager@example.com
```

---

## 📦 第四阶段：AI 能力扩展（第 8-10 周）

### 目标
引入多模态 AI 能力，实现图像识别和语音交互。

### 预期效果
- 车辆录入效率：+200%
- 客户沟通便利性：+80%
- 知识库自动化：+60%

---

### 任务 4.1：多模态 AI 服务

**文件**: `Services/AI/MultimodalService.cs`

```csharp
using System;
using System.IO;
using System.Threading.Tasks;

namespace NetYamlForge.Projects.AutoDealerDemo.Services.AI
{
    /// <summary>
    /// 多模态 AI 服务
    /// </summary>
    public class MultimodalService
    {
        private readonly IOcrService _ocr;
        private readonly IVisionService _vision;
        private readonly ISpeechService _speech;

        public MultimodalService(
            IOcrService ocr,
            IVisionService vision,
            ISpeechService speech)
        {
            _ocr = ocr;
            _vision = vision;
            _speech = speech;
        }

        /// <summary>
        /// 从车辆照片提取 VIN 码
        /// </summary>
        public async Task<string> ExtractVinFromImageAsync(Stream imageStream)
        {
            var ocrResult = await _ocr.RecognizeAsync(imageStream);
            
            // VIN 码正则表达式（17 位字母数字）
            var vinPattern = @"[A-HJ-NPR-Z0-9]{17}";
            var match = System.Text.RegularExpressions.Regex.Match(
                ocrResult.Text, 
                vinPattern);
            
            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// 评估车辆状况（基于照片）
        /// </summary>
        public async Task<VehicleConditionReport> AssessVehicleConditionAsync(
            List<Stream> photos)
        {
            var report = new VehicleConditionReport();

            foreach (var photo in photos)
            {
                var analysis = await _vision.AnalyzeAsync(photo);
                
                // 检测划痕、凹陷、污渍等
                if (analysis.DetectedObjects.Contains("scratch"))
                {
                    report.ExteriorIssues.Add(new Issue {
                        Type = "scratch",
                        Severity = analysis.Confidence
                    });
                }
            }

            // 计算综合评分
            report.OverallScore = CalculateConditionScore(report);

            return report;
        }

        /// <summary>
        /// 语音消息转文字
        /// </summary>
        public async Task<string> TranscribeVoiceMessageAsync(Stream audioStream)
        {
            return await _speech.TranscribeAsync(audioStream);
        }

        /// <summary>
        /// 生成语音回复
        /// </summary>
        public async Task<Stream> GenerateVoiceResponseAsync(string text)
        {
            return await _speech.SynthesizeAsync(text);
        }

        private int CalculateConditionScore(VehicleConditionReport report)
        {
            var baseScore = 100;
            
            foreach (var issue in report.ExteriorIssues)
            {
                baseScore -= issue.Severity switch
                {
                    "minor" => 5,
                    "moderate" => 15,
                    "severe" => 30,
                    _ => 10
                };
            }

            return Math.Max(0, baseScore);
        }
    }

    public class VehicleConditionReport
    {
        public int OverallScore { get; set; }
        public List<Issue> ExteriorIssues { get; set; } = new();
        public List<Issue> InteriorIssues { get; set; } = new();
    }

    public class Issue
    {
        public string Type { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; }
    }
}
```

---

## 📊 预期效果总结

### 量化指标

| 指标 | 基线 | 目标 | 提升 | 实现阶段 |
|------|------|------|------|----------|
| 线索转化率 | 15% | 25% | +67% | 阶段 1 |
| 平均销售周期 | 21 天 | 14 天 | -33% | 阶段 1+3 |
| 试驾预约率 | 8% | 20% | +150% | 阶段 2 |
| 客户满意度 | 3.8 | 4.5 | +18% | 阶段 2 |
| 销售人均效率 | 8 单/月 | 12 单/月 | +50% | 阶段 1 |
| 流失客户挽回 | 5% | 15% | +200% | 阶段 3 |
| 销售预测准确率 | 60% | 85% | +42% | 阶段 3 |
| 车辆录入效率 | 5 分/台 | 2 分/台 | +150% | 阶段 4 |

### 质化收益

- ✅ AI 主动发现销售机会，减少遗漏
- ✅ 标准化销售流程，提升专业度
- ✅ 数据驱动决策，减少主观判断
- ✅ 24 小时不间断客户服务
- ✅ 销售新人快速上手
- ✅ 客户体验个性化、差异化

---

## 🚀 快速开始

### 第 1 周立即可以做的事情

```bash
# 1. 创建线索培育任务实体
cp entities/lead_nurturing_tasks.yml.template entities/lead_nurturing_tasks.yml

# 2. 创建批处理作业
cp jobs/lead_nurturing_generator.yml.template jobs/lead_nurturing_generator.yml

# 3. 创建钩子代码
# 在 Hooks/ 目录下创建 LeadNurturingHooks.cs

# 4. 更新 AI 提示词
# 编辑 config/ai_system_prompt.yml

# 5. 重启应用
dotnet run --project NetYamlForge
```

### 验证效果

```bash
# 查看生成的任务
dotnet run -- --query "SELECT * FROM lead_nurturing_tasks WHERE status='pending'"

# 查看线索转化率变化
dotnet run -- --query "SELECT status, COUNT(*) FROM sales_leads GROUP BY status"
```

---

## 📝 相关文档

- [AI 系统提示词配置](../../docs/AI-SYSTEM-PROMPT-CONFIG.md)
- [批处理作业实现](../../docs/guides/batch-jobs.md)
- [钩子系统说明](../../docs/COMMON_HOOKS.md)
- [PDF 生成服务](../../docs/guides/pdf-generation.md)

---

*本文档将持续更新，反映最新实施进度和最佳实践。*
