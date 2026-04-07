# JPiere エンティティ定義リファレンス

> **バージョン**: 1.0  
> **作成日**: 2026-04-07  
> **プロジェクト**: JPiere 契約サービス

---

## 目次

1. [主データ](#1-主データ)
2. [契約管理](#2-契約管理)
3. [見積管理](#3-見積管理)
4. [請求管理](#4-請求管理)
5. [会計](#5-会計)
6. [購買](#6-購買)
7. [承認](#7-承認)
8. [TODO](#8-todo)
9. [AI コア](#9-ai-コア)

---

## 1. 主データ

### business_partners - 取引先マスタ

**説明**: 顧客・仕入先・取引先の基本情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| partner_code | string | 取引先コード |
| partner_name | string | 取引先名 |
| partner_type | string | 顧客/仕入先/両方 |
| postal_code | string | 郵便番号 |
| address | string | 住所 |
| phone | string | 電話番号 |
| email | string | メールアドレス |
| status | string | 有効/無効 |

**使用例**:
```json
{"tool_call": "query_data", "entity": "business_partners", "action": "list", "top": 20}
```

---

### products - 商品マスタ

**説明**: 販売・購入商品の情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| product_code | string | 商品コード |
| product_name | string | 商品名 |
| category_id | int | カテゴリ ID (FK) |
| unit_price | decimal | 単価 |
| cost_price | decimal | 原価 |
| tax_rate | decimal | 消費税率 |
| status | string | 有効/無効 |

---

## 2. 契約管理

### contracts - 契約ヘッダ

**説明**: 契約の基本情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| contract_no | string | 契約番号 (CON-YYYYMM-XXXX) |
| title | string | 契約タイトル |
| partner_id | int | 取引先 ID (FK) |
| category_id | int | 契約カテゴリ (FK) |
| template_id | int | 契約テンプレート (FK) |
| status | string | DR(下書)/IN(承認中)/CO(確定)/CL(クローズ) |
| total_doc_amt | decimal | 契約合計金額 |
| start_date | date | 開始日 |
| end_date | date | 終了日 |
| created_by | string | 作成者 |
| created_at | datetime | 作成日時 |

**ステータス遷移**:
```
DR (下書) → IN (承認中) → CO (確定) → CL (クローズ)
```

**使用例**:
```json
{
  "tool_call": "query_data",
  "entity": "contracts",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "CO"},
    {"field": "end_date", "operator": "<=", "value": "2026-04-30"}
  ],
  "orderBy": {"field": "end_date", "direction": "asc"},
  "top": 10
}
```

---

### contract_lines - 契約明細

**説明**: 契約の品目明細

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| contract_id | int | 契約ヘッダ ID (FK) |
| line_no | int | 行番号 |
| product_id | int | 商品 ID (FK) |
| description | string | 説明 |
| quantity | decimal | 数量 |
| unit_price | decimal | 単価 |
| line_amt | decimal | 行金額 |

---

## 3. 見積管理

### estimations - 見積ヘッダ

**説明**: 見積書の基本情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| estimation_no | string | 見積番号 (EST-YYYYMM-XXXX) |
| title | string | 見積タイトル |
| partner_id | int | 取引先 ID (FK) |
| status | string | DR(下書)/SN(送付済)/AC(受注)/RJ(拒否) |
| total_doc_amt | decimal | 見積合計金額 |
| valid_until | date | 有効期限 |
| created_by | string | 作成者 |

**使用例**:
```json
{
  "tool_call": "query_data",
  "entity": "estimations",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "SN"},
    {"field": "valid_until", "operator": ">=", "value": "2026-04-01"}
  ]
}
```

---

## 4. 請求管理

### bills - 請求ヘッダ

**説明**: 請求書の基本情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| bill_no | string | 請求番号 (BILL-YYYYMM-XXXX) |
| title | string | 請求タイトル |
| partner_id | int | 取引先 ID (FK) |
| bill_type | string | AR(売上)/AP(仕入) |
| status | string | DR(下書)/CO(確定)/CN(取消)/PA(支払済) |
| grand_total | decimal | 請求合計 |
| outstanding_amt | decimal | 未収残高 |
| due_date | date | 支払期日 |
| linked_contract_id | int | 関連契約 ID (FK) |

**確定時自動仕訳** (AR):
```
借方: 売掛金 (1100)
貸方: 売上高 (4100)
```

**使用例**:
```json
{
  "tool_call": "query_data",
  "entity": "bills",
  "action": "list",
  "filters": [
    {"field": "bill_type", "operator": "=", "value": "AR"},
    {"field": "status", "operator": "=", "value": "CO"},
    {"field": "outstanding_amt", "operator": ">", "value": 0}
  ],
  "orderBy": {"field": "due_date", "direction": "asc"}
}
```

---

## 5. 会計

### journals - 仕訳ヘッダ

**説明**: 会計仕訳の基本情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| journal_no | string | 仕訳番号 (JRN-YYYYMM-XXXX) |
| journal_date | date | 仕訳日 |
| journal_type | string | SALES/ PURCHASE/PAYMENT/RECEIPT/ADJUSTMENT |
| status | string | DR(下書)/CO(確定) |
| description | string | 摘要 |
| total_dr | decimal | 借方合計 |
| total_cr | decimal | 貸方合計 |
| source_entity | string | 参照元エンティティ |
| source_id | int | 参照元 ID |

**バリデーション**: `total_dr == total_cr` (貸借一致必須)

---

### journal_lines - 仕訳明細

**説明**: 仕訳の科目明細

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| journal_id | int | 仕訳ヘッダ ID (FK) |
| line_no | int | 行番号 |
| account_id | int | 勘定科目 ID (FK) |
| dr_amt | decimal | 借方金額 |
| cr_amt | decimal | 貸方金額 |
| description | string | 摘要 |

---

### accounts - 勘定科目マスタ

**説明**: 会計科目の基本情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| account_code | string | 科目コード |
| account_name | string | 科目名 |
| account_type | string | A(資産)/L(負債)/E(資本)/R(収益)/X(費用) |
| parent_id | int | 親科目 ID (FK) |

**主要科目**:
- 1100: 売掛金
- 1900: 現金
- 2100: 買掛金
- 4100: 売上高
- 5100: 仕入高

---

## 6. 購買

### purchase_orders - 購買オーダ

**説明**: 発注書の基本情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| po_no | string | 発注番号 (PO-YYYYMM-XXXX) |
| partner_id | int | 仕入先 ID (FK) |
| status | string | DR(下書)/AP(承認中)/CO(確定)/CL(クローズ) |
| total_amt | decimal | 発注合計 |
| order_date | date | 発注日 |
| delivery_date | date | 納期 |

**承認フロー**: 金額 >= 100,000 の場合、自動承認必要

---

### purchase_receipts - 入荷確認

**説明**: 購買入荷の確認情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| receipt_no | string | 入荷番号 (RCV-YYYYMM-XXXX) |
| po_id | int | 発注 ID (FK) |
| receipt_date | date | 入荷日 |
| status | string | DR(下書)/CO(確定) |

**確定時**: 自動在庫入库 + 仕訳起票

---

### ap_invoices - 仕入請求

**説明**: 仕入先からの請求情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| invoice_no | string | 請求番号 (API-YYYYMM-XXXX) |
| po_id | int | 発注 ID (FK) |
| partner_id | int | 仕入先 ID (FK) |
| status | string | DR(下書)/CO(確定)/PA(支払済) |
| invoice_amt | decimal | 請求金額 |
| due_date | date | 支払期日 |

**確定時自動仕訳**:
```
借方: 仕入高 (5100)
貸方: 買掛金 (2100)
```

---

## 7. 承認

### approval_requests - 承認依頼

**説明**: 承認ワークフローの依頼情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| request_no | string | 依頼番号 (APP-YYYYMM-XXXX) |
| title | string | 承認タイトル |
| source_entity | string | 参照元エンティティ |
| source_id | int | 参照元 ID |
| status | string | PENDING/APPROVED/REJECTED |
| priority | string | HIGH/MEDIUM/LOW |
| requested_by | string | 依頼者 |
| requested_at | datetime | 依頼日時 |

**使用例**:
```json
{
  "tool_call": "query_data",
  "entity": "approval_requests",
  "action": "list",
  "filters": [
    {"field": "status", "operator": "=", "value": "PENDING"}
  ],
  "orderBy": {"field": "priority", "direction": "desc"}
}
```

---

## 8. TODO

### todos - TODO/タスク

**説明**: タスク・やること情報

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| title | string | TODO タイトル |
| category_id | int | カテゴリ ID (FK) |
| status | string | OPEN/IN_PROGRESS/DONE/CLOSED |
| priority | string | HIGH/MEDIUM/LOW |
| due_date | date | 期限 |
| assigned_to | string | 担当者 |
| linked_entity | string | 関連エンティティ |
| linked_entity_id | int | 関連エンティティ ID |
| created_by | string | 作成者 |

---

## 9. AI コア

### ai_conversations - AI 会話

**説明**: AI との会話セッション

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| conversation_id | string | 会話 ID |
| channel | string | web/api/mobile |
| status | string | active/completed/escalated/closed |
| user_role | string | employee/contract_manager/accountant/... |
| last_intent | string | 最終意図 |
| sentiment_score | decimal | 感情スコア (-1.0〜+1.0) |
| started_at | datetime | 開始時刻 |
| message_count | int | メッセージ数 |

---

### ai_handovers - AI 引継ぎ

**説明**: AI から担当者への引継ぎ

| フィールド | 型 | 説明 |
|-----------|-----|------|
| id | int | ID (PK) |
| handover_id | string | 引継ぎ ID |
| conversation_id | string | 会話 ID (FK) |
| reason | string | complaint/urgent/complex_query/... |
| priority | string | high/medium/low |
| target_department | string | contract/billing/accounting/... |
| status | string | pending/in_progress/completed/closed |
| assigned_to | string | 担当者 |

---

## クエリ例文集

### 契約関連
```sql
-- 今月の契約件数・合計金額
SELECT COUNT(*), SUM(total_doc_amt) FROM contracts 
WHERE created_at >= '2026-04-01' AND status = 'CO'

-- 有効期限切れ間近の契約（30日以内）
SELECT * FROM contracts 
WHERE end_date BETWEEN '2026-04-01' AND '2026-04-30' 
AND status = 'CO'
ORDER BY end_date ASC
```

### 請求関連
```sql
-- 未収請求一覧
SELECT bill_no, partner_id, outstanding_amt, due_date 
FROM bills 
WHERE outstanding_amt > 0 AND status = 'CO'
ORDER BY due_date ASC
```

### 会計関連
```sql
-- 今月の仕訳件数
SELECT COUNT(*) FROM journals 
WHERE journal_date >= '2026-04-01' AND status = 'CO'

-- 貸借不一致チェック
SELECT j.id, j.journal_no, j.total_dr, j.total_cr
FROM journals j
WHERE ABS(j.total_dr - j.total_cr) > 0.01
```

---

*最終更新：2026年4月7日*
