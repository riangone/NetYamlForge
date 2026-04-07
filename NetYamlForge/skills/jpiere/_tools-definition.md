# JPiere AI ツール定義

> **バージョン**: 1.0  
> **作成日**: 2026-04-07  
> **プロジェクト**: JPiere 契約サービス

---

## 使用可能ツール一覧

### 1. query_data - データクエリ

**説明**: データベースから安全にデータを取得する

**パラメータ**:
```json
{
  "tool_call": "query_data",
  "entity": "エンティティ名 (必須)",
  "action": "list | count | detail (デフォルト: list)",
  "filters": [
    {
      "field": "フィールド名",
      "operator": "= | != | > | < | >= | <= | LIKE | IN | BETWEEN",
      "value": "値"
    }
  ],
  "orderBy": {
    "field": "ソートフィールド",
    "direction": "asc | desc"
  },
  "top": 取得件数 (デフォルト: 20),
  "select": ["選択フィールド配列"]
}
```

**使用例**:
```json
// 契約一覧取得（今月・ステータスINのもの）
{
  "tool_call": "query_data",
  "entity": "contracts",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "IN"},
    {"field": "created_at", "operator": ">=", "value": "2026-04-01"}
  ],
  "orderBy": {"field": "total_doc_amt", "direction": "desc"},
  "top": 10
}

// 仕訳の月次合計
{
  "tool_call": "query_data",
  "entity": "journals",
  "action": "count",
  "filters": [
    {"field": "journal_date", "operator": ">=", "value": "2026-04-01"},
    {"field": "journal_date", "operator": "<=", "value": "2026-04-30"}
  ]
}
```

---

### 2. create_record - レコード作成

**説明**: 新しいレコードを作成する（権限が必要）

**パラメータ**:
```json
{
  "tool_call": "create_record",
  "entity": "エンティティ名 (必須)",
  "data": {
    "field1": "value1",
    "field2": "value2"
  }
}
```

**権限**:
- employee: ❌ 不可
- contract_manager: ✅ contracts, estimations, bills, todos
- accountant: ✅ journals, payments, recognitions
- purchaser: ✅ purchase_orders, purchase_receipts, ap_invoices
- approver: ✅ approval_requests
- admin: ✅ 全部

---

### 3. update_record - レコード更新

**説明**: 既存レコードを更新する（権限が必要）

**パラメータ**:
```json
{
  "tool_call": "update_record",
  "entity": "エンティティ名 (必須)",
  "id": "レコードID (必須)",
  "data": {
    "field1": "new_value1",
    "field2": "new_value2"
  }
}
```

**権限**: create_record に準ずる

---

### 4. approve_record - 承認

**説明**: 承認ワークフローの承認/却下を行う

**パラメータ**:
```json
{
  "tool_call": "approve_record",
  "entity": "approval_requests | purchase_orders | contracts",
  "id": "レコードID (必須)",
  "action": "approve | reject (必須)",
  "comment": "承認コメント (必須)"
}
```

**権限**:
- approver: ✅ 全部
- admin: ✅ 全部
- その他: ❌ 不可

---

## ツール呼び出しルール

1. **安全性**: query_data は SELECT のみ可能、INSERT/UPDATE/DELETE は禁止
2. **権限**: 各ツールは役割権限に従って実行可否を判断
3. **監査**: 全ツール実行は ai_messages に記録
4. **制限**: 1回の会話で最大10回までツール呼び出し可能
5. **エラー処理**: エラー発生時はユーザーに明確に報告

---

*最終更新：2026年4月7日*
