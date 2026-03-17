# salesforce-crm UAT 场景（中文）

## 1. 目标
验证 Salesforce 风格 CRM 页面在销售、客服、管理三条主线中的可用性与一致性。

## 2. 前置条件
- 入口: `/salesforce-crm`
- 角色: `Admin` 与 `普通用户`
- 语言: `en-US / zh-CN / ja-JP / ko-KR`

## 3. 销售场景
### SC-01 线索到商机
1. 打开 `Lead Inbox`
2. 对任意线索执行分配
3. 在 `Lead Detail 360` 查看活动
4. 在 `Opportunity Detail` 更新阶段

期望结果:
- 状态更新成功并即时展示
- `Audit Trail` 可见相关操作记录

### SC-02 报价到回款
1. 打开 `Quote Builder`
2. 查看报价候选
3. 在 `Order Management` 查看订单状态
4. 在 `Invoice & Payment` 查看开票/回款状态

期望结果:
- 金额与状态展示一致，无明显断链

## 4. 客服场景
### SV-01 工单升级与解决
1. 打开 `Case Queue`
2. 执行 `Escalate` / `Resolve`
3. 在 `Case Detail` 查看最新状态与活动
4. 在 `SLA Monitor` 查看预警

期望结果:
- 工单状态成功变化
- `Case Detail` 活动区可见审计事件

### SV-02 知识与全渠道
1. 打开 `Knowledge Base`
2. 打开 `Omni-Channel Console`

期望结果:
- 列表可正常展示
- 筛选和排序可用

## 5. 管理场景
### AD-01 审批流
1. 打开 `Approval Inbox`
2. 执行 `Approve` 或 `Reject`
3. 在 `Audit Trail` 验证记录

期望结果:
- 审批状态更新成功
- 审计日志完整可追溯

### AD-02 用户权限管理
1. 打开 `User Role Profile`
2. 对用户执行 `Enable/Disable`、`Grant/Remove Admin`
3. 在 `Role Access Matrix` 验证变更

期望结果:
- 角色/状态变化生效
- 变更写入 `AuditLog`

## 6. 多语言场景
### I18N-01 韩语验证
1. 切换到 `ko-KR`
2. 打开 `Executive Cockpit`、`Case Detail`、`User Role Profile`

期望结果:
- 标题、列名、按钮均韩语化
- 不出现 `MissingManifestResourceException`

## 7. 验收标准
- 全场景无 500 错误
- 关键操作可在审计日志中追踪
- 4 种语言下不出现原始 i18n key 泄漏
