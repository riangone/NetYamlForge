---
title: "JPiere 購買スキル"
version: "1.0"
category: "purchase"
created: "2026-04-07"
---

# 📦 JPiere 購買スキル

## スキル概要

購買オーダー・入荷確認・仕入請求・支払処理に関する業務スキル。

## 対象エンティティ

- `purchase_orders` - 購買オーダー
- `purchase_order_lines` - 購買明細
- `purchase_receipts` - 入荷確認
- `purchase_receipt_lines` - 入荷明細
- `ap_invoices` - 仕入請求
- `payments` - 支払
- `stock_moves` - 在庫移動
- `business_partners` - 仕入先

## 主要操作

### 1. 購買オーダー照会

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "purchase_orders",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "CO"},
    {"field": "order_date", "operator": ">=", "value": "2026-04-01"}
  ],
  "orderBy": {"field": "order_date", "direction": "desc"},
  "top": 20
}
```

### 2. 未入荷チェック

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "purchase_orders",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "CO"}
  ],
  "select": ["id", "po_no", "partner_id", "total_amt", "delivery_date"]
}
```
→ purchase_receiptsと突合して未入荷の購買を特定

### 3. 購買統計

**分析項目**:
- 月別購買件数・合計金額
- 仕入先別購買金額
- 未入荷オーダー状況
- 在庫回転率

### 4. 仕入請求状況

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "ap_invoices",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "CO"},
    {"field": "outstanding_amt", "operator": ">", "value": 0}
  ],
  "orderBy": {"field": "due_date", "direction": "asc"},
  "top": 20
}
```

## 業務ルール

### 購買フロー

```
発注書作成 → [金額>=10万: 承認必要] → 仕入先へ発注
    ↓
入荷確認 → 在庫入库 + 在庫移動記録
    ↓
仕入請求書 → 確定 → 自動仕訳: 仕入高(DR)/買掛金(CR)
    ↓
支払処理 → 自動仕訳: 買掛金(DR)/現金預金(CR)
```

### 承認フロー

- 購買金額 >= 100,000 の場合、自動承認必要
- 承認ステータス: `DR` → `AP` (承認中) → `CO` (確定)

### 入荷処理

- 入荷数量 = 注文数量 (一致確認)
- 差異がある場合は差異記録
- 入库后: 库存数量增加 + 库存移动记录

### 仕入請求確定時自動仕訳

```
借方: 仕入高 (5100)
    消費税 (1100)
貸方: 買掛金 (2100)
```

## 推奨アクション

1. 未入荷購買オーダーの確認
2. 仕入先との価格交渉（長期在庫品目）
3. 支払期日が近づいた仕入請求の確認
4. 月次購買レポートの作成

---

*最終更新：2026年4月7日*
