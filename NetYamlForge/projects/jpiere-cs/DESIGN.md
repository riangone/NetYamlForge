# JPiere Contract Service (JPCS) — 詳細設計ドキュメント

> バージョン: 2.0 | 作成日: 2026-04-06 | ステータス: 設計中

---

## 目次

1. [現状分析 (AS-IS)](#1-現状分析-as-is)
2. [目標アーキテクチャ (TO-BE)](#2-目標アーキテクチャ-to-be)
3. [モジュール構成](#3-モジュール構成)
4. [エンティティ関連図 (ERD)](#4-エンティティ関連図-erd)
5. [ドキュメントステータス遷移](#5-ドキュメントステータス遷移)
6. [フェーズ1: 会計基盤](#6-フェーズ1-会計基盤)
7. [フェーズ2: 購買フロー](#7-フェーズ2-購買フロー)
8. [フェーズ3: 承認ワークフロー](#8-フェーズ3-承認ワークフロー)
9. [Hook実装仕様](#9-hook実装仕様)
10. [バッチジョブ拡張計画](#10-バッチジョブ拡張計画)
11. [実装ロードマップ](#11-実装ロードマップ)

---

## 1. 現状分析 (AS-IS)

### 1.1 実装済みエンティティ

| テーブル | 役割 | 状態 |
|---|---|---|
| `business_partners` | 取引先マスタ（顧客/仕入先/両方） | 完成 |
| `product_categories` | 商品カテゴリマスタ | 完成 |
| `products` | 商品マスタ（価格・税率含む） | 完成 |
| `contract_categories` | 契約カテゴリマスタ | 完成 |
| `contract_templates` | 契約テンプレート | 完成 |
| `contracts` | 契約ヘッダ | 完成 |
| `contract_lines` | 契約明細 | 完成 |
| `estimations` | 見積ヘッダ | 完成 |
| `estimation_lines` | 見積明細 | 完成 |
| `bills` | 請求ヘッダ | 完成 |
| `bill_lines` | 請求明細 | 完成 |
| `recognitions` | 売上認識ヘッダ | 完成 |
| `recognition_lines` | 売上認識明細 | 完成 |
| `todo_categories` | TODOカテゴリ | 完成 |
| `todos` | TODO/タスク管理 | 完成 |

### 1.2 実装済みHook

| Hook名 | クラス | 機能 |
|---|---|---|
| `bill_document_no` | `BillDocumentNoHook` | 請求書番号の自動採番 `BILL-YYYYMM-XXXX` |
| `bill_due_date` | `BillDueDateHook` | 支払期日の自動計算（請求日 + 支払条件日数） |
| `bill_outstanding` | `BillOutstandingHook` | 未収残高 = `grand_total - pay_amt` の自動計算 |
| `contract_document_no` | `ContractDocumentNoHook` | 契約番号の自動採番 `CON-YYYYMM-XXXX` |
| `contract_amount_calculate` | `ContractAmountCalculateHook` | 契約明細合計から `total_doc_amt` を再計算 |
| `contract_status` | `ContractStatusHook` | ステータス遷移の許可チェック |
| `contract_expiry_check` | `ContractExpiryCheckHook` | 期限切れ契約への自動ステータス変更 |

### 1.3 現状の限界

```
現状: データを保存するだけのCRUDシステム
目標: 業務処理フローを持つ会計連携型ERPサブセット
```

欠けている機能:

- ドキュメント確定（Complete）処理の実質的なロジック
- 会計基盤（勘定科目・仕訳）の完全欠如
- 購買フロー（発注 → 受入 → AP請求 → 支払）
- 在庫数量管理（在庫移動・倉庫連携）
- 承認ワークフロー（多段階承認）
- 売上認識の確定時の仕訳起票

---

## 2. 目標アーキテクチャ (TO-BE)

### 2.1 全体フロー

```
【売上フロー】
見積(Estimation) --確定--> 受注(SalesOrder) --出荷--> 出荷(Shipment)
                                  |                        |
                            売上認識(Recognition)     在庫出庫(StockMove)
                                  |
                            請求(Bill) --確定--> 仕訳起票(JournalEntry)
                                  |                 売掛金(DR) / 売上(CR)
                            入金(Payment) --> 仕訳起票
                                              銀行(DR) / 売掛金(CR)

【購買フロー】
発注書(PurchaseOrder) --受入--> 受入処理(PurchaseReceipt)
                                  |
                            在庫入庫(StockMove)
                                  |
                           仕入請求書(APInvoice) --確定--> 仕訳起票
                                  |                 仕入(DR) / 買掛金(CR)
                            支払(Payment) --> 仕訳起票
                                              買掛金(DR) / 銀行(CR)
```

### 2.2 契約管理との接続

```
契約(Contract) --月次バッチ--> 請求自動生成(Bill)
      |                               |
      +--売上認識スケジュール--> Recognition --> 仕訳起票
```

---

## 3. モジュール構成

```
projects/jpiere-cs/
├── entities/
│   ├── [既存15テーブル]
│   ├── [P1追加] account.yml          -- 勘定科目マスタ
│   ├── [P1追加] journal.yml          -- 仕訳ヘッダ
│   ├── [P1追加] journal_line.yml     -- 仕訳明細
│   ├── [P2追加] purchase_order.yml   -- 発注書ヘッダ
│   ├── [P2追加] purchase_order_line.yml
│   ├── [P2追加] purchase_receipt.yml -- 受入処理ヘッダ
│   ├── [P2追加] purchase_receipt_line.yml
│   ├── [P2追加] ap_invoice.yml       -- AP請求書（仕入）
│   ├── [P2追加] ap_invoice_line.yml
│   ├── [P2追加] payment.yml          -- 入金/支払処理
│   ├── [P2追加] stock_move.yml       -- 在庫移動
│   └── [P3追加] approval_request.yml -- 承認申請
│
├── Hooks/
│   ├── [既存] BillingHooks.cs
│   ├── [既存] ContractHooks.cs
│   ├── [既存] EstimationHooks.cs
│   ├── [P1追加] AccountingHooks.cs   -- 仕訳起票Hook群
│   ├── [P2追加] PurchaseHooks.cs     -- 購買フローHook群
│   └── [P3追加] ApprovalHooks.cs     -- 承認ワークフローHook
│
├── jobs/
│   ├── [既存] contract_expiry_alert.yml
│   ├── [既存] monthly_billing.yml
│   ├── [P1追加] journal_close.yml    -- 期末仕訳締め
│   └── [P2追加] payment_reminder.yml -- 支払督促
│
└── pages/
    ├── [既存4ページ]
    ├── [P1追加] AccountBalance.yaml  -- 残高照会ページ
    ├── [P1追加] TrialBalance.yaml    -- 試算表
    └── [P2追加] CashFlow.yaml        -- 資金繰り照会
```

---

## 4. エンティティ関連図 (ERD)

### 4.1 現状スキーマ（Phase 0）

```
business_partners -----------------------------------------------+
       |                                                         |
       +--1:N-- contracts ----1:N---- contract_lines            |
       |              |                      |                  |
       |              |               product_id -> products    |
       |              |                                         |
       +--1:N-- estimations --1:N---- estimation_lines          |
       |                                     |                  |
       +--1:N-- bills ---------1:N---- bill_lines               |
       |                                                        |
       +--1:N-- recognitions --1:N---- recognition_lines        |
                                             |                  |
product_categories --1:N-- products ---------+                  |
contract_categories --1:N-- contract_templates                  |
todo_categories --1:N-- todos ----------------------------------+
```

### 4.2 Phase 1 追加（会計基盤）

```
新規テーブル:

accounts (勘定科目マスタ)
  id PK
  code          UNIQUE  (例: 1100, 4100)
  name          NOT NULL (例: 売掛金, 売上高)
  account_type  TEXT  (A=資産, L=負債, E=純資産, R=収益, X=費用)
  normal_balance TEXT  (D=借方, C=貸方)
  is_active

journals (仕訳ヘッダ)
  id PK
  document_no   UNIQUE
  doc_status    TEXT  (DR=下書き, CO=確定, VO=取消)
  journal_type  TEXT  (AR=売掛, AP=買掛, GL=総勘定元帳)
  date_acct     DATE NOT NULL
  description   TEXT
  source_table  TEXT  (bills, payments, etc.)
  source_id     INT
  total_debit   REAL
  total_credit  REAL
  is_balanced   INT   (借方=貸方の場合1)
  is_active

journal_lines (仕訳明細)
  id PK
  journal_id    FK -> journals
  line_no       INT
  account_id    FK -> accounts
  debit_amt     REAL DEFAULT 0
  credit_amt    REAL DEFAULT 0
  description   TEXT
  is_active

接続:
bills --(AfterConfirm Hook)--> journals --1:N-- journal_lines
                                                       |
                                               account_id -> accounts
payments --(AfterConfirm Hook)--> journals
recognitions --(AfterConfirm Hook)--> journals
```

### 4.3 Phase 2 追加（購買フロー）

```
新規テーブル:

purchase_orders (発注書ヘッダ)
  id PK
  document_no   UNIQUE  (PO-YYYYMM-XXXX)
  doc_status    TEXT  (DR / IP / CO / CL / VO)
  business_partner_id FK -> business_partners
  date_ordered  DATE NOT NULL
  date_promised DATE
  total_lines   REAL
  grand_total   REAL
  tax_amt       REAL
  approved_by   TEXT
  approved_at   TEXT
  linked_contract_id FK -> contracts

purchase_order_lines (発注明細)
  id PK
  purchase_order_id FK -> purchase_orders
  line_no       INT
  product_id    FK -> products
  qty_ordered   REAL
  unit_price    REAL
  line_amt      REAL
  qty_received  REAL DEFAULT 0   <- 受入済数量（受入後更新）

purchase_receipts (受入処理ヘッダ)
  id PK
  document_no   UNIQUE  (REC-YYYYMM-XXXX)
  doc_status    TEXT
  purchase_order_id FK -> purchase_orders
  date_received DATE NOT NULL
  total_received_amt REAL

purchase_receipt_lines (受入明細)
  id PK
  receipt_id    FK -> purchase_receipts
  po_line_id    FK -> purchase_order_lines
  product_id    FK -> products
  qty_received  REAL
  unit_cost     REAL
  line_amt      REAL

ap_invoices (仕入請求書ヘッダ)
  id PK
  document_no   UNIQUE  (APBILL-YYYYMM-XXXX)
  doc_status    TEXT
  business_partner_id FK -> business_partners
  purchase_order_id FK -> purchase_orders
  date_invoiced DATE
  date_due      DATE
  total_lines   REAL
  grand_total   REAL
  tax_amt       REAL
  pay_amt       REAL
  outstanding_amt REAL

payments (入金/支払処理)
  id PK
  document_no   UNIQUE  (PAY-YYYYMM-XXXX)
  payment_type  TEXT  (AR=入金, AP=支払)
  doc_status    TEXT
  business_partner_id FK -> business_partners
  payment_date  DATE
  payment_method TEXT  (T=銀行振込, C=クレジット, CH=小切手)
  pay_amt       REAL
  bill_id       FK -> bills (AR入金の場合)
  ap_invoice_id FK -> ap_invoices (AP支払の場合)

stock_moves (在庫移動)
  id PK
  move_type     TEXT  (IN=入庫, OUT=出庫, TF=転送)
  product_id    FK -> products
  qty           REAL
  unit_cost     REAL
  date_moved    DATE
  source_table  TEXT  (purchase_receipts, shipments, etc.)
  source_id     INT

接続:
purchase_orders --1:N-- purchase_order_lines
purchase_orders --1:N-- purchase_receipts --1:N-- purchase_receipt_lines
purchase_orders --1:N-- ap_invoices
purchase_receipts --(AfterConfirm)--> stock_moves (在庫増加)
ap_invoices --(AfterConfirm)--> journals (仕入(DR)/買掛金(CR))
payments --(AfterConfirm)--> journals (買掛金(DR)/銀行(CR))
```

### 4.4 Phase 3 追加（承認ワークフロー）

```
approval_requests (承認申請)
  id PK
  source_table  TEXT  (purchase_orders, contracts, etc.)
  source_id     INT
  requester     TEXT
  current_step  INT DEFAULT 1
  total_steps   INT DEFAULT 1
  status        TEXT  (PENDING / APPROVED / REJECTED / CANCELLED)
  created_at    TEXT

approval_steps (承認ステップ)
  id PK
  request_id    FK -> approval_requests
  step_no       INT
  approver      TEXT
  status        TEXT  (PENDING / APPROVED / REJECTED)
  approved_at   TEXT
  comments      TEXT
```

---

## 5. ドキュメントステータス遷移

### 5.1 共通ステータス値

| コード | 意味 | iDempiere対応 |
|---|---|---|
| `DR` | Draft（下書き） | Drafted |
| `IN` | In Progress（処理中/交渉中） | In Progress |
| `CO` | Completed（確定済み） | Completed |
| `CL` | Closed（完了/解約） | Closed |
| `VO` | Voided（取消/無効） | Voided |
| `RE` | Reversed（反転仕訳済み） | Reversed |

### 5.2 見積（Estimation）の状態遷移

```
DR (下書き)
    |
    +--[送付]--> IN (提出中)
    |               |
    |          +----+-----+
    |          |           |
    |       [受注]      [失注/却下]
    |          |           |
    |          v           v
    |        CO (受注確定)  VO (無効)
    |          |
    |       [契約化]
    |          v
    |        CL (完了)
    |
    +--[直接無効]--> VO (無効)
```

### 5.3 請求書（Bill）の状態遷移

```
DR (下書き)
    |
    +--[確定]--> CO (確定済み)   <- この時点で仕訳が自動起票される
    |               |
    |          +----+----------+
    |          |               |
    |       [入金完了]       [取消]
    |          |               |
    |          v               v
    |        CL (入金済み)     RE (取消・仕訳反転)
    |
    +--[直接取消]--> VO (無効)

仕訳起票内容 (CO確定時):
  借方: 売掛金 1100     grand_total
  貸方: 売上高 4100     tax_base_amt
  貸方: 仮受消費税 2400  tax_amt
```

### 5.4 契約（Contract）の状態遷移

```
contract_status 値:
  WP = Work in Progress (交渉中)
  AC = Active (有効)
  EX = Expired (期限切れ)
  CL = Cancelled (解約)
  CA = Cancelled Archive (解約済アーカイブ)

doc_status 値:
  DR = 下書き
  IN = 交渉中
  CO = 確定/締結
  CL = 終了
  VO = 無効

遷移図 (doc_status):
DR/WP --[交渉開始]--> IN/WP --[契約締結]--> CO/AC
                         |                      |
                         |               [自動/期限切れ]
                         |                      |
                      [交渉失敗]                 v
                         |                  CO/EX
                         v                      |
                       VO/WP             [解約処理]
                                               |
                                               v
                                         CL/CL --[アーカイブ]--> CL/CA
```

### 5.5 発注書（PurchaseOrder）の状態遷移（Phase 2）

```
DR (下書き)
    |
    +--[承認申請]--> [Approval Workflow]
    |                       |
    |                  [承認済み]
    |                       |
    |                       v
    |                 IP (発注済み/受入待ち)
    |                       |
    |                  [全量受入完了]
    |                       |
    |                       v
    |                 CO (完了)
    |
    +--[却下/取消]--> VO (無効)
```

---

## 6. フェーズ1: 会計基盤

### 6.1 勘定科目マスタ設計

```sql
CREATE TABLE IF NOT EXISTS accounts (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    code            TEXT NOT NULL UNIQUE,
    name            TEXT NOT NULL,
    account_type    TEXT NOT NULL,   -- A=資産, L=負債, E=純資産, R=収益, X=費用
    normal_balance  TEXT NOT NULL,   -- D=借方, C=貸方
    parent_id       INTEGER REFERENCES accounts(id),
    description     TEXT,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 標準勘定科目（iDempiere 準拠）
INSERT INTO accounts (code, name, account_type, normal_balance) VALUES
-- 資産
('1000', '流動資産合計',      'A', 'D'),
('1100', '売掛金',            'A', 'D'),
('1110', '完成工事未収入金',  'A', 'D'),
('1200', '棚卸資産',          'A', 'D'),
('1900', '現金及び預金',      'A', 'D'),
-- 負債
('2000', '流動負債合計',      'L', 'C'),
('2100', '買掛金',            'L', 'C'),
('2300', '前受金',            'L', 'C'),
('2400', '仮受消費税',        'L', 'C'),
('2410', '仮払消費税',        'A', 'D'),
-- 収益
('4000', '売上高合計',        'R', 'C'),
('4100', 'サービス売上高',    'R', 'C'),
('4200', '商品売上高',        'R', 'C'),
-- 費用
('5000', '売上原価',          'X', 'D'),
('5100', '仕入高',            'X', 'D'),
('6000', '販売管理費',        'X', 'D'),
('6100', '外注費',            'X', 'D');
```

### 6.2 仕訳スキーマ設計

```sql
CREATE TABLE IF NOT EXISTS journals (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no     TEXT NOT NULL UNIQUE,   -- JNL-YYYYMM-XXXX
    doc_status      TEXT NOT NULL DEFAULT 'DR',
    journal_type    TEXT NOT NULL,           -- AR, AP, GL, MAN
    date_acct       TEXT NOT NULL,
    description     TEXT,
    source_table    TEXT,                    -- bills, payments, ap_invoices
    source_id       INTEGER,
    total_debit     REAL NOT NULL DEFAULT 0,
    total_credit    REAL NOT NULL DEFAULT 0,
    is_balanced     INTEGER NOT NULL DEFAULT 0,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS journal_lines (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    journal_id      INTEGER NOT NULL REFERENCES journals(id),
    line_no         INTEGER NOT NULL,
    account_id      INTEGER NOT NULL REFERENCES accounts(id),
    debit_amt       REAL NOT NULL DEFAULT 0,
    credit_amt      REAL NOT NULL DEFAULT 0,
    description     TEXT,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);
```

### 6.3 仕訳起票ルール

#### 請求確定時（Bill.doc_status: DR → CO）

```
借方: 売掛金 (1100)        = grand_total
  貸方: 売上高 (4100)       = tax_base_amt
  貸方: 仮受消費税 (2400)   = tax_amt
```

#### 入金時（Payment type='AR' 確定時）

```
借方: 現金及び預金 (1900)  = pay_amt
  貸方: 売掛金 (1100)       = pay_amt
```

#### 売上認識確定時（Recognition.doc_status: DR → CO）

```
借方: 売掛金 (1100)        = grand_total
  貸方: サービス売上高 (4100) = grand_total
```

#### 仕入請求確定時（APInvoice.doc_status: DR → CO）

```
借方: 仕入高 (5100)        = tax_base_amt
借方: 仮払消費税 (2410)    = tax_amt
  貸方: 買掛金 (2100)       = grand_total
```

#### 支払時（Payment type='AP' 確定時）

```
借方: 買掛金 (2100)        = pay_amt
  貸方: 現金及び預金 (1900)  = pay_amt
```

### 6.4 試算表ページ設計（TrialBalance.yaml）

```yaml
page:
  title: 試算表
  type: sql_table
  sql: |
    SELECT
      a.code          AS 勘定科目コード,
      a.name          AS 勘定科目名,
      a.account_type  AS 区分,
      COALESCE(SUM(jl.debit_amt), 0)  AS 借方合計,
      COALESCE(SUM(jl.credit_amt), 0) AS 貸方合計,
      CASE a.normal_balance
        WHEN 'D' THEN COALESCE(SUM(jl.debit_amt), 0) - COALESCE(SUM(jl.credit_amt), 0)
        WHEN 'C' THEN COALESCE(SUM(jl.credit_amt), 0) - COALESCE(SUM(jl.debit_amt), 0)
      END AS 残高
    FROM accounts a
    LEFT JOIN journal_lines jl ON jl.account_id = a.id
    LEFT JOIN journals j ON j.id = jl.journal_id AND j.doc_status = 'CO'
    WHERE a.is_active = 1
    GROUP BY a.id, a.code, a.name, a.account_type, a.normal_balance
    ORDER BY a.code
```

---

## 7. フェーズ2: 購買フロー

### 7.1 フロー概要

```
1. 購買担当が発注書(PurchaseOrder)を作成
   -> 金額に応じて承認ワークフローへ（Phase 3）

2. 確定(CO)後、発注書の doc_status = 'IP'（受入待ち）

3. 品物/サービス受入時、受入処理(PurchaseReceipt)を作成
   -> purchase_order_lines.qty_received を更新
   -> stock_moves テーブルに入庫記録（在庫品の場合）
   -> 全明細受入完了時、purchase_orders.doc_status = 'CO'

4. 仕入先から請求書が来たら AP請求書(APInvoice)を作成
   -> 発注書と照合（価格・数量）

5. AP請求書確定時
   -> journals に仕訳起票（仕入 DR / 買掛金 CR）

6. 支払期日に支払処理(Payment)を作成・確定
   -> journals に仕訳起票（買掛金 DR / 銀行 CR）
   -> ap_invoices.outstanding_amt を更新
```

### 7.2 在庫管理ルール

- `products.product_type = 'I'`（在庫品）のみ `stock_moves` に記録
- `product_type = 'S'`（サービス）は在庫移動なし
- 現在の在庫数量は `stock_moves` の集計で算出（別途在庫テーブル不要）

```sql
-- 現在庫数量の照会クエリ
SELECT
    p.code, p.name,
    SUM(CASE sm.move_type WHEN 'IN' THEN sm.qty ELSE -sm.qty END) AS current_stock
FROM products p
LEFT JOIN stock_moves sm ON sm.product_id = p.id
WHERE p.product_type = 'I'
GROUP BY p.id, p.code, p.name;
```

### 7.3 3方向照合（3-Way Matching）

発注書・受入・請求書の照合チェック（APInvoiceCompleteHook 内で実装）:

```
1. 数量チェック: ap_invoice_lines.qty <= purchase_receipt_lines.qty_received の合計
2. 単価チェック: 許容差 +/-5% 以内
3. 発注書ステータスチェック: 'IP' であること（受入済みであること）
```

---

## 8. フェーズ3: 承認ワークフロー

### 8.1 承認ルール設計（将来の config/approval_rules.yml）

```yaml
approval_rules:
  purchase_orders:
    - condition: "grand_total < 100000"
      steps:
        - approver_role: "manager"
          label: 上長承認
    - condition: "grand_total >= 100000 AND grand_total < 1000000"
      steps:
        - approver_role: "manager"
          label: 部長承認
        - approver_role: "director"
          label: 取締役承認
    - condition: "grand_total >= 1000000"
      steps:
        - approver_role: "manager"
          label: 部長承認
        - approver_role: "director"
          label: 取締役承認
        - approver_role: "executive"
          label: 役員承認
```

### 8.2 フレームワーク側拡張が必要な機能

| 拡張内容 | 概要 |
|---|---|
| `ApprovalService` | 承認申請の作成・ステップ進行管理 |
| `IApprovalHook` | エンティティへの承認フック注入インターフェース |
| 承認UIページ | 承認一覧・承認/却下ボタンのHTMXページ |
| 通知連携 | 承認待ちのTODO自動作成 |

### 8.3 暫定実装（Phase 3前の簡易承認）

フレームワーク拡張が完了するまでの暫定対応:

```yaml
# purchase_order.yml に以下フィールドを追加
approved_by:     { type: string, label: 承認者 }
approved_at:     { type: string, label: 承認日時 }
approval_status: { type: string, label: 承認状況 }  # PENDING / APPROVED / REJECTED
```

Hook で金額チェックを行い、一定金額以上は `approval_status = 'PENDING'` に設定。
承認者が `approved_by` を入力し `approval_status = 'APPROVED'` に更新することで確定可能とする。

---

## 9. Hook実装仕様

### 9.1 Phase 1 追加Hookの詳細

#### `BillCompleteHook`（AccountingHooks.cs）

| 属性 | 値 |
|---|---|
| 登録タイミング | `AfterAsync`（Update、doc_status が CO に変化した時） |
| 処理 | `journals` + `journal_lines` に仕訳を自動挿入 |
| トランザクション | 同一トランザクション内（Bill確定と仕訳起票はアトミック） |

```csharp
// 実装骨格（擬似コード）
public async Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx)
{
    var newStatus = ctx.Values.GetValueOrDefault("DocStatus")?.ToString();
    if (newStatus != "CO") return;

    var billId = Convert.ToInt32(ctx.Values["Id"]);

    // bills テーブルから最新データを取得
    var bill = await db.QuerySingleAsync<dynamic>(
        "SELECT * FROM bills WHERE id = @id", new { id = billId }, tx);

    // 採番: JNL-YYYYMM-XXXX
    var journalNo = await GenerateJournalNoAsync(db, tx);

    // journals ヘッダ挿入
    var journalId = await db.ExecuteScalarAsync<long>(@"
        INSERT INTO journals (document_no, doc_status, journal_type, date_acct,
                              description, source_table, source_id,
                              total_debit, total_credit, is_balanced)
        VALUES (@no, 'CO', 'AR', @dateAcct, @desc, 'bills', @billId,
                @grandTotal, @grandTotal, 1);
        SELECT last_insert_rowid()",
        new {
            no = journalNo,
            dateAcct = bill.date_billed,
            desc = $"請求確定: {bill.document_no}",
            billId,
            grandTotal = bill.grand_total
        }, tx);

    // 明細1: 売掛金 (DR)
    var arAccountId = await GetAccountIdAsync(db, tx, "1100");
    await InsertJournalLineAsync(db, tx, journalId, 10, arAccountId,
        debit: bill.grand_total, credit: 0, "売掛金計上");

    // 明細2: 売上高 (CR)
    var salesAccountId = await GetAccountIdAsync(db, tx, "4100");
    await InsertJournalLineAsync(db, tx, journalId, 20, salesAccountId,
        debit: 0, credit: bill.tax_base_amt, "売上計上");

    // 明細3: 仮受消費税 (CR)
    var taxAccountId = await GetAccountIdAsync(db, tx, "2400");
    await InsertJournalLineAsync(db, tx, journalId, 30, taxAccountId,
        debit: 0, credit: bill.tax_amt, "消費税計上");
}
```

#### `BillReverseHook`（AccountingHooks.cs）

| 属性 | 値 |
|---|---|
| 登録タイミング | `AfterAsync`（doc_status が RE に変化した時） |
| 処理 | 元の仕訳の逆仕訳を自動起票（借方と貸方を入れ替え） |

#### `RecognitionCompleteHook`（AccountingHooks.cs）

| 属性 | 値 |
|---|---|
| 登録タイミング | `AfterAsync`（doc_status が CO に変化した時） |
| 処理 | 売上認識の仕訳起票（売掛金(DR) / サービス売上高(CR)） |

#### `JournalDocumentNoHook`（AccountingHooks.cs）

| 属性 | 値 |
|---|---|
| 登録タイミング | `BeforeAsync`（Create） |
| 処理 | `JNL-YYYYMM-XXXX` 形式の自動採番 |

#### `JournalBalanceValidationHook`（AccountingHooks.cs）

| 属性 | 値 |
|---|---|
| 登録タイミング | `BeforeAsync`（Update/Create） |
| 処理 | `total_debit != total_credit` の場合は `HookResult.Abort()` で保存拒否 |

### 9.2 Phase 2 追加Hookの詳細

#### `PurchaseReceiptCompleteHook`（PurchaseHooks.cs）

```
処理:
1. purchase_order_lines.qty_received を加算
2. 在庫品（product_type='I'）の場合、stock_moves に IN レコードを挿入
3. 全明細受入完了（qty_received >= qty_ordered）の場合
   purchase_orders.doc_status = 'CO' に自動更新
```

#### `APInvoiceCompleteHook`（PurchaseHooks.cs）

```
処理:
1. 3方向照合チェック（数量・単価の検証）
2. journals に仕訳起票
   借: 仕入高 (5100)      = tax_base_amt
   借: 仮払消費税 (2410)  = tax_amt
   貸: 買掛金 (2100)      = grand_total
```

#### `PaymentCompleteHook`（PurchaseHooks.cs）

```
AR入金の場合:
  bills.pay_amt を加算
  bills.outstanding_amt を再計算
  outstanding_amt == 0 の場合 bills.doc_status = 'CL'
  仕訳: 現金(1900)(DR) / 売掛金(1100)(CR)

AP支払の場合:
  ap_invoices.pay_amt を加算
  ap_invoices.outstanding_amt を再計算
  仕訳: 買掛金(2100)(DR) / 現金(1900)(CR)
```

---

## 10. バッチジョブ拡張計画

### 10.1 既存バッチジョブ

| ファイル | スケジュール | 処理 |
|---|---|---|
| `contract_expiry_alert.yml` | 毎日8:00 | 期限切れ前90日の契約アラート CSV 出力 |
| `monthly_billing.yml` | 毎月1日 | 月次請求書の自動生成 |

### 10.2 Phase 1 追加バッチジョブ

#### `journal_close.yml`（月次締め処理）

```yaml
name: journal_close
display_name: 月次仕訳締め処理
schedule: "0 0 1 * *"   # 毎月1日0時
type: sql_to_csv
output: jobs/output/journal_unclosed_{date}.csv
sql: |
  SELECT
    document_no AS 仕訳番号,
    journal_type AS 種別,
    date_acct AS 計上日,
    description AS 摘要,
    total_debit AS 借方合計,
    total_credit AS 貸方合計
  FROM journals
  WHERE doc_status = 'DR'
    AND date_acct < date('now', 'start of month')
  ORDER BY date_acct
```

### 10.3 Phase 2 追加バッチジョブ

#### `payment_reminder.yml`（支払督促）

```yaml
name: payment_reminder
display_name: 未収入金督促レポート
schedule: "0 9 * * 1"   # 毎週月曜9時
type: sql_to_csv
output: jobs/output/payment_reminder_{date}.csv
sql: |
  SELECT
    b.document_no AS 請求番号,
    bp.name AS 取引先,
    b.date_due AS 支払期限,
    b.outstanding_amt AS 未収金額,
    CAST(julianday('now') - julianday(b.date_due) AS INT) AS 延滞日数
  FROM bills b
  JOIN business_partners bp ON bp.id = b.business_partner_id
  WHERE b.doc_status = 'CO'
    AND b.outstanding_amt > 0
    AND b.date_due < date('now')
  ORDER BY 延滞日数 DESC
```

---

## 11. 実装ロードマップ

### 11.1 優先度マトリクス

| 優先度 | フェーズ | 内容 | 難易度 | 依存関係 |
|---|---|---|---|---|
| P1 | 会計基盤 | `accounts` + `journals` テーブル追加 | 小 | なし |
| P1 | 会計基盤 | `BillCompleteHook` 仕訳起票 | 中 | スキーマ追加後 |
| P1 | 会計基盤 | `RecognitionCompleteHook` 仕訳起票 | 中 | スキーマ追加後 |
| P1 | 会計基盤 | 試算表・残高照会ページ | 小 | journals データ後 |
| P2 | 購買フロー | 購買系テーブル追加（PO/Receipt/APInvoice/Payment） | 中 | なし |
| P2 | 購買フロー | `stock_moves` + 在庫照会ページ | 小 | 購買テーブル後 |
| P2 | 購買フロー | `PurchaseReceiptCompleteHook` | 中 | 購買テーブル後 |
| P2 | 購買フロー | `APInvoiceCompleteHook` | 中 | P1会計基盤後 |
| P2 | 購買フロー | `PaymentCompleteHook` | 中 | P1会計基盤後 |
| P3 | 承認WF | フレームワーク拡張（`ApprovalService`） | 大 | P2完成後 |
| P3 | 承認WF | 発注書承認フロー | 大 | フレームワーク拡張後 |
| P3 | 資金繰り | キャッシュフロー照会ページ | 中 | payments データ後 |

### 11.2 Phase 1 実装ステップ（詳細）

```
Step 1: スキーマ追加 (database/init.sql)
  - accounts テーブル追加
  - journals テーブル追加
  - journal_lines テーブル追加
  - 標準勘定科目マスタデータ追加
  - テスト用仕訳データ追加

Step 2: YAML エンティティ定義
  - entities/account.yml
  - entities/journal.yml
  - entities/journal_line.yml

Step 3: Hook 実装 (Hooks/AccountingHooks.cs)
  - JournalDocumentNoHook  (採番)
  - JournalBalanceValidationHook  (借貸均衡チェック)
  - BillCompleteHook  (請求確定時の仕訳起票)
  - BillReverseHook  (請求取消時の逆仕訳起票)
  - RecognitionCompleteHook  (売上認識確定時の仕訳起票)

Step 4: Hook の登録
  - bill.yml の hooks セクションに BillCompleteHook / BillReverseHook を追加
  - recognition.yml の hooks セクションに RecognitionCompleteHook を追加
  - journal.yml の hooks セクションに JournalDocumentNoHook / JournalBalanceValidationHook を追加

Step 5: カスタムページ追加
  - pages/AccountBalance.yaml  (勘定科目残高照会)
  - pages/TrialBalance.yaml  (試算表)

Step 6: ナビゲーション更新
  - config/layout.yml に「会計」セクション追加

Step 7: テスト作成
  - NetYamlForge.Tests/Hooks/AccountingHooksTests.cs
    - BillCompleteHook: 請求確定時に仕訳が3行挿入されることを確認
    - BillCompleteHook: 借方=貸方であることを確認
    - BillReverseHook: 逆仕訳が正しく起票されることを確認
    - JournalBalanceValidationHook: 不均衡仕訳が拒否されることを確認
```

---

## 付録: 設計原則

### iDempiere 準拠の原則

1. **ドキュメント中心**: 全業務処理はドキュメント（見積/契約/請求等）を軸に設計
2. **二重仕訳**: 全ての金銭トランザクションは借方・貸方で必ず均衡（is_balanced=1）
3. **取消不可原則**: 確定(CO)済みドキュメントは直接削除せず、反転仕訳(RE)で訂正
4. **トランザクション一貫性**: ドキュメント確定と仕訳起票は同一DBトランザクション内

### NetYamlForge フレームワーク制約

- Hook は `IEntityHook` インターフェース実装のみ
- 仕訳起票は `AfterAsync` 内で `IDbConnection` / `IDbTransaction` を通じて実行
- 複数テーブルへの書き込みは同一トランザクション内で可能（`tx` を渡す）
- `SqlSafetyGuard` を通さない動的テーブル名は使用不可
- カスタムページは `pages/*.yaml` の SQL で集計クエリを実装
