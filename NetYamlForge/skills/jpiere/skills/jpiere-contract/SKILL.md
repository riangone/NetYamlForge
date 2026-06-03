---
title: "JPiere 契約管理スキル"
version: "1.0"
category: "contract"
created: "2026-04-07"
---

# 📝 JPiere 契約管理スキル

## スキル概要

契約の作成・更新・分析・期限管理に関する業務スキル。

## 対象エンティティ

- `contracts` - 契約ヘッダ
- `contract_lines` - 契約明細
- `contract_categories` - 契約カテゴリ
- `contract_templates` - 契約テンプレート

## 主要操作

### 1. 契約照会

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "contracts",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "CO"},
    {"field": "created_at", "operator": ">=", "value": "2026-04-01"}
  ],
  "orderBy": {"field": "total_doc_amt", "direction": "desc"},
  "top": 20
}
```

### 2. 有効期限チェック

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "contracts",
  "action": "list",
  "filters": [
    {"field": "end_date", "operator": "<=", "value": "2026-04-30"},
    {"field": "status", "operator": "=", "value": "CO"}
  ],
  "orderBy": {"field": "end_date", "direction": "asc"},
  "top": 10
}
```

### 3. 契約統計

**分析項目**:
- 月別契約件数
- 月別合計金額
- カテゴリ別契約状況
- 顧客別契約金額

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
→ bills テーブルと突合して未請求の契約を特定

## 業務ルール

### ステータス遷移

```
DR (下書) → IN (承認中) → CO (確定) → CL (クローズ)
```

### 金額計算

- 契約合計金額 = `SUM(contract_lines.line_amt)`
- 税込み・税別を明確に区別

### 期限管理

- 30日以内: 注意レベル
- 7日以内: 警告レベル
- 当日: 緊急レベル

## 推奨アクション

1. 有効期限が近づいた契約の更新確認
2. 未請求の契約に対する請求書作成
3. 月次契約レポートの作成
4. 顧客別契約状況の分析とフォローアップ

---

*最終更新：2026年4月7日*
