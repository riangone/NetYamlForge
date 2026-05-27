# 🚗 Auto-Dealer-Demo — AI主导汽车销售管理系统

> **版本**: 1.1.0（AI全面主导化版）  
> **平台**: NetYamlForge  
> **更新日期**: 2026-05-27  
> **文档类型**: 设计说明 / 功能说明 / 使用指南 合集

---

## 📚 文档目录

1. [系统概述](#1-系统概述)
2. [系统架构设计](#2-系统架构设计)
3. [数据模型设计](#3-数据模型设计)
4. [AI引擎设计](#4-ai引擎设计)
5. [功能模块说明](#5-功能模块说明)
6. [自动化任务（批处理）说明](#6-自动化任务批处理说明)
7. [用户角色与权限](#7-用户角色与权限)
8. [各角色使用指南](#8-各角色使用指南)
9. [AI交互流程说明](#9-ai交互流程说明)
10. [人工确认机制](#10-人工确认机制)
11. [页面功能一览](#11-页面功能一览)

---

## 1. 系统概述

### 1.1 系统定位

Auto-Dealer-Demo 是一个以 **AI全面主导** 为核心理念的汽车销售管理系统。系统中的绝大多数业务决策和执行动作均由 AI（Gemini）自动完成，只有在以下情况才需要人工介入：

- AI 判断置信度低于设定阈值
- 涉及高金额折扣（≥10%）
- 特殊情况标记（高预算客户投诉等）
- 系统主动发起的人工审核请求

### 1.2 核心理念

```
传统流程：
  客户询问 → 人工登记 → 人工评分 → 人工跟进 → 人工报价 → 成交

AI主导流程：
  客户询问 → AI自动登记 → AI自动评分 → AI自动跟进
           → [置信度不足时] → 人工确认
           → AI自动报价 → AI自动发送 → 成交
```

### 1.3 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | NetYamlForge（ASP.NET Core + YAML配置驱动） |
| 数据库 | SQLite |
| AI引擎 | Gemini CLI（通过 `AiDealerEngineExecutor.cs` 调用） |
| 邮件服务 | IEmailServiceFactory（NetYamlForge内置） |
| 调度器 | 内置 BatchJobHostedService（cron表达式） |
| 多语言 | 日语（ja-JP）/ 时区：Asia/Tokyo |

---

## 2. 系统架构设计

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                    用户界面层（YAML Pages）                     │
│  CustomerDashboard │ SalesRepDashboard │ ExecDashboard       │
│  LeadKanban │ AiDecisionApproval │ AiDashboard │ OperatorChat│
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│                   业务逻辑层（Services）                        │
│  AiDealerEngineExecutor  │  AiCommunicationExecutor         │
│  BatchJobExecutor        │  BatchJobHostedService            │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│                    AI推理层（Gemini CLI）                       │
│  Lead Scoring  │  Nurturing Task Gen  │  Quote Generation    │
│  Email Composition │  Response Analysis │  Follow-up Gen      │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│                    数据持久化层（SQLite）                        │
│  sales_leads │ ai_decisions │ ai_quotes │ ai_communications   │
│  lead_nurturing_tasks │ ai_action_log │ customers │ vehicles  │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 AI执行引擎架构

```
BatchJobHostedService（cron调度）
  │
  ├── type: ai_dealer_engine
  │     └── AiDealerEngineExecutor.cs
  │           ├── mode: lead_scoring     → Gemini分析 → ai_decisions
  │           ├── mode: nurturing        → Gemini建议 → lead_nurturing_tasks
  │           └── mode: quote_generation → Gemini报价 → ai_quotes
  │
  └── type: ai_communication_sender
        └── AiCommunicationExecutor.cs
              ├── mode: nurturing_email  → Gemini生成邮件 → EmailService发送
              ├── mode: quote_email      → Gemini生成报价邮件 → EmailService发送
              └── mode: response_check   → 检测未回复 → 生成跟进任务
```

---

## 3. 数据模型设计

### 3.1 核心业务表

#### `sales_leads`（销售线索）
| 字段 | 类型 | 说明 |
|------|------|------|
| lead_id | VARCHAR(50) PK | 线索ID |
| customer_id | VARCHAR(50) | 客户ID |
| vehicle_id | VARCHAR(50) | 意向车辆ID |
| status | VARCHAR(20) | new/contacted/qualified/negotiating/won/lost |
| lead_score | INTEGER | AI评分（0-100） |
| assigned_sales | VARCHAR(50) | 责任销售 |
| budget_min/max | DECIMAL | 预算范围 |
| **ai_touch_count** | INTEGER | AI接触次数 |
| **ai_conversion_path** | TEXT | AI转化路径日志 |
| **ai_first_touch_conversation_id** | VARCHAR(64) | 首次AI对话ID |
| **ai_last_touch_conversation_id** | VARCHAR(64) | 最近AI对话ID |

#### `ai_decisions`（AI决定记录）
| 字段 | 类型 | 说明 |
|------|------|------|
| decision_id | VARCHAR(50) PK | 决定ID |
| decision_type | VARCHAR(50) | lead_scoring/nurturing_task/quote_generation/discount_approval/escalation |
| entity_type | VARCHAR(50) | 对象类型（sales_leads等） |
| entity_id | VARCHAR(50) | 对象ID |
| ai_reasoning | TEXT | AI判断理由全文 |
| confidence_score | DECIMAL(5,2) | 置信度（0-100） |
| status | VARCHAR(20) | **pending/auto_executed/approved/rejected** |
| requires_human | BOOLEAN | 是否需要人工确认 |
| executed_at | DATETIME | 执行时间 |

#### `lead_nurturing_tasks`（AI育成任务）
| 字段 | 类型 | 说明 |
|------|------|------|
| task_id | VARCHAR(50) PK | 任务ID |
| lead_id | VARCHAR(50) | 关联线索 |
| customer_id | VARCHAR(50) | 关联客户 |
| task_type | VARCHAR(30) | email/call/appointment/test_drive/catalog |
| priority_score | INTEGER | 优先度（0-100） |
| status | VARCHAR(20) | pending/in_progress/done/cancelled |
| ai_recommendation | TEXT | AI推荐内容 |
| ai_reasoning | TEXT | AI判断理由 |
| comm_sent_at | DATETIME | 邮件发送时间 |
| comm_id | VARCHAR(60) | 关联通信记录 |

#### `ai_quotes`（AI生成报价）
| 字段 | 类型 | 说明 |
|------|------|------|
| quote_id | VARCHAR(50) PK | 报价ID |
| lead_id / customer_id / vehicle_id | FK | 关联信息 |
| base_price | DECIMAL(12,2) | 车辆原价 |
| discount_amount | DECIMAL(12,2) | AI计算折扣额 |
| discount_rate | DECIMAL(5,2) | 折扣率（%） |
| final_price | DECIMAL(12,2) | 最终价格 |
| trade_in_value | DECIMAL(12,2) | 以旧换新估值 |
| down_payment | DECIMAL(12,2) | 首付金额 |
| monthly_payment | DECIMAL(12,2) | 月供金额 |
| loan_months | INTEGER | 贷款期数（月） |
| ai_confidence | DECIMAL(5,2) | AI置信度 |
| ai_reasoning | TEXT | AI报价依据 |
| status | VARCHAR(20) | draft/approved/sent/expired |
| quote_sent_at | DATETIME | 报价发送时间 |

#### `ai_communications`（AI通信记录）
| 字段 | 类型 | 说明 |
|------|------|------|
| comm_id | VARCHAR(60) PK | 通信ID |
| lead_id / customer_id | FK | 关联信息 |
| nurturing_task_id | VARCHAR(50) | 来源育成任务 |
| comm_channel | VARCHAR(20) | email/sms/line/phone_memo |
| subject | VARCHAR(200) | 邮件标题（AI生成） |
| body_text | TEXT | 正文（AI生成） |
| ai_personalized | INTEGER | 是否AI个性化（1=是） |
| ai_confidence | DECIMAL(5,2) | AI生成置信度 |
| send_status | VARCHAR(20) | pending/sent/failed/cancelled |
| response_received | INTEGER | 是否收到回复（1=是） |
| response_sentiment | VARCHAR(20) | positive/neutral/negative |
| response_summary | TEXT | 回复内容摘要 |
| requires_human | INTEGER | 是否需人工确认 |

#### `ai_action_log`（AI行动日志）
| 字段 | 类型 | 说明 |
|------|------|------|
| log_id | VARCHAR(64) PK | 日志ID |
| action_type | VARCHAR(50) | 动作类型 |
| entity_type / entity_id | VARCHAR | 对象信息 |
| ai_model | VARCHAR(50) | 使用的AI模型 |
| prompt_summary | TEXT | 提示摘要 |
| result_summary | TEXT | 结果摘要 |
| execution_ms | INTEGER | 执行耗时（毫秒） |

---

## 4. AI引擎设计

### 4.1 AiDealerEngineExecutor（核心AI决策引擎）

**文件**: `Services/BatchJob/AiDealerEngineExecutor.cs`

#### 执行流程

```
1. 读取 jobs.yml 中的 mode 参数
2. 执行对应的 SQL 查询（jobs/sql/）获取候选数据
3. 将候选数据格式化为 Gemini CLI 的提示词（Prompt）
4. 调用 Gemini CLI 获取 AI 分析结果
5. 解析结果（JSON格式）
6. 判断 confidence_score vs autoExecuteThreshold：
   - 置信度 ≥ 阈值 → 自动执行，写入数据库，状态 = auto_executed
   - 置信度 < 阈值 → 写入 ai_decisions，状态 = pending，等待人工审核
7. 记录 ai_action_log
```

#### 三种执行模式

| 模式 | 触发条件 | SQL来源 | 输出表 | 自动执行阈值 |
|------|---------|---------|--------|------------|
| `lead_scoring` | 7天以上未更新的线索 | `ai_lead_rescore.sql` | `ai_decisions` + `sales_leads.lead_score` | 85% |
| `nurturing` | 评分30-79的线索 | `ai_nurturing_candidates.sql` | `lead_nurturing_tasks` | 80% |
| `quote_generation` | 评分≥80的线索 | `ai_quote_candidates.sql` | `ai_quotes` | 90% |

### 4.2 AiCommunicationExecutor（AI通信执行引擎）

**文件**: `Services/BatchJob/AiCommunicationExecutor.cs`

#### 三种执行模式

| 模式 | 触发条件 | 执行内容 | 自动发送阈值 |
|------|---------|---------|------------|
| `nurturing_email` | pending状态的育成任务（email/appointment/test_drive类型） | Gemini生成个性化邮件 → EmailService发送 | 80% |
| `quote_email` | approved状态的AI报价（未发送） | Gemini生成报价邮件 → EmailService发送 → 更新ai_quotes.status=sent | 85% |
| `response_check` | 3天以上未回复的已发邮件 | 检测 → Gemini判断最优跟进方式 → 自动追加lead_nurturing_tasks | — |

### 4.3 AI置信度机制

```
置信度 (confidence_score) 含义：
  90-100% → 高度确信，系统自动执行，无需人工
  80-89%  → 较高确信，大多数情况自动执行
  70-79%  → 中等确信，进入人工审核队列
  0-69%   → 低置信度，必须人工确认

特殊规则（强制人工确认）：
  - 折扣率 > 10%
  - 涉及预算 ≥ 500万的客户
  - 系统标记为"投诉"的情况
  - requires_human = 1 的任何决定
```

---

## 5. 功能模块说明

### 5.1 线索管理模块

**涉及页面**: `SalesLeads.yaml`, `LeadKanban.yaml`

| 功能 | 执行者 | 说明 |
|------|--------|------|
| 线索状态跟踪 | 人工 + AI辅助 | Kanban视图，状态可拖拽更新 |
| AI评分显示 | AI自动 | 显示当前lead_score及AI推荐理由 |
| AI推荐行动 | AI自动 | 页面底部显示AI推荐的下一步动作 |
| AI育成任务 | AI自动 | LeadKanban中显示pending育成任务 |
| 手动状态更新 | 人工 | 成交/失单等关键状态变更 |

### 5.2 AI决策管理模块

**涉及页面**: `AiDecisionApproval.yaml`, `AiDashboard.yaml`

| 功能 | 说明 |
|------|------|
| 紧急承认面板 | 显示 requires_human=1 的决定，置顶显示 |
| 承认待ち一覧 | 全部pending状态的AI决定列表 |
| 自动执行队列 | 置信度≥90%、无需人工的决定列表 |
| 审核历史 | 已审核（approved/rejected/auto_executed）记录 |
| KPI统计 | 承认待件数 / 高置信度件数 / 人工必须件数 |

### 5.3 AI通信管理模块

**涉及页面**: `AiCommunications.yaml`, `OperatorChat.yaml`

| 功能 | 说明 |
|------|------|
| 邮件发送状态监控 | pending/sent/failed状态追踪 |
| 回复率追踪 | 是否收到回复、回复情感分析 |
| 待审批通信 | requires_human=1的通信等待人工确认 |
| 无回复检测 | 3天未回复自动触发跟进 |
| 运营商对话管理 | 人工客服对话记录与AI推荐回答 |

### 5.4 报价管理模块

**涉及页面**: `AiDecisionApproval.yaml`（quote_generation类型）

| 功能 | 说明 |
|------|------|
| AI报价生成 | 基于车辆价格、客户预算、库存情况自动生成 |
| 报价审批 | 折扣≥10%时需人工审批 |
| 报价邮件发送 | 审批后自动发送给客户 |
| 贷款计算 | AI自动计算月供、首付建议 |

### 5.5 销售业绩模块

**涉及页面**: `SalesRepDashboard.yaml`, `ExecDashboard.yaml`

| 功能 | 角色 | 说明 |
|------|------|------|
| AI朝礼ウィジェット | sales_rep | 每日TOP5 AI推荐行动（页面最顶部） |
| 个人KPI | sales_rep | 担当线索数/本月成交/新规线索 |
| 经营ROI仪表盘 | executive | 全局成交率、AI贡献度、ROI分析 |

### 5.6 顾客服务模块

**涉及页面**: `CustomerDashboard.yaml`, `CustomerAppointments.yaml`, `CustomerAiChat.yaml`

| 功能 | 说明 |
|------|------|
| 顾客个人仪表盘 | 个人信息、车辆信息、预约状态 |
| 预约管理 | 试驾/维修预约查看与新建 |
| AI通信历史 | 顾客查看收到的AI发送的消息 |
| AI提案查看 | 查看AI为该顾客生成的提案 |

---

## 6. 自动化任务（批处理）说明

### 6.1 任务调度时间表

```
时间（Tokyo）    任务名称                    执行内容
─────────────────────────────────────────────────────────────
每日 10:00      appointment_reminder         发送明日预约提醒
平日 08:00      ai_nurturing_generator       AI生成育成任务（评分30-79的线索）
平日 09:00      stale_lead_alert             停滞线索告警报告
平日 09:00      ai_quote_generator           AI生成报价（评分≥80的线索）
平日 10:30      ai_nurturing_email_sender    AI发送育成邮件
平日 11:00      ai_quote_email_sender        AI发送报价邮件
平日 14:00      ai_response_check            检测无回复、生成追跟任务
每周一 09:00    vip_customer_followup         VIP顾客跟进提取
每2小时         ai_lead_scorer               AI重新评分（7天未更新线索）
```

### 6.2 任务失败处理

所有任务均配置：
- 失败重试次数：2次
- 重试间隔：300秒（5分钟）
- 错误日志：开启

---

## 7. 用户角色与权限

### 7.1 角色定义

| 角色 | 说明 | 登录后默认页面 |
|------|------|--------------|
| `customer` | 顾客 | CustomerDashboard |
| `operator` | 客服运营 | OperatorConsole |
| `sales_rep` | 销售担当 | SalesRepDashboard |
| `sales_manager` | 销售经理 | LeadKanban |
| `service_staff` | 服务技师 | Appointments |
| `executive` | 高管 | ExecDashboard |
| `admin` | 系统管理员 | — |

### 7.2 页面访问权限矩阵

| 页面 | customer | operator | sales_rep | sales_manager | service_staff | executive |
|------|----------|----------|-----------|---------------|---------------|-----------|
| CustomerDashboard | ✅ | — | — | — | — | — |
| CustomerAiChat | ✅ | — | — | — | — | — |
| SalesRepDashboard | — | — | ✅ | ✅ | — | — |
| LeadKanban | — | — | ✅ | ✅ | — | ✅ |
| SalesLeads | — | — | ✅ | ✅ | — | — |
| AiDecisionApproval | — | — | — | ✅ | — | ✅ |
| AiCommunications | — | ✅ | — | ✅ | — | ✅ |
| AiDashboard | — | — | — | ✅ | — | ✅ |
| AiAssistant | — | ✅ | ✅ | ✅ | — | ✅ |
| OperatorChat | — | ✅ | — | ✅ | — | ✅ |
| ExecDashboard | — | — | — | ✅ | — | ✅ |
| VehicleInventory | — | — | ✅ | ✅ | ✅ | — |
| Appointments | — | — | — | ✅ | ✅ | — |

---

## 8. 各角色使用指南

### 8.1 👤 顾客（Customer）

**登录后看到**: 个人仪表盘 → 车辆信息 / 预约状态 / AI发来的消息

**日常操作流程**:
1. 登录后进入 **マイページ（个人主页）**
2. 查看「**予約・サービス確認**」确认预约状态
3. 查看「**在庫車両を見る**」浏览意向车辆
4. 查看「**💬 AIチャット・メッセージ**」查看AI发来的提案和消息

**注意**: 顾客无法主动发起AI对话（系统由AI主动联系）

---

### 8.2 🎯 销售担当（Sales Rep）

**登录后看到**: 个人KPI仪表盘，**顶部显示今日AI推荐行动TOP5**

**日常操作流程**:

**早上开始工作（必看）**:
1. 进入「**担当者パフォーマンス**」
2. 查看页面最顶部「🌅 今日のAI推奨アクション TOP5」
3. 按优先顺序执行AI推荐的任务（电话/邮件/试驾等）

**线索管理**:
1. 进入「**🔥 リードパイプライン**（Kanban）」
2. 查看页面中部的「🤖 AI育成タスク（実行待ちTOP10）」
3. 查看「⏳ AI承認待ち決定」中需要确认的事项
4. 在「**📋 セールスリード管理**」查看AI推荐行动

**使用AI助手**:
1. 进入「**🤖 AIアシスタント**」
2. 查看AI稼働状況KPI（承認待ち決定数 / 今日自動実行済み数）
3. 查看「今日のAI推奨アクション TOP10」了解全局动态

---

### 8.3 🔑 销售经理（Sales Manager）

**登录后看到**: Lead Kanban 看板

**日常操作流程**:

**AI决定审批（每日必做）**:
1. 进入「**🤖 AI 決定承認**」
2. **优先处理页面最顶部「🚨 緊急承認が必要な決定」**（红色警告区域）
3. 逐条查看AI判断理由（`ai_reasoning`字段）
4. 点击「✅ 承認」或「❌ 却下」
5. 查看「🟢 自動実行可能（確信度90%以上）」确认AI是否自动处理正确

**AI通信监控**:
1. 进入「**📧 AI コミュニケーション**」
2. 查看邮件发送状态（已发送/失败/待发）
3. 查看客户回复率和情感分析结果

**业绩报告**:
1. 进入「**📊 経営ダッシュボード（ROI）**」
2. 查看全局销售漏斗、AI贡献指标

---

### 8.4 🎧 客服运营（Operator）

**登录后看到**: OperatorConsole（AI引流管理）

**日常操作流程**:

**AI引流处理**:
1. 进入「**🎧 オペレーターチャット**」
2. 查看「🔴 優先対応キュー」（紧急+高优先对话）
3. 参考「💡 AI推奨回答パターン」处理客户咨询
4. 查看「📋 AI対話履歴」了解历史对话

**AI通信审核**:
1. 进入「**📧 AI コミュニケーション**」
2. 审核需要人工确认的AI通信（requires_human=1）
3. 在「**🤖 AIアシスタント**」查看AI总体运行状态

---

### 8.5 📊 高管（Executive）

**登录后看到**: 经营仪表盘（ROI全局视图）

**日常操作流程**:

**全局AI监控**:
1. 进入「**📊 AI ダッシュボード**」
2. 查看AI活动7种图表：决定类型分布 / 育成任务状态 / 通信量趋势 / 成功率等

**战略审批**:
1. 进入「**🤖 AI 決定承認**」
2. 审批高价值决定（大额折扣、VIP客户special case等）

---

## 9. AI交互流程说明

### 9.1 从线索到成交的完整AI主导流程

```
第1步: 线索录入
  操作者/系统 录入新线索
  ↓
第2步: AI评分（每2小时运行）
  ai_lead_scorer 启动
  → Gemini分析客户数据（预算/意向车型/联系历史）
  → 生成 lead_score (0-100)
  → 置信度≥85% → 自动更新数据库
  → 置信度<85% → 进入人工审批队列

第3步: AI育成（平日08:00运行）
  ai_nurturing_generator 启动
  → 筛选评分30-79的线索
  → Gemini生成最优跟进策略（电话/邮件/试驾）
  → 置信度≥80% → 自动写入 lead_nurturing_tasks
  → 展示在 SalesRepDashboard 的「今日AI推奨TOP5」

第4步: AI邮件发送（平日10:30运行）
  ai_nurturing_email_sender 启动
  → 读取 pending 状态的育成任务
  → Gemini生成个性化邮件文本
  → 置信度≥80% → EmailService自动发送
  → 记录到 ai_communications

第5步: 回复监控（平日14:00运行）
  ai_response_check 启动
  → 检测3天未回复的邮件
  → Gemini判断最优跟进方式
  → 自动追加新的 lead_nurturing_tasks

第6步: 报价生成（平日09:00运行）
  ai_quote_generator 启动
  → 筛选评分≥80的成熟线索
  → Gemini计算最优报价（价格/折扣/贷款方案）
  → 置信度≥90% 且 折扣率<10% → 自动生成 ai_quotes

第7步: 报价发送（平日11:00运行）
  ai_quote_email_sender 启动
  → 读取 approved 状态的报价
  → Gemini生成报价邮件（含金额表格/贷款亮点）
  → 置信度≥85% → 自动发送给客户
  → 更新 ai_quotes.status = 'sent'

第8步: 成交确认（人工）
  销售担当 在 SalesLeads 页面
  → 将 status 更新为 'won'（唯一需人工确认的关键节点）
```

### 9.2 人工介入触发条件一览

| 触发条件 | 说明 |
|---------|------|
| AI置信度 < 设定阈值 | 评分<85% / 育成<80% / 报价<90% |
| 折扣率 ≥ 10% | 高折扣需经理审批 |
| requires_human = 1 | AI主动标记需要人工 |
| 特殊客户标记 | 投诉客户、VIP客户特殊case |
| 最终成交确认 | 状态改为'won'必须人工操作 |

---

## 10. 人工确认机制

### 10.1 AI决定承认页面（AiDecisionApproval）操作方法

**进入路径**: 左侧菜单 → AI管理 → AI決定承認

**页面结构（从上到下）**:

```
🚨 緊急承認が必要な決定（最优先处理）
   → requires_human=1 的决定
   → ✅ 承認 / ❌ 却下 按钮（绿色/红色，一目了然）

📊 承認待ちサマリー KPI
   → 承認待ち件数 / 高確信度件数 / 人間承認必須件数 / 本日作成件数

📊 決定種別別 承認待ち件数（棒グラフ）
   → 可视化各类型待审批分布

⏳ 承認待ち一覧（全部pending列表）
   → 显示：決定ID / 決定種別 / 確信度 / 人間承認 / AI判断理由
   → ✅ 承認 / ❌ 却下 操作

🟢 自動実行可能（確信度90%以上）
   → 确认AI自动执行是否合理
   → 一括自動実行へ 按钮

📋 最近の承認履歴（直近20件）
   → 查看历史处理记录
```

### 10.2 人工处理决定的标准判断依据

1. **查看 AI判断理由（ai_reasoning）**: 理解AI为什么做此决定
2. **查看 確信度**: 数字越高越可靠
3. **判断标准**:
   - AI推荐与实际业务逻辑一致 → **承認**
   - AI推荐存在明显错误或风险 → **却下**
   - 不确定时 → 可先查看相关线索/客户信息再决定

---

## 11. 页面功能一览

| 页面文件 | 页面名称 | 主要功能 |
|---------|---------|---------|
| `Landing.yaml` | 公开首页 | 车辆展示、系统介绍 |
| `Welcome.yaml` | スタートガイド | 新员工引导页 |
| `CustomerDashboard.yaml` | 顾客仪表盘 | 顾客个人信息/车辆/预约状态 |
| `CustomerAppointments.yaml` | 顾客预约 | 试驾/维修预约管理 |
| `CustomerVehicles.yaml` | 顾客车辆 | 意向车辆浏览 |
| `CustomerAiChat.yaml` | AIチャット | 顾客查看AI发来的消息和提案 |
| `SalesRepDashboard.yaml` | 担当者KPI | **AI朝礼TOP5** + 个人业绩KPI |
| `LeadKanban.yaml` | リードKanban | **AI育成任务** + AI承認待ち + 销售漏斗 |
| `SalesLeads.yaml` | リード管理 | 线索列表 + **AI推荐行动区** |
| `VehicleInventory.yaml` | 车辆在库 | 库存管理 |
| `Customers.yaml` | 顾客管理 | 顾客档案 |
| `Appointments.yaml` | 预约管理 | 服务预约（service_staff） |
| `ServiceRequests.yaml` | 服务依赖 | 维修依赖管理 |
| `OperatorConsole.yaml` | 运营控制台 | AI引流管理 |
| `OperatorChat.yaml` | オペレーターチャット | **AI推荐回答** + 对话历史管理 |
| `AiDecisionApproval.yaml` | AI決定承認 | **核心承认页** 人工审批AI决定 |
| `AiCommunications.yaml` | AI通信管理 | 邮件发送状态 / 回复率追踪 |
| `AiDashboard.yaml` | AI仪表盘 | 7种图表展示AI活动全局 |
| `AiAssistant.yaml` | AIアシスタント | AI稼働状況 + 今日推荐行动TOP10 |
| `ExecDashboard.yaml` | 经营仪表盘 | 全局ROI / AI贡献度 |

---

## 附录：文件结构一览

```
auto-dealer-demo/
├── project.yaml                    # 项目配置（路由/权限/导航）
├── docs/
│   └── README.md                   # 本文档
├── database/
│   ├── init.sql                    # 初始表结构
│   ├── init_seed.sql               # 初始测试数据
│   ├── migration_ai_full.sql       # Phase1-2: AI核心表
│   ├── migration_phase3_comms.sql  # Phase3: AI通信表
│   └── auto-dealer-demo.db         # SQLite数据库文件
├── entities/                       # CRUD实体定义
│   ├── ai_decisions.yml
│   ├── ai_quotes.yml
│   ├── ai_communications.yml
│   ├── lead_nurturing_tasks.yml
│   ├── sales_leads.yml
│   ├── customers.yml
│   ├── vehicles.yml
│   └── ...
├── pages/                          # UI页面定义
│   ├── AiDecisionApproval.yaml     ← 核心AI承认页
│   ├── AiDashboard.yaml
│   ├── AiAssistant.yaml
│   ├── AiCommunications.yaml
│   ├── OperatorChat.yaml
│   ├── CustomerAiChat.yaml
│   ├── SalesRepDashboard.yaml
│   ├── LeadKanban.yaml
│   ├── SalesLeads.yaml
│   └── ...
├── jobs/
│   ├── jobs.yml                    # 全部批处理任务定义（9个任务）
│   └── sql/                        # AI用SQL查询
│       ├── ai_lead_rescore.sql
│       ├── ai_nurturing_candidates.sql
│       └── ai_quote_candidates.sql
├── Services/BatchJob/              # AI执行引擎
│   ├── AiDealerEngineExecutor.cs   ← Phase1-2核心
│   └── AiCommunicationExecutor.cs  ← Phase3核心
└── Hooks/
    └── AutoDealerHooks.cs          # 事件钩子
```

---

*本文档由 Hyperion（AI架构助手）自动生成。*  
*如需更新，请在改动对应功能后同步修改本文档。*
