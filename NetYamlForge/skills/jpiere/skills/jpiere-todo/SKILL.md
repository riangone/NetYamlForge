---
title: "JPiere TODO管理スキル"
version: "1.0"
category: "todo"
created: "2026-04-07"
---

# ✅ JPiere TODO管理スキル

## スキル概要

TODO/タスクの作成・更新・完了管理に関する業務スキル。

## 対象エンティティ

- `todos` - TODO/タスク
- `todo_categories` - TODOカテゴリ
- `contracts` - 関連契約
- `bills` - 関連請求
- `estimations` - 関連見積
- `users` - 担当者

## 主要操作

### 1. TODO照会

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "todos",
  "action": "list",
  "filters": [
    {"field": "assigned_to", "operator": "=", "value": "ユーザーID"},
    {"field": "status", "operator": "!=", "value": "DONE"}
  ],
  "orderBy": {"field": "due_date", "direction": "asc"},
  "top": 20
}
```

### 2. 期限切れTODOチェック

**クエリ例**:
```json
{
  "tool_call": "query_data",
  "entity": "todos",
  "action": "list",
  "filters": [
    {"field": "due_date", "operator": "<", "value": "現在日付"},
    {"field": "status", "operator": "!=", "value": "DONE"}
  ],
  "orderBy": {"field": "due_date", "direction": "asc"},
  "top": 20
}
```

### 3. TODO統計

**分析項目**:
- 担当者別TODO件数
- カテゴリ別TODO状況
- 期限達成率
- 平均完了時間

### 4. TODO作成・更新

**作成例**:
```json
{
  "tool_call": "create_record",
  "entity": "todos",
  "data": {
    "title": "契約更新確認",
    "category_id": 1,
    "status": "OPEN",
    "priority": "HIGH",
    "due_date": "2026-04-15",
    "assigned_to": "ユーザーID",
    "linked_entity": "contracts",
    "linked_entity_id": 123
  }
}
```

**更新例**:
```json
{
  "tool_call": "update_record",
  "entity": "todos",
  "id": "TODO ID",
  "data": {
    "status": "IN_PROGRESS" | "DONE" | "CLOSED"
  }
}
```

## 業務ルール

### TODOステータス遷移

```
OPEN → IN_PROGRESS → DONE
                    → CLOSED
```

### TODO優先度

- **HIGH (高)**: 当日または翌日対応必要
- **MEDIUM (中)**: 1週間以内に対応必要
- **LOW (低)**: 月内に対応必要

### カテゴリ例

- 契約更新
- 請求確認
- 支払い処理
- 承認依頼
- 顧客フォロー
- その他

### 自動TODO作成

- AI会話から自動生成（有効期限切れ契約、未請求など）
- バッチジョブから自動生成（契約到期提醒、未収催促）
- 手動作成

## 推奨アクション

1. 期限切れTODOの確認と対応
2. 本日のTODO確認（優先度高→低）
3. 長期未対応TODOの処理
4. 完了TODOの振り返り

---

*最終更新：2026年4月7日*
