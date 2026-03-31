# 汽车销售 AI 系统智能化提升计划

## 📋 执行摘要

本文档规划了将当前 AI 系统从"被动查询助手"升级为"主动销售伙伴"的完整路线图。

---

## 🎯 核心目标

| 目标 | 当前状态 | 目标状态 | 预期提升 |
|------|----------|----------|----------|
| 线索转化率 | ~15% | ~25% | +67% |
| 客户满意度 | 3.8/5 | 4.5/5 | +18% |
| 销售周期 | 21 天 | 14 天 | -33% |
| 试驾预约率 | 8% | 20% | +150% |

---

## 📦 第一阶段：智能销售助手（1-2 周）

### 1.1 线索培育自动化

#### 新增实体定义

```yaml
# entities/lead_nurturing_tasks.yml
imports: []
entities:
  lead_nurturing_tasks:
    table: lead_nurturing_tasks
    key: task_id
    displayName: 线索培育任务
    softDelete: false
    isPublic: true

    columns:
      task_id:
        type: string
        length: 50
        required: true
        label: 任务 ID
      lead_id:
        type: string
        length: 50
        required: true
        label: 线索 ID
        searchable: true
      customer_id:
        type: string
        length: 50
        required: true
        label: 客户 ID
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
      trigger_reason:
        type: string
        length: 200
        required: false
        label: 触发原因
      priority_score:
        type: int
        required: true
        label: 优先级评分
        default: 50
        sortable: true
      status:
        type: string
        length: 20
        required: true
        label: 状态
        options:
          - pending
          - in_progress
          - completed
          - cancelled
      assigned_to:
        type: string
        length: 50
        required: false
        label: 负责人
      ai_recommendation:
        type: text
        required: false
        label: AI 建议话术
      completed_at:
        type: datetime
        required: false
        label: 完成时间
      created_at:
        type: datetime
        required: true
        label: 创建时间
      updated_at:
        type: datetime
        required: true
        label: 更新时间

    hooks:
      beforeCreate:
        - set_task_timestamps
        - calculate_task_priority
      beforeUpdate:
        - update_task_timestamps

    paging:
      pageSize: 20
      mode: numbered
      enableCount: true
```

#### 批处理作业：自动生成培育任务

```yaml
# jobs/lead_nurturing_generator.yml
name: lead_nurturing_generator
displayName: "线索培育任务生成器"
description: "每日扫描客户行为，自动生成跟进任务"
schedule: "0 9 * * *"  # 每天 9:00 执行
timeout: 300

tasks:
  - name: analyze_customer_behavior
    type: script
    script: |
      # 1. 浏览特定车型 3 次以上的客户
      SELECT customer_id, vehicle_id, COUNT(*) as view_count
      FROM lead_activities
      WHERE activity_type = 'vehicle_view'
        AND created_at >= datetime('now', '-7 days')
      GROUP BY customer_id, vehicle_id
      HAVING COUNT(*) >= 3;

      # 2. 价格咨询后未跟进的线索
      SELECT sl.lead_id, sl.customer_id, sl.vehicle_interest
      FROM sales_leads sl
      LEFT JOIN lead_activities la ON sl.lead_id = la.lead_id
      WHERE sl.status IN ('new', 'contacted')
        AND sl.last_contact_at < datetime('now', '-3 days');

      # 3. 试驾后 7 天未联系的客戶
      SELECT customer_id, appointment_id
      FROM service_appointments
      WHERE appointment_type = 'test_drive'
        AND status = 'completed'
        AND completed_at < datetime('now', '-7 days')
        AND customer_id NOT IN (
          SELECT customer_id FROM sales_leads 
          WHERE status IN ('won', 'lost')
        );
```

### 1.2 智能车辆推荐

#### 推荐算法逻辑

```csharp
// Services/AI/VehicleRecommendationService.cs
public class VehicleRecommendationService
{
    /// <summary>
    /// 基于客户画像推荐车辆
    /// </summary>
    public async Task<RecommendationResult> RecommendForCustomerAsync(
        string customerId, 
        int maxResults = 5)
    {
        // 1. 获取客户信息
        var customer = await _db.GetAsync<Customer>("customers", customerId);
        
        // 2. 分析客户行为
        var behavior = await AnalyzeBehaviorAsync(customerId);
        
        // 3. 计算推荐分数
        var recommendations = await _db.QueryAsync<Vehicle>(@"
            SELECT v.*, 
                   (@budget_match * 0.3 + 
                    @type_match * 0.25 + 
                    @brand_affinity * 0.2 +
                    @feature_match * 0.15 +
                    @trending * 0.1) as match_score
            FROM vehicles v
            WHERE v.status = 'available'
            ORDER BY match_score DESC
            LIMIT @top", 
            new { 
                budget_match = CalculateBudgetMatch(customer.Budget, v.Price),
                type_match = CalculateTypeMatch(customer.PreferredType, v.VehicleType),
                brand_affinity = GetBrandAffinity(customer.Id, v.Maker),
                feature_match = CalculateFeatureMatch(customer.Needs, v.Features),
                trending = GetTrendingScore(v.Model),
                top = maxResults
            });
        
        // 4. AI 生成推荐理由
        foreach (var rec in recommendations)
        {
            rec.Reasoning = await GenerateReasoningAsync(customer, rec);
        }
        
        return new RecommendationResult {
            CustomerId = customerId,
            Matches = recommendations.ToList(),
            GeneratedAt = DateTime.Now
        };
    }
}
```

### 1.3 销售脚本生成器

#### 动态话术生成

```yaml
# entities/ai_sales_scripts.yml
ai_sales_scripts:
  columns:
    script_id: { type: string, label: 脚本 ID }
    scenario: 
      type: string
      options: [initial_contact, price_negotiation, competitor_comparison, closing]
    customer_persona:
      type: string
      options: [first_time_buyer, trade_in, competitor_owner, luxury_buyer]
    vehicle_category: { type: string, options: [sedan, suv, ev, luxury] }
    script_content: { type: text, label: 话术内容 }
    key_points: { type: text, label: 关键要点 }
    objection_handling: { type: text, label: 异议处理 }
    success_count: { type: int, default: 0 }
    failure_count: { type: int, default: 0 }
    last_used_at: { type: datetime }
```

---

## 📦 第二阶段：客户体验增强（1-2 周）

### 2.1 虚拟试驾助手流程

```
┌─────────────────────────────────────────────────────────────┐
│                    虚拟试驾助手流程                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  1. AI 询问需求                                              │
│     ├─ 用车场景（通勤/家庭/越野）                            │
│     ├─ 家庭成员数                                           │
│     └─ 预算范围                                             │
│                                                             │
│  2. AI 推荐车型（2-3 款）                                     │
│     ├─ 匹配理由说明                                         │
│     ├─ 关键参数对比                                         │
│     └─ 实车图片展示                                         │
│                                                             │
│  3. 试驾邀请                                                │
│     ├─ 智能推荐时间段（基于历史预约数据）                    │
│     ├─ 可选上门试驾                                         │
│     └─ 一键确认                                             │
│                                                             │
│  4. 自动确认                                                │
│     ├─ 短信/邮件确认                                        │
│     ├─ 日历邀请                                             │
│     └─ 销售顾问分配                                         │
│                                                             │
│  5. 提醒跟进                                                │
│     ├─ 前 1 天提醒                                            │
│     ├─ 前 2 小时提醒                                          │
│     └─ 改约/取消处理                                        │
│                                                             │
│  6. 试驾后反馈                                              │
│     ├─ 满意度调查                                           │
│     ├─ 购车意向确认                                         │
│     └─ 自动创建销售线索                                     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 购车指南 PDF 生成

```csharp
// Services/Pdf/BuyingGuideService.cs
public class BuyingGuideService
{
    public async Task<PdfDocument> GenerateGuideAsync(
        string customerId, 
        List<string> vehicleIds)
    {
        var customer = await GetCustomerAsync(customerId);
        var vehicles = await GetVehiclesAsync(vehicleIds);
        
        var pdf = new PdfDocument();
        
        // 第 1 页：封面
        pdf.AddPage();
        pdf.AddText($"购车指南 - {customer.Name} 先生/女士");
        pdf.AddText($"生成日期：{DateTime.Now:yyyy/MM/dd}");
        
        // 第 2 页：客户需求分析
        pdf.AddPage();
        pdf.AddText("您的需求分析");
        pdf.AddText(await AnalyzeNeedsAsync(customer));
        
        // 第 3-5 页：推荐车型详情
        foreach (var v in vehicles)
        {
            pdf.AddPage();
            pdf.AddText($"推荐车型：{v.Maker} {v.Model}");
            pdf.AddImage(v.ImageUrl);
            pdf.AddText(await GenerateVehicleDescriptionAsync(v));
        }
        
        // 第 6 页：价格对比
        pdf.AddPage();
        pdf.AddTable(GeneratePriceComparison(vehicles));
        
        // 第 7 页：贷款方案
        pdf.AddPage();
        pdf.AddText(await GenerateLoanOptionsAsync(customer, vehicles));
        
        // 第 8 页：保养成本估算
        pdf.AddPage();
        pdf.AddText(await GenerateMaintenanceCostAsync(vehicles));
        
        return pdf;
    }
}
```

---

## 📦 第三阶段：数据驱动决策（2-3 周）

### 3.1 销售预测仪表板

```yaml
# pages/SalesForecast.yaml
page:
  id: sales_forecast
  displayName: 销售预测
  roles: [sales_manager, executive]

widgets:
  - type: chart
    title: 月度销售预测
    source: v_sales_forecast
    chartType: line
    metrics:
      - predicted_sales
      - actual_sales
      - pipeline_value

  - type: kpi
    title: 本月目标达成率
    source: v_sales_target
    format: percentage

  - type: table
    title: 销售人员排名
    source: v_sales_rep_ranking
    columns:
      - sales_rep_name
      - closed_deals
      - pipeline_count
      - predicted_conversion
```

### 3.2 客户流失预警作业

```yaml
# jobs/churn_prediction.yml
name: churn_prediction
displayName: "客户流失预警"
schedule: "0 8 * * *"  # 每天 8:00

tasks:
  - name: detect_churn_risk
    type: script
    script: |
      -- 高风险客户定义：
      -- 1. 线索评分>70 但 14 天未联系
      -- 2. 试驾完成后 7 天无跟进
      -- 3. 价格咨询后无下文
      
      INSERT INTO lead_nurturing_tasks (task_id, lead_id, customer_id, task_type, trigger_reason, priority_score)
      SELECT 
        'CHURN-' || strftime('%Y%m%d%H%M%S', 'now') || '-' || substr(hex(randomblob(4)), 1, 6),
        sl.lead_id,
        sl.customer_id,
        'followup_call',
        '流失风险：线索评分 ' || sl.lead_score || ' 但 ' || 
          CAST(julianday('now') - julianday(sl.last_contact_at) AS INT) || ' 天未联系',
        sl.lead_score
      FROM sales_leads sl
      WHERE sl.status IN ('new', 'contacted', 'qualified')
        AND sl.lead_score >= 70
        AND julianday('now') - julianday(sl.last_contact_at) >= 14
        AND NOT EXISTS (
          SELECT 1 FROM lead_nurturing_tasks lnt 
          WHERE lnt.lead_id = sl.lead_id 
            AND lnt.status = 'pending'
        );
```

---

## 📦 第四阶段：AI 能力扩展（2-3 周）

### 4.1 多模态 AI 集成

```csharp
// Services/AI/MultimodalService.cs
public class MultimodalService
{
    // 车辆照片识别 VIN
    public async Task<string> ExtractVinFromImageAsync(Stream imageStream)
    {
        var ocrResult = await _ocrService.RecognizeAsync(imageStream);
        var vin = Regex.Match(ocrResult.Text, @"[A-HJ-NPR-Z0-9]{17}").Value;
        return vin;
    }
    
    // 车况评估（基于照片）
    public async Task<VehicleCondition> AssessConditionAsync(List<Stream> photos)
    {
        var analysis = await _visionModel.AnalyzeAsync(photos);
        return new VehicleCondition {
            ExteriorScore = analysis.Exterior.Condition,
            InteriorScore = analysis.Interior.Condition,
            DetectedIssues = analysis.Issues
        };
    }
    
    // 语音输入支持
    public async Task<string> TranscribeVoiceMessageAsync(Stream audio)
    {
        return await _speechService.TranscribeAsync(audio);
    }
}
```

### 4.2 知识库自动学习

```csharp
// Services/AI/KnowledgeExtractor.cs
public class KnowledgeExtractor
{
    public async Task<List<KnowledgeSnippet>> ExtractFromSuccessfulDealsAsync()
    {
        // 获取成功成交的对话
        var wonLeads = await _db.QueryAsync(@"
            SELECT sl.*, c.name as customer_name
            FROM sales_leads sl
            JOIN customers c ON sl.customer_id = c.customer_id
            WHERE sl.status = 'won'
            ORDER BY sl.updated_at DESC
            LIMIT 100
        ");
        
        var snippets = new List<KnowledgeSnippet>();
        
        foreach (var lead in wonLeads)
        {
            // 获取相关对话
            var conversations = await GetConversationsForLead(lead.LeadId);
            
            // AI 分析成功因素
            var analysis = await _aiService.AnalyzeSuccessFactorsAsync(conversations);
            
            snippets.Add(new KnowledgeSnippet {
                Scenario = analysis.Scenario,
                KeyPhrase = analysis.KeyPhrase,
                SuccessPattern = analysis.Pattern,
                SourceLeadId = lead.LeadId
            });
        }
        
        return snippets;
    }
}
```

---

## 📊 预期效果

### 量化指标

| 指标 | 基线 | 目标 | 提升 |
|------|------|------|------|
| 线索转化率 | 15% | 25% | +67% |
| 平均销售周期 | 21 天 | 14 天 | -33% |
| 试驾预约率 | 8% | 20% | +150% |
| 客户满意度 | 3.8 | 4.5 | +18% |
| 销售人员效率 | 8 单/月 | 12 单/月 | +50% |
| 流失客户挽回 | 5% | 15% | +200% |

### 质化收益

- ✅ AI 主动发现销售机会，减少遗漏
- ✅ 标准化销售流程，提升专业度
- ✅ 数据驱动决策，减少主观判断
- ✅ 24 小时不间断客户服务
- ✅ 销售新人快速上手

---

## 🚀 实施步骤

### 第 1 周：基础建设
- [ ] 创建新增实体 YAML
- [ ] 实现线索培育任务生成器
- [ ] 更新 AI 系统提示词

### 第 2 周：核心功能
- [ ] 实现车辆推荐服务
- [ ] 开发销售脚本生成器
- [ ] 集成试驾预约流程

### 第 3 周：数据分析
- [ ] 创建销售预测视图
- [ ] 实现流失预警作业
- [ ] 开发预测仪表板

### 第 4 周：AI 扩展
- [ ] 集成多模态 AI 服务
- [ ] 实现知识库自动学习
- [ ] 系统测试和优化

---

## 📝 相关文档

- [AI 系统提示词配置](docs/AI-SYSTEM-PROMPT-CONFIG.md)
- [批处理作业实现](docs/guides/batch-jobs.md)
- [钩子系统说明](docs/COMMON_HOOKS.md)
