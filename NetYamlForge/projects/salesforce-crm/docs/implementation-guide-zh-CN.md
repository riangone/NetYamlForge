# salesforce-crm 实施指南（详细版）

## 1. 目标与范围
`salesforce-crm` 是基于现有 Dynamic CRUD 框架，分阶段构建可落地的 Salesforce 风格 CRM 子项目。

本文档定义：
- 必需功能与页面清单
- 数据模型映射策略
- 实施优先级（MVP 到生产）
- 验收标准

## 2. 当前状态与差距
### 2.1 已实现
- 专用页面（覆盖 Sales/Revenue/Service/Marketing/Admin）：
  - Executive Cockpit / Lead Command Center / Lead Inbox / Lead Detail 360
  - Account Workspace / Contact Workspace / Opportunity Workspace / Opportunity Detail / Pipeline Board
  - Activity Console / Quote Builder / Forecast Console
  - Service Desk 360 / Case Queue / Case Detail / Omni-Channel Console / Knowledge Base / SLA Monitor
  - Campaign Planner / Campaign Member / Attribution Dashboard
  - Approval Inbox / Automation Rules / Assignment Rules / Duplicate Rules
  - Object Manager / Role Access Matrix / User Role Profile / Data Import Export / Audit Trail
  - Integration Hub / Webhook Delivery Monitor / Order Management / Contract Center / Invoice & Payment
- CRUD 实体：
  - `order / customer / orderdetail / employee / product / supplier / shipper / category`
- 多语言：
  - `en-US / zh-CN / ja-JP / ko-KR`

### 2.2 主要缺口
- 当前仍以 `northwind` 映射替代 Salesforce 专有对象（Lead/Case/Quote/Contract 等）
- 审批、分配、去重主要通过页面导向，尚未补齐 Flow 类 API/后台作业
- 权限控制已覆盖 Admin/普通用户操作边界，并包含管理敏感字段保护

### 2.3 运维可追溯性（已实现）
- 通过 `Page` 的关键更新/删除操作会写入 `AuditLog`，动作类型为 `page_update / page_delete`
- 可在 `Case Queue / Approval Inbox / Opportunity Detail` 等操作链路追踪审计记录

## 3. 按业务域划分的必需页面

## 3.1 Sales Cloud（销售）
1. `Lead Inbox`（线索池）
- 作用：接收线索、自动分配、去重校验
- 操作：assign / merge / qualify / reject

2. `Lead Detail 360`
- 作用：线索信息、活动轨迹、跟进历史一体化
- 操作：新增活动、设置下次跟进、附件管理

3. `Account Workspace`
- 作用：企业维度聚合商机、订单、服务记录
- 操作：归属调整、客户等级、关键联系人维护

4. `Contact Workspace`
- 作用：联系人及决策链管理
- 操作：更新角色、影响力、关系人

5. `Opportunity Board`（看板拖拽）
- 作用：按阶段管理商机推进
- 操作：拖拽阶段变更

6. `Opportunity Detail`
- 作用：金额、概率、竞品、下一步计划管理
- 操作：阶段/概率更新、风险登记

7. `Activity Console`
- 作用：任务/电话/会议/邮件统一管理
- 操作：创建任务、变更优先级与截止日

8. `Quote Builder`
- 作用：报价生成、版本管理、审批提交
- 操作：报价行编辑、折扣审批、报价确认

## 3.2 Revenue Cloud（订单与收入）
1. `Order Management`
- 作用：订单生命周期管理
- 操作：状态流转、延期处理

2. `Contract Center`
- 作用：合同周期、续签、终止管理
- 操作：续签提醒、自动续签判定

3. `Invoice & Payment`
- 作用：开票与回款追踪
- 操作：核销、逾期预警

4. `Forecast Console`
- 作用：按周期/团队/销售预测收入
- 操作：commit 调整、预测冻结

## 3.3 Service Cloud（客服）
1. `Case Queue`
- 作用：工单受理、优先级排序、分派
- 操作：assign / escalate / resolve

2. `Case Detail`
- 作用：工单流程、SLA、知识库联动
- 操作：状态流转、根因分类

3. `Omni-Channel Console`
- 作用：多渠道队列与负载均衡
- 操作：自动重分配、响应时长监控

4. `Knowledge Base`
- 作用：知识文章创建、审核、发布
- 操作：草稿、评审、发布

5. `SLA Monitor`
- 作用：SLA 预警与违规处置
- 操作：延期审批、优先级重排

## 3.4 Marketing（营销）
1. `Campaign Planner`
2. `Campaign Member`
3. `Attribution Dashboard`

## 3.5 Platform / Admin（平台管理）
1. `Approval Inbox`
2. `Automation Rules`（Flow 类）
3. `Assignment Rules`
4. `Duplicate Rules`
5. `User/Role/Profile`
6. `Object/Field Manager`
7. `Data Import/Export`
8. `Audit Trail`
9. `Integration Hub`（API/Webhook）

## 4. 数据模型策略（基于当前数据库的临时映射）
基于 `northwind` 的临时映射：
- Lead/Account: `Customers`
- Contact: `Customers.ContactName`（后续建议拆分独立表）
- Opportunity: `Orders`
- OpportunityLineItem: `OrderDetails`
- Product/Price: `Products`
- Owner/SalesRep: `Employees`
- Case（临时）: `Orders.Status in ('Delayed','Cancelled')`

生产化建议新增表：
- `Lead`, `Contact`, `Case`, `Contract`, `Quote`, `QuoteLine`, `TaskActivity`, `ApprovalRequest`, `SlaPolicy`, `Notification`

## 5. 实施优先级（建议）
### Phase 1: MVP（2-4周）
- Lead Inbox / Opportunity Board / Opportunity Detail / Case Queue / Approval Inbox
- 目标：销售与客服基本可运转

### Phase 2: 稳定化（4-8周）
- Activity Console / Quote Builder / Forecast Console / SLA Monitor
- 目标：收入管理与服务质量稳定

### Phase 3: 扩展（8周+）
- Campaign Planner / Attribution / Integration Hub / Object Manager
- 目标：自动化与分析增强

## 6. 页面类型设计原则
- `DynamicEntity`：
  - 主数据维护、列表检索、单表编辑
- `Page (YAML)`：
  - 多数据源聚合、业务工作台
- `Custom Razor View`：
  - 高交互场景（审批按钮、拖拽、聚合卡片）

## 7. 多语言策略
- 支持语言：`en-US / zh-CN / ja-JP / ko-KR`
- 规则：
  - 禁止在 `.cshtml` 中写死业务文案
  - 统一走 `config/i18n.yml` key
  - `PageView` 的 `title/description/section/column` 必须可翻译

## 8. 验收标准（Definition of Done）
1. 关键页面可完成检索、筛选、排序
2. 关键操作（assign/approve/stage update）有审计日志
3. 四种语言下无明显未翻译 key、无布局错乱
4. Admin/普通用户权限边界有效
5. `dotnet build` 通过，主流程手工用例通过

## 9. 实施完成检查
1. 必需页面 `pages/*.yaml` 已补齐
2. 关键操作（assign/approve/stage update）可通过 `Page` 流程执行
3. 主要 i18n key（导航、首页、标题/描述、关键列）已落地
4. UAT 场景见 `docs/uat-scenarios-ja.md` 与 `docs/uat-scenarios-zh-CN.md`
