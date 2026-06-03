---
title: "JPiere 承認スキル"
version: "1.0"
category: "approval"
created: "2026-04-07"
---

# ✅ JPiere 承認スキル

## スキル概要

承認ワークフローの確認・承認・却下・進捗管理に関する業務スキル。

## 対象エンティティ

- `approval_requests` - 承認依頼
- `approval_steps` - 承認ステップ
- `purchase_orders` - 購買オーダー（承認対象）
- `contracts` - 契約（承認対象）
- `todos` - 関連TODO

## 主要操作

### 1. 承認依頼照会

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "approval_requests",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "PENDING"}
  ],
  "orderBy": {"field": "priority", "direction": "desc"},
  "top": 20
}
```

### 2. 承認待ちチェック

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "approval_requests",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "PENDING"},
    {"field": "assigned_to", "operator": "=", "value": "ユーザーID"}
  ],
  "orderBy": {"field": "requested_at", "direction": "asc"},
  "top": 20
}
```

### 3. 承認統計

**分析項目**:
- 承認・却下件数
- 平均承認時間
- 部門別承認状況
- 承認率・却下率

### 4. 承認・却下処理

**クエリ例**:
```json
{
  "tool_call": "approve_record",
  "entity": "approval_requests",
  "id": "承認依頼ID",
  "action": "approve | reject",
  "comment": "承認コメント"
}
```

## 業務ルール

### 承認ステータス遷移

```
PENDING → APPROVED (承認)
        → REJECTED (却下)
```

### 承認フロー

1. 承認依頼作成 → 承認者に割り当て
2. 承認者が確認 → 承認/却下
3. 承認時: 対象エンティティのステータスを更新
4. 却下時: 理由必須、依頼者に通知

### 多级承認

- 複数段階承認可能（例: 課長 → 部長 → 社長）
- 各ステップで承認/却下
- 却下時は最初のステップに戻る

### 自動承認

- 金額 < 100,000 の購買は自動承認
- 一定額以下は承認省略

## 推奨アクション

1. 承認待ち案件の確認（優先度順）
2. 長期保留中の承認案件の確認
3. 却下案件の再提出
4. 月次承認レポートの確認

---

*最終更新：2026年4月7日*
