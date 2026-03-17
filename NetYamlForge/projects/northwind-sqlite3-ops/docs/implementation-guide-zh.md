# northwind-sqlite3-ops 实施说明

## 概述
`northwind-sqlite3-ops` 是基于 `northwind-sqlite3` 的派生子项目，复用同一套 SQLite 数据库。

本次实现包含：
- 多个业务场景页面
- 基于 hook 的业务规则
- 多语言标签（ja-JP / en-US / zh-CN）

## 业务场景
1. 履约优先队列（按延迟天数和收入排序）
2. 补货计划（结合未完成订单需求）
3. 客户风险雷达（按延迟占比识别风险）

## Hook 场景
- `nw_order_date_guard`：校验订单日期、要求日期和运费范围
- `nw_order_status_transition`：禁止已取消订单回滚到其他状态，并写入审计日志
- `nw_orderdetail_stock_guard`：阻止超过库存的明细写入

## 路由
- `/northwind-sqlite3-ops/Dashboard`
- `/northwind-sqlite3-ops/DynamicEntity/Index?entity=order`
- `/northwind-sqlite3-ops/DynamicEntity/Index?entity=orderdetail`
- `/northwind-sqlite3-ops/Page/FulfillmentQueue`
- `/northwind-sqlite3-ops/Page/ReplenishmentPlan`
- `/northwind-sqlite3-ops/Page/CustomerRiskRadar`
