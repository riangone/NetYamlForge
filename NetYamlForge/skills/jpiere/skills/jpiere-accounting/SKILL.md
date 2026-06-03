---
title: "JPiere 会計スキル"
version: "1.0"
category: "accounting"
created: "2026-04-07"
---

# 📊 JPiere 会計スキル

## スキル概要

仕訳・総勘目元帳・月次決算・資金管理に関する業務スキル。

## 対象エンティティ

- `journals` - 仕訳ヘッダ
- `journal_lines` - 仕訳明細
- `accounts` - 勘定科目マスタ
- `bills` - 請求（仕訳連携）
- `payments` - 入金/支払
- `recognitions` - 売上認識

## 主要操作

### 1. 仕訳照会

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "journals",
  "action": "list",
  "filters": [
    {"field": "journal_date", "operator": ">=", "value": "2026-04-01"},
    {"field": "journal_date", "operator": "<=", "value": "2026-04-30"},
    {"field": "status", "operator": "=", "value": "CO"}
  ],
  "orderBy": {"field": "journal_no", "direction": "asc"},
  "top": 50
}
```

### 2. 科目残高照会

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "accounts",
  "action": "list",
  "filters": [],
  "select": ["account_code", "account_name", "account_type"],
  "top": 100
}
```

### 3. 会計統計

**分析項目**:
- 月別仕訳件数・借方/貸方合計
- 科目別発生額
- 損益試算（収益-費用）
- 資金繰り状況

### 4. 貸借不一致チェック

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "journals",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "DR"}
  ],
  "select": ["id", "journal_no", "total_dr", "total_cr", "description"]
}
```
→ `ABS(total_dr - total_cr) > 0.01` の仕訳を抽出

## 業務ルール

### 勘定科目体系

```
資産 (A): 1xxx - 現金、売掛金、棚卸資産など
負債 (L): 2xxx - 買掛金、借入金など
資本 (E): 3xxx - 資本金、利益剰余金など
収益 (R): 4xxx - 売上高、営業外収益など
費用 (X): 5xxx - 仕入高、販売費、一般管理費など
```

### 主要仕訳パターン

**売上計上**:
```
借方: 売掛金 (1100)
貸方: 売上高 (4100)
    消費税 (2100)
```

**入金処理**:
```
借方: 現金預金 (1000)
貸方: 売掛金 (1100)
```

**仕入計上**:
```
借方: 仕入高 (5100)
    消費税 (1100)
貸方: 買掛金 (2100)
```

**支払処理**:
```
借方: 買掛金 (2100)
貸方: 現金預金 (1000)
```

### 月次締め

- 月次で収益・費用を集計し、損益計算
- 翌月繰越処理
- 試算平衡表作成

## 推奨アクション

1. 貸借不一致仕訳の修正
2. 月次損益レポートの作成
3. 未処理仕訳の確認
4. 資金繰り予測の更新

---

*最終更新：2026年4月7日*
