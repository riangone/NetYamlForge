# northwind-sqlite3-ops Implementation Guide

## Overview
`northwind-sqlite3-ops` is a derived subproject based on `northwind-sqlite3`, reusing the same SQLite database.

Key deliverables:
- Multi-business operational pages
- Hook-based business validation
- Multi-language labels (ja-JP / en-US / zh-CN)

## Business Scenarios
1. Fulfillment queue prioritization by delay and revenue.
2. Replenishment planning with pending demand.
3. Customer risk radar based on delayed-order ratio.

## Hook Scenarios
- `nw_order_date_guard`: validates order/required date and freight range.
- `nw_order_status_transition`: blocks invalid status rollback from `Cancelled`; writes hook audit entries.
- `nw_orderdetail_stock_guard`: blocks order details exceeding product stock.

## Routes
- `/northwind-sqlite3-ops/Dashboard`
- `/northwind-sqlite3-ops/DynamicEntity/Index?entity=order`
- `/northwind-sqlite3-ops/DynamicEntity/Index?entity=orderdetail`
- `/northwind-sqlite3-ops/Page/FulfillmentQueue`
- `/northwind-sqlite3-ops/Page/ReplenishmentPlan`
- `/northwind-sqlite3-ops/Page/CustomerRiskRadar`
