# 汽车销售 AI 系统智能化提升 - 完整方案

> **文档版本**: 1.0  
> **创建日期**: 2026 年 3 月 31 日  
> **目标**: 将 AI 从"被动查询助手"升级为"主动销售伙伴"

---

## 📋 目录

1. [现状分析](#现状分析)
2. [提升目标](#提升目标)
3. [实施路线图](#实施路线图)
4. [技术实现](#技术实现)
5. [预期效果](#预期效果)
6. [快速开始](#快速开始)

---

## 现状分析

### 当前系统能力 ✅

| 功能 | 状态 | 说明 |
|------|------|------|
| 数据查询 | ✅ 完善 | 支持自然语言查询车辆、客户、线索、预约 |
| 情感分析 | ✅ 基础 | 可检测客户负面情绪并自动升级 |
| 线索创建 | ✅ 自动 | 从 AI 对话自动创建销售线索 |
| 基础推荐 | ⚠️ 简单 | 基于规则的简单推荐 |
| 话术生成 | ⚠️ 模板 | 固定模板，缺少个性化 |

### 主要不足 ❌

- ❌ **被动响应**: AI 仅回答用户问题，不主动提供建议
- ❌ **缺少洞察**: 返回数据但不解读数据背后的含义
- ❌ **无个性化**: 对所有客户用同样的方式沟通
- ❌ **无预测能力**: 不能预测销售趋势、客户流失风险
- ❌ **无竞品情报**: 销售人员手动准备竞品对比资料

---

## 提升目标

### 量化目标

| 指标 | 当前 | 目标 | 提升幅度 |
|------|------|------|----------|
| 线索转化率 | ~15% | ~25% | **+67%** |
| 平均销售周期 | 21 天 | 14 天 | **-33%** |
| 试驾预约率 | 8% | 20% | **+150%** |
| 客户满意度 | 3.8/5 | 4.5/5 | **+18%** |
| 销售人员效率 | 8 单/月 | 12 单/月 | **+50%** |
| 流失客户挽回 | 5% | 15% | **+200%** |

### 质化目标

- 🎯 **主动发现机会**: AI 自动识别高价值线索、流失风险、促销时机
- 📊 **数据驱动决策**: 提供深度分析，不只是原始数据
- 💡 **个性化沟通**: 根据客户画像生成定制化话术
- 🔮 **预测能力**: 销售预测、流失预警、库存优化
- 📚 **持续学习**: 从成功案例中学习，不断优化

---

## 实施路线图

### 第一阶段（1-2 周）：智能销售助手

#### 1.1 新增实体

```yaml
# 核心实体
- lead_nurturing_tasks      # 线索培育任务
- ai_sales_scripts          # AI 销售话术库
- customer_behavior_scores  # 客户行为评分
- competitor_intelligence   # 竞品情报库
- test_drive_tracking       # 试驾追踪
```

#### 1.2 批处理作业

```yaml
# 每日自动执行
- lead_nurturing_generator  # 生成培育任务（每天 9:00）
- customer_behavior_scorer  # 计算行为评分（每天 2:00）
```

#### 1.3 AI 能力提升

- ✅ 主动机会识别（库存分析、线索质量分析）
- ✅ 智能车辆推荐（基于客户画像）
- ✅ 销售话术生成（场景化、个性化）
- ✅ 竞品应对策略（自动对比、话术生成）

**详细文档**: 
- [实体定义指南](entities/ENHANCED-ENTITIES-GUIDE.md)
- [批处理作业](jobs/AI-ENHANCEMENT-JOBS.md)
- [AI 提示词增强](AI-PROMPT-ENHANCED.md)

---

### 第二阶段（1-2 周）：客户体验增强

#### 2.1 虚拟试驾助手

```
流程:
1. AI 询问需求（场景、家庭、预算）
2. 推荐 2-3 款车型（附理由）
3. 智能推荐试驾时间
4. 自动确认（短信/邮件）
5. 提醒跟进（前 1 天、前 2 小时）
6. 试驾后反馈收集
```

#### 2.2 购车指南 PDF 生成

```csharp
// 自动生成个性化购车指南
// 包含：需求分析、车型对比、贷款方案、保养成本
BuyingGuideService.GenerateGuideAsync(customerId, vehicleIds)
```

#### 2.3 以旧换新估价

```
AI 询问 → 车辆信息 → 照片识别（可选）→ AI 估价 → 置换方案
```

**实现文件**:
- `Services/AI/TestDriveAssistantService.cs`
- `Services/Pdf/BuyingGuideService.cs`
- `Services/AI/VehicleTradeInService.cs`

---

### 第三阶段（2-3 周）：数据驱动决策

#### 3.1 销售预测仪表板

```sql
-- 视图：v_sales_forecast
-- 显示：目标达成率、预测成交、pipeline 分析
```

#### 3.2 客户流失预警

```yaml
# 自动检测高风险客户
- 线索评分≥70 但 14 天未联系
- 试驾后 7 天无跟进
- 价格咨询后无下文

# 自动生成挽回任务
churn_prediction 作业
```

#### 3.3 竞争情报分析

```
- 自动抓取竞品价格、促销信息
- AI 生成对比分析表
- 生成应对话术
```

**实现文件**:
- `Pages/SalesForecast.yaml`
- `Jobs/churn_prediction.yml`
- `Services/Analytics/SalesForecastService.cs`

---

### 第四阶段（2-3 周）：AI 能力扩展

#### 4.1 多模态 AI

```
- 车辆照片识别 VIN
- 车况评估（基于照片）
- 语音输入支持
- 文档 OCR（驾照、保险单）
```

#### 4.2 知识库自动学习

```csharp
// 从成功成交对话中学习
KnowledgeExtractor.ExtractFromSuccessfulDealsAsync()
// 输出：可复用的销售脚本、最佳实践
```

#### 4.3 智能排班

```
AI 分析:
- 各时段客流量
- 销售人员擅长领域
- 客户预约偏好

输出：最优排班建议
```

---

## 技术实现

### 核心服务架构

```
NetYamlForge/Services/AI/
├── AutoDealerChatService.cs      # 现有：对话处理
├── QueryParserService.cs          # 现有：自然语言解析
├── QueryExecutionService.cs       # 现有：查询执行
├── QueryResultFormatter.cs        # 现有：结果格式化
│
├── VehicleRecommendationService.cs  # 新增：车辆推荐
├── SalesScriptGenerator.cs          # 新增：话术生成
├── TestDriveAssistantService.cs     # 新增：试驾助手
├── CustomerBehaviorAnalyzer.cs      # 新增：行为分析
├── ChurnPredictionService.cs        # 新增：流失预测
└── KnowledgeExtractor.cs            # 新增：知识学习
```

### 数据库变更

```sql
-- 新增表（由实体 YAML 自动生成）
CREATE TABLE lead_nurturing_tasks (...);
CREATE TABLE ai_sales_scripts (...);
CREATE TABLE customer_behavior_scores (...);
CREATE TABLE competitor_intelligence (...);
CREATE TABLE test_drive_tracking (...);

-- 新增视图
CREATE VIEW v_sales_forecast AS ...;
CREATE VIEW v_customer_360 AS ...;
CREATE VIEW v_inventory_aging AS ...;
```

### AI 提示词升级

关键变更：

```diff
- 您是汽车销售 AI 业务助手
+ 您是汽车销售专家 AI 助手，不仅是数据查询工具，更是：
+ - 主动销售伙伴
+ - 数据分析顾问
+ - 策略建议专家
+ - 客户体验设计师
```

**完整提示词**: [AI-PROMPT-ENHANCED.md](AI-PROMPT-ENHANCED.md)

---

## 预期效果

### ROI 分析

#### 投资成本

| 项目 | 成本 |
|------|------|
| 开发人力（4 周×2 人） | ¥1,200,000 |
| AI API 调用费（月） | ¥50,000 |
| 服务器增量成本（月） | ¥20,000 |
| **首月总投入** | **¥1,270,000** |

#### 预期收益（月）

| 收益来源 | 金额 | 计算方式 |
|----------|------|----------|
| 增加销量 | ¥3,000,000 | +4 台 × ¥750,000/台 |
| 减少流失 | ¥600,000 | 挽回 3 台 × ¥200,000/台 |
| 效率提升 | ¥400,000 | 节省 2 人人力 |
| **月总收益** | **¥4,000,000** |

#### 投资回报

- **首月 ROI**: (4,000,000 - 1,270,000) / 1,270,000 = **215%**
- **回本周期**: **< 1 个月**
- **年度收益**: ¥48,000,000 - ¥840,000 = **¥47,160,000**

---

## 快速开始

### 步骤 1: 创建实体

```bash
# 复制实体 YAML 文件
cd NetYamlForge/projects/auto-dealer-demo/entities/

# 创建以下文件（内容参考 ENHANCED-ENTITIES-GUIDE.md）
- lead_nurturing_tasks.yml
- ai_sales_scripts.yml
- customer_behavior_scores.yml
- competitor_intelligence.yml
- test_drive_tracking.yml
```

### 步骤 2: 添加钩子代码

编辑 `Hooks/AutoDealerHooks.cs`，添加以下钩子类：

```csharp
public class SetTaskTimestampsHook : IEntityHook { ... }
public class CalculateTaskPriorityHook : IEntityHook { ... }
public class GenerateAiRecommendationHook : IEntityHook { ... }
// 参考 ENHANCED-ENTITIES-GUIDE.md 完整代码
```

### 步骤 3: 创建批处理作业

```bash
# 创建作业 YAML 文件
cd NetYamlForge/projects/auto-dealer-demo/jobs/

# 创建以下文件（内容参考 AI-ENHANCEMENT-JOBS.md）
- lead_nurturing_generator.yml
- customer_behavior_scorer.yml
- sales_forecast_generator.yml
```

### 步骤 4: 更新 AI 提示词

编辑 `skills/auto-dealer/_system-prompt-staff.md`，参考 [AI-PROMPT-ENHANCED.md](AI-PROMPT-ENHANCED.md) 升级提示词。

### 步骤 5: 测试验证

```bash
# 1. 启动应用
dotnet run --project NetYamlForge

# 2. 访问实体管理页面
# http://localhost:5000/auto-dealer-demo/DynamicEntity/Index?entity=lead_nurturing_tasks

# 3. 手动执行作业
dotnet run --project NetYamlForge -- --execute-job \
  --project=auto-dealer-demo \
  --job=lead_nurturing_generator

# 4. 测试 AI 对话
# 访问：http://localhost:5000/auto-dealer-demo/Page/AIDashboard
```

### 步骤 6: 监控效果

```sql
-- 查看生成的任务
SELECT task_type, COUNT(*), AVG(priority_score)
FROM lead_nurturing_tasks
GROUP BY task_type;

-- 查看任务转化效果
SELECT 
  lnt.task_type,
  COUNT(*) as tasks,
  SUM(CASE WHEN sl.status = 'won' THEN 1 END) as won
FROM lead_nurturing_tasks lnt
LEFT JOIN sales_leads sl ON lnt.lead_id = sl.lead_id
GROUP BY lnt.task_type;
```

---

## 相关文档

### 设计文档

- [AI 增强计划](AI-ENHANCEMENT-PLAN.md) - 完整实施计划
- [AI 提示词增强](AI-PROMPT-ENHANCED.md) - AI 响应行为设计
- [实体定义指南](entities/ENHANCED-ENTITIES-GUIDE.md) - 新增实体 YAML

### 实现文档

- [批处理作业](jobs/AI-ENHANCEMENT-JOBS.md) - 自动化作业示例
- [钩子实现](Hooks/AutoDealerHooks.cs) - 业务逻辑钩子

### 配置文档

- [AI 系统提示词配置](../../../docs/AI-SYSTEM-PROMPT-CONFIG.md)
- [批处理作业说明](../../../docs/guides/batch-jobs.md)
- [钩子系统说明](../../../docs/COMMON_HOOKS.md)

---

## 常见问题

### Q1: 现有系统会受影响吗？

**A**: 不会。所有新增功能都是增量式的，现有查询、对话功能保持不变。

### Q2: AI API 调用成本会增加多少？

**A**: 预计每月增加 ¥50,000（基于 1000 次对话/天，每次 ¥0.05）。

### Q3: 需要多长时间能看到效果？

**A**: 
- 第 1 周：基础功能上线
- 第 2 周：开始生成培育任务
- 第 4 周：转化率开始提升
- 第 8 周：达到稳定状态

### Q4: 销售人员如何配合？

**A**: 
- 每天查看 AI 生成的任务列表
- 按优先级执行跟进任务
- 记录跟进结果（用于 AI 学习）
- 反馈话术效果（用于优化）

### Q5: 如何保证数据隐私？

**A**: 
- 所有数据存储在本地 SQLite/PostgreSQL
- AI API 调用仅发送必要的文本内容
- 不传输客户敏感信息（电话、邮箱等）
- 符合日本个人信息保护法（APPI）

---

## 联系支持

- **技术问题**: 查看 [NetYamlForge 文档](../../../README-ja.md)
- **业务咨询**: 参考 [AI 增强计划](AI-ENHANCEMENT-PLAN.md)
- **Bug 报告**: 提交 Issue 到 GitHub

---

*最后更新：2026 年 3 月 31 日*
