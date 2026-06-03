---
title: "JPiere 請求管理スキル"
version: "1.0"
category: "billing"
created: "2026-04-07"
---

# 💰 JPiere 請求管理スキル

## スキル概要

請求書の作成・確定・入金管理・未収追跡に関する業務スキル。

## 対象エンティティ

- `bills` - 請求ヘッダ
- `bill_lines` - 請求明細
- `contracts` - 契約（請求連携）
- `recognitions` - 売上認識
- `journals` - 仕訳（確定時自動起票）
- `business_partners` - 取引先

## 主要操作

### 1. 請求照会

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "bills",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "CO"},
    {"field": "bill_type", "operator": "=", "value": "AR"}
  ],
  "orderBy": {"field": "due_date", "direction": "asc"},
  "top": 20
}
```

### 2. 未収請求チェック

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "bills",
  "action": "list",
  "filters": [
    {"field": "outstanding_amt", "operator": ">", "value": 0},
    {"field": "status", "operator": "=", "value": "CO"}
  ],
  "orderBy": {"field": "due_date", "direction": "asc"},
  "top": 20
}
```

### 3. 請求統計

**分析項目**:
- 月別請求件数・合計金額
- 未収残高合計
- 支払期日overdue件数
- 顧客別請求金額

### 4. 未請求契約検出

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "contracts",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "CO"}
  ],
  "select": ["id", "contract_no", "title", "total_doc_amt", "partner_id"]
}
```
→ billsテーブルと突合して未請求の契約を特定

## 業務ルール

### ステータス遷移

```
DR (下書) → CO (確定) → PA (支払済)
            ↓
          CN (取消)
```

### 確定時自動仕訳

**AR (売上請求)**:
```
借方: 売掛金 (1100)
貸方: 売上高 (4100)
    消費税 (2100)
```

**AP (仕入請求)**:
```
借方: 仕入高 (5100)
    消費税 (1100)
貸方: 買掛金 (2100)
```

### 未収管理

- 未収残高 = `grand_total - pay_amt`
- 支払期日 overdue の場合は自動的に督促を推奨
- 督促ステータス: なし → 1次督促 → 2次督促 → 最終督促

## 推奨アクション

1. 支払期日が近づいた請求の確認
2. 未収請求に対する督促状作成
3. 月次請求レポートの作成
4. 顧客別請求状況の分析と回収計画

---

*最終更新：2026年4月7日*
