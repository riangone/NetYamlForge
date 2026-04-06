# JPiere Contract Service (JPCS) — NetYamlForge サブプロジェクト 詳細設計書

**バージョン**: 1.0  
**作成日**: 2026-04-06  
**ブランチ**: `feature/jpiere-erp-subproject`  
**ソース**: [JPiere/JPCS ExpDat.jar](https://github.com/JPiere/JPCS/blob/master/data/ExpDat.jar)

---

## 1. 概要

### 1.1 プロジェクト目的

JPiere Contract Service（JPCS）は、日本企業向け ERP システム **JPiere**（iDempiere ベース）の契約管理モジュールです。
本設計書は、JPCS の PostgreSQL ダンプ（`ExpDat.jar`、140MB、1,148 テーブル）を解析し、
NetYamlForge フレームワークのサブプロジェクトとして再実装するための詳細設計を定義します。

### 1.2 スコープ（JPiere → NetYamlForge 変換対象）

JPiere の膨大なテーブル群から、コアビジネス機能に絞って以下を実装します：

| JPiere テーブル群 | NetYamlForge エンティティ | 機能 |
|-----------------|--------------------------|------|
| `c_bpartner` | `business_partners` | 取引先マスタ |
| `m_product`, `m_product_category` | `products`, `product_categories` | 商品マスタ |
| `jp_contractcategory`, `jp_contractt` | `contract_categories`, `contract_templates` | 契約カテゴリ・テンプレート |
| `jp_contract`, `jp_contractline` | `contracts`, `contract_lines` | 契約ヘッダ・明細 |
| `jp_estimation`, `jp_estimationline` | `estimations`, `estimation_lines` | 見積ヘッダ・明細 |
| `jp_bill`, `jp_billline` | `bills`, `bill_lines` | 請求ヘッダ・明細 |
| `jp_recognition`, `jp_recognitionline` | `recognitions`, `recognition_lines` | 売上計上ヘッダ・明細 |
| `jp_todo`, `jp_todo_category` | `todos`, `todo_categories` | TODOタスク管理 |

**スコープ外**（今回実装しない）：
- 会計仕訳（GL、会計エンジン）
- 在庫移動（M_Movement）
- 製造管理（PP: Production Planning）
- ワークフロー承認エンジン
- 多通貨換算

---

## 2. プロジェクト設定

### 2.1 ディレクトリ構造

```
NetYamlForge/projects/jpiere-cs/
├── project.yaml                    # プロジェクト設定
├── database/
│   ├── init.sql                    # スキーマ + テストデータ
│   └── jpiere-cs.db                # SQLite DB
├── entities/
│   ├── business_partners.yml       # 取引先マスタ
│   ├── product_categories.yml      # 商品カテゴリ
│   ├── products.yml                # 商品マスタ
│   ├── contract_categories.yml     # 契約カテゴリ
│   ├── contract_templates.yml      # 契約テンプレート
│   ├── contracts.yml               # 契約ヘッダ
│   ├── contract_lines.yml          # 契約明細
│   ├── estimations.yml             # 見積ヘッダ
│   ├── estimation_lines.yml        # 見積明細
│   ├── bills.yml                   # 請求ヘッダ
│   ├── bill_lines.yml              # 請求明細
│   ├── recognitions.yml            # 売上計上
│   ├── recognition_lines.yml       # 売上計上明細
│   ├── todo_categories.yml         # TODOカテゴリ
│   └── todos.yml                   # TODOタスク
├── pages/
│   ├── Dashboard.yaml              # トップダッシュボード
│   ├── ContractDetail.yaml         # 契約詳細（ヘッダ+明細）
│   ├── EstimationDetail.yaml       # 見積詳細
│   └── BillDetail.yaml             # 請求詳細
├── dashboard.yml                   # 統計・グラフ
├── config/
│   ├── layout.yml                  # ナビゲーション
│   └── i18n.yml                    # 日本語ラベル
├── jobs/
│   ├── contract_expiry_alert.yml   # 契約期限アラートバッチ
│   └── monthly_billing.yml         # 月次請求バッチ
└── Hooks/
    ├── ContractHooks.cs            # 契約ビジネスロジック
    ├── EstimationHooks.cs          # 見積ビジネスロジック
    └── BillingHooks.cs             # 請求ビジネスロジック
```

### 2.2 project.yaml

```yaml
name: jpiere-cs
displayName: JPiere 契約サービス
description: JPiere Contract Service - 日本企業向け契約・見積・請求管理システム
dbType: sqlite
features:
  - crud
  - dashboard
  - api
  - export
  - batchJobs
theme:
  primaryColor: "#1a5276"
  accentColor: "#2980b9"
  logo: jpiere-logo.png
```

---

## 3. データベース設計

### 3.1 ER 図（エンティティ関連）

```
product_categories ──< products
                              │
contract_categories ──< contracts ──< contract_lines >── products
contract_templates  ──/              │
business_partners   ──/              │
                                     │
estimations ──< estimation_lines >── products
  └── business_partners
  
bills ──< bill_lines >── (invoices ref)
  └── business_partners

recognitions ──< recognition_lines >── contract_lines

todo_categories ──< todos
```

### 3.2 テーブル定義（SQLite 形式）

#### 3.2.1 business_partners（取引先マスタ）

JPiere ソース: `c_bpartner`

```sql
CREATE TABLE business_partners (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    code        TEXT NOT NULL UNIQUE,          -- 取引先コード (value)
    name        TEXT NOT NULL,                 -- 取引先名
    name2       TEXT,                          -- 取引先名2（カナ）
    bp_type     TEXT NOT NULL DEFAULT 'C',    -- C=顧客, V=仕入先, B=両方
    is_customer INTEGER NOT NULL DEFAULT 1,
    is_vendor   INTEGER NOT NULL DEFAULT 0,
    tax_id      TEXT,                          -- 法人番号
    url         TEXT,
    phone       TEXT,
    email       TEXT,
    address1    TEXT,
    address2    TEXT,
    city        TEXT,
    postal_code TEXT,
    credit_limit REAL DEFAULT 0,
    payment_rule TEXT DEFAULT 'T',            -- T=翌月払い, I=即時
    payment_term_days INTEGER DEFAULT 30,
    description TEXT,
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.2 product_categories（商品カテゴリ）

JPiere ソース: `m_product_category`, `jp_productcategoryl1`, `jp_productcategoryl2`

```sql
CREATE TABLE product_categories (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    code        TEXT NOT NULL UNIQUE,
    name        TEXT NOT NULL,
    parent_id   INTEGER REFERENCES product_categories(id),
    description TEXT,
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.3 products（商品マスタ）

JPiere ソース: `m_product`

```sql
CREATE TABLE products (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    code                TEXT NOT NULL UNIQUE,    -- SKU/品番
    name                TEXT NOT NULL,
    description         TEXT,
    product_category_id INTEGER NOT NULL REFERENCES product_categories(id),
    uom                 TEXT NOT NULL DEFAULT 'EA',  -- 単位
    product_type        TEXT NOT NULL DEFAULT 'I',   -- I=在庫品, S=サービス, R=リソース
    is_purchased        INTEGER NOT NULL DEFAULT 1,
    is_sold             INTEGER NOT NULL DEFAULT 1,
    list_price          REAL DEFAULT 0,              -- 定価
    std_price           REAL DEFAULT 0,              -- 標準価格
    cost_price          REAL DEFAULT 0,              -- 原価
    tax_rate            REAL DEFAULT 0.10,           -- 消費税率
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.4 contract_categories（契約カテゴリ）

JPiere ソース: `jp_contractcategory`, `jp_contractcategoryl1`, `jp_contractcategoryl2`

```sql
CREATE TABLE contract_categories (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    code        TEXT NOT NULL UNIQUE,
    name        TEXT NOT NULL,
    parent_id   INTEGER REFERENCES contract_categories(id),
    description TEXT,
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.5 contract_templates（契約テンプレート）

JPiere ソース: `jp_contractt`

```sql
CREATE TABLE contract_templates (
    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    code                    TEXT NOT NULL UNIQUE,
    name                    TEXT NOT NULL,
    contract_type           TEXT NOT NULL DEFAULT 'PUR',  -- PUR=購買, SAL=販売, BOT=両方
    contract_category_id    INTEGER NOT NULL REFERENCES contract_categories(id),
    description             TEXT,
    default_payment_term_days INTEGER DEFAULT 30,
    auto_renewal            INTEGER DEFAULT 0,   -- 自動更新フラグ
    is_active               INTEGER NOT NULL DEFAULT 1,
    created_at              TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at              TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.6 contracts（契約ヘッダ）

JPiere ソース: `jp_contract`

```sql
CREATE TABLE contracts (
    id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no                 TEXT NOT NULL UNIQUE,      -- 契約番号
    name                        TEXT NOT NULL,             -- 契約名称
    contract_type               TEXT NOT NULL DEFAULT 'SAL', -- SAL=販売, PUR=購買
    doc_status                  TEXT NOT NULL DEFAULT 'DR',
        -- DR=下書き, IN=進行中, CO=確定, CL=解約, VO=無効
    contract_status             TEXT NOT NULL DEFAULT 'WP',
        -- WP=交渉中, AC=有効, EX=期限切れ, CA=解約
    contract_category_id        INTEGER REFERENCES contract_categories(id),
    contract_template_id        INTEGER REFERENCES contract_templates(id),
    business_partner_id         INTEGER NOT NULL REFERENCES business_partners(id),
    sales_rep                   TEXT,                     -- 営業担当者名
    date_acct                   TEXT NOT NULL,            -- 会計日付
    period_date_from            TEXT NOT NULL,            -- 契約開始日
    period_date_to              TEXT,                     -- 契約終了日
    auto_renewal                INTEGER DEFAULT 0,
    cancel_deadline             TEXT,                     -- 解約申し出期限
    cancel_date                 TEXT,                     -- 解約日
    cancel_cause                TEXT,                     -- 解約理由
    monthly_revenue_amt         REAL DEFAULT 0,           -- 月次売上金額
    monthly_expense_amt         REAL DEFAULT 0,           -- 月次費用金額
    total_doc_amt               REAL DEFAULT 0,           -- 契約総額
    currency                    TEXT NOT NULL DEFAULT 'JPY',
    description                 TEXT,
    is_active                   INTEGER NOT NULL DEFAULT 1,
    created_at                  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at                  TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.7 contract_lines（契約明細）

JPiere ソース: `jp_contractline`

```sql
CREATE TABLE contract_lines (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    contract_id         INTEGER NOT NULL REFERENCES contracts(id),
    line_no             INTEGER NOT NULL,
    product_id          INTEGER REFERENCES products(id),
    description         TEXT,
    qty                 REAL NOT NULL DEFAULT 1,
    uom                 TEXT DEFAULT 'EA',
    unit_price          REAL NOT NULL DEFAULT 0,
    line_amt            REAL NOT NULL DEFAULT 0,    -- qty × unit_price
    tax_rate            REAL DEFAULT 0.10,
    tax_amt             REAL DEFAULT 0,
    billing_policy      TEXT DEFAULT 'M',           -- M=月次, Y=年次, O=一括
    billing_start_date  TEXT,
    billing_end_date    TEXT,
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.8 estimations（見積ヘッダ）

JPiere ソース: `jp_estimation`

```sql
CREATE TABLE estimations (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,
    estimation_date     TEXT NOT NULL DEFAULT (date('now')),
    version             INTEGER NOT NULL DEFAULT 1,
    doc_status          TEXT NOT NULL DEFAULT 'DR',
        -- DR=下書き, IN=提出済み, CO=受注, VO=失注
    is_so_trx           INTEGER NOT NULL DEFAULT 1,   -- 1=販売見積, 0=購買見積
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    sales_rep           TEXT,
    date_promised       TEXT,                         -- 納期
    currency            TEXT NOT NULL DEFAULT 'JPY',
    total_lines         REAL DEFAULT 0,               -- 小計
    grand_total         REAL DEFAULT 0,               -- 合計（税込）
    tax_base_amt        REAL DEFAULT 0,
    tax_amt             REAL DEFAULT 0,
    description         TEXT,
    linked_contract_id  INTEGER REFERENCES contracts(id),  -- 受注後の契約
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.9 estimation_lines（見積明細）

JPiere ソース: `jp_estimationline`

```sql
CREATE TABLE estimation_lines (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    estimation_id   INTEGER NOT NULL REFERENCES estimations(id),
    line_no         INTEGER NOT NULL,
    product_id      INTEGER REFERENCES products(id),
    description     TEXT,
    date_ordered    TEXT NOT NULL DEFAULT (date('now')),
    date_promised   TEXT,
    qty_ordered     REAL NOT NULL DEFAULT 1,
    uom             TEXT DEFAULT 'EA',
    unit_price      REAL NOT NULL DEFAULT 0,
    line_amt        REAL NOT NULL DEFAULT 0,
    tax_rate        REAL DEFAULT 0.10,
    tax_amt         REAL DEFAULT 0,
    discount        REAL DEFAULT 0,                   -- 割引率(%)
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.10 bills（請求ヘッダ）

JPiere ソース: `jp_bill`

```sql
CREATE TABLE bills (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,
    doc_status          TEXT NOT NULL DEFAULT 'DR',
        -- DR=下書き, CO=確定, VO=無効
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    date_billed         TEXT NOT NULL DEFAULT (date('now')),  -- 請求日
    date_due            TEXT,                                  -- 支払期限
    date_sent           TEXT,                                  -- 送付日
    payment_rule        TEXT DEFAULT 'T',                     -- T=翌月, I=即時
    payment_term_days   INTEGER DEFAULT 30,
    currency            TEXT NOT NULL DEFAULT 'JPY',
    total_lines         REAL DEFAULT 0,
    grand_total         REAL DEFAULT 0,
    tax_base_amt        REAL DEFAULT 0,
    tax_amt             REAL DEFAULT 0,
    pay_amt             REAL DEFAULT 0,                       -- 入金額
    outstanding_amt     REAL DEFAULT 0,                       -- 残高
    description         TEXT,
    linked_contract_id  INTEGER REFERENCES contracts(id),
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.11 bill_lines（請求明細）

JPiere ソース: `jp_billline`

```sql
CREATE TABLE bill_lines (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id     INTEGER NOT NULL REFERENCES bills(id),
    line_no     INTEGER NOT NULL,
    description TEXT,
    period_from TEXT,                   -- 対象期間（開始）
    period_to   TEXT,                   -- 対象期間（終了）
    total_lines REAL DEFAULT 0,
    grand_total REAL DEFAULT 0,
    tax_amt     REAL DEFAULT 0,
    pay_amt     REAL DEFAULT 0,
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.12 recognitions（売上計上ヘッダ）

JPiere ソース: `jp_recognition`

```sql
CREATE TABLE recognitions (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,
    doc_status          TEXT NOT NULL DEFAULT 'DR',
    is_so_trx           INTEGER NOT NULL DEFAULT 1,
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    date_acct           TEXT NOT NULL,            -- 計上日付
    grand_total         REAL NOT NULL DEFAULT 0,
    description         TEXT,
    linked_contract_id  INTEGER REFERENCES contracts(id),
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.13 recognition_lines（売上計上明細）

JPiere ソース: `jp_recognitionline`

```sql
CREATE TABLE recognition_lines (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    recognition_id  INTEGER NOT NULL REFERENCES recognitions(id),
    line_no         INTEGER NOT NULL,
    contract_line_id INTEGER REFERENCES contract_lines(id),
    product_id      INTEGER REFERENCES products(id),
    description     TEXT,
    qty_recognized  REAL NOT NULL DEFAULT 1,
    unit_price      REAL DEFAULT 0,
    line_amt        REAL DEFAULT 0,
    tax_rate        REAL DEFAULT 0.10,
    tax_amt         REAL DEFAULT 0,
    period_from     TEXT,
    period_to       TEXT,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.14 todo_categories（TODOカテゴリ）

JPiere ソース: `jp_todo_category`

```sql
CREATE TABLE todo_categories (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    description TEXT,
    color       TEXT DEFAULT '#3498db',   -- ラベルカラー（hex）
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);
```

#### 3.2.15 todos（TODOタスク）

JPiere ソース: `jp_todo`

```sql
CREATE TABLE todos (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    title               TEXT NOT NULL,
    description         TEXT,
    todo_type           TEXT NOT NULL DEFAULT 'T',  -- T=タスク, M=ミーティング, C=電話
    todo_status         TEXT NOT NULL DEFAULT 'NY', -- NY=未着手, IP=進行中, DN=完了, CA=キャンセル
    todo_category_id    INTEGER REFERENCES todo_categories(id),
    assigned_to         TEXT,                       -- 担当者名
    scheduled_start     TEXT,
    scheduled_end       TEXT,
    actual_start        TEXT,
    actual_end          TEXT,
    linked_contract_id  INTEGER REFERENCES contracts(id),
    linked_partner_id   INTEGER REFERENCES business_partners(id),
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);
```

---

## 4. Entity YAML 設計

### 4.1 business_partners.yml（主要フィールドのみ示す）

```yaml
name: business_partners
displayName: 取引先マスタ
description: 顧客・仕入先・その他取引先を管理します

columns:
  - name: id
    type: integer
    primaryKey: true
  - name: code
    type: string
    required: true
    maxLength: 40
  - name: name
    type: string
    required: true
    maxLength: 120
  - name: name2
    type: string
    maxLength: 60
  - name: bp_type
    type: string
    required: true
    enum: [C, V, B]
    enumLabels: [顧客, 仕入先, 両方]
    default: C
  - name: tax_id
    type: string
    maxLength: 20
  - name: phone
    type: string
  - name: email
    type: string
  - name: credit_limit
    type: decimal
    default: 0
  - name: payment_term_days
    type: integer
    default: 30
  - name: is_active
    type: boolean
    default: true

listView:
  columns: [code, name, bp_type, phone, email, credit_limit]
  searchFields: [code, name, tax_id]
  defaultSort: name ASC

formFields:
  - section: 基本情報
    fields: [code, name, name2, bp_type, tax_id, url]
  - section: 連絡先
    fields: [phone, email, address1, address2, city, postal_code]
  - section: 支払条件
    fields: [payment_rule, payment_term_days, credit_limit]
  - section: その他
    fields: [description, is_active]
```

### 4.2 contracts.yml（抜粋）

```yaml
name: contracts
displayName: 契約管理
description: 販売・購買契約のヘッダ情報を管理します

columns:
  - name: id
    type: integer
    primaryKey: true
  - name: document_no
    type: string
    required: true
    maxLength: 30
  - name: name
    type: string
    required: true
    maxLength: 120
  - name: contract_type
    type: string
    required: true
    enum: [SAL, PUR, BOT]
    enumLabels: [販売契約, 購買契約, 両方]
    default: SAL
  - name: doc_status
    type: string
    required: true
    enum: [DR, IN, CO, CL, VO]
    enumLabels: [下書き, 進行中, 確定, 解約, 無効]
    default: DR
  - name: contract_status
    type: string
    required: true
    enum: [WP, AC, EX, CA]
    enumLabels: [交渉中, 有効, 期限切れ, 解約]
    default: WP
  - name: business_partner_id
    type: integer
    required: true
    foreignKey:
      table: business_partners
      displayColumn: name
  - name: contract_category_id
    type: integer
    foreignKey:
      table: contract_categories
      displayColumn: name
  - name: period_date_from
    type: date
    required: true
  - name: period_date_to
    type: date
  - name: monthly_revenue_amt
    type: decimal
    default: 0
  - name: total_doc_amt
    type: decimal
    default: 0
  - name: currency
    type: string
    default: JPY
  - name: is_active
    type: boolean
    default: true

hooks:
  - ContractDocumentNoHook       # 自動採番
  - ContractAmountCalculateHook  # 金額自動計算
  - ContractStatusHook           # ステータス遷移制御
```

---

## 5. ビジネスロジック（Hooks）設計

### 5.1 ContractHooks.cs

| フック名 | タイミング | 処理内容 |
|---------|-----------|---------|
| `ContractDocumentNoHook` | BeforeCreate | 契約番号の自動採番（`CON-YYYYMM-XXXX`） |
| `ContractAmountCalculateHook` | BeforeCreate/Update | 明細から合計金額を再計算 |
| `ContractStatusHook` | BeforeUpdate | ステータス遷移の妥当性チェック |
| `ContractExpiryCheckHook` | AfterRead | 期限切れ警告フラグのセット |

### 5.2 EstimationHooks.cs

| フック名 | タイミング | 処理内容 |
|---------|-----------|---------|
| `EstimationDocumentNoHook` | BeforeCreate | 見積番号の自動採番（`EST-YYYYMM-XXXX`） |
| `EstimationTotalHook` | BeforeCreate/Update | 明細合計・税額の再計算 |
| `EstimationToContractHook` | AfterUpdate | doc_status=CO 時に契約レコードを自動生成 |

### 5.3 BillingHooks.cs

| フック名 | タイミング | 処理内容 |
|---------|-----------|---------|
| `BillDocumentNoHook` | BeforeCreate | 請求番号の自動採番（`BILL-YYYYMM-XXXX`） |
| `BillDueDateHook` | BeforeCreate | 支払期限の自動計算（請求日 + payment_term_days） |
| `BillOutstandingHook` | BeforeUpdate | 残高（grand_total - pay_amt）の更新 |

---

## 6. バッチジョブ設計

### 6.1 contract_expiry_alert.yml

```yaml
name: contract_expiry_alert
displayName: 契約期限アラート
schedule: "0 9 * * 1"   # 毎週月曜9時
type: sql_to_csv
outputPath: jobs/output/contract_expiry_{date}.csv
query: |
  SELECT
    c.document_no,
    c.name,
    bp.name AS partner_name,
    c.period_date_to,
    CAST(julianday(c.period_date_to) - julianday('now') AS INTEGER) AS days_remaining
  FROM contracts c
  JOIN business_partners bp ON c.business_partner_id = bp.id
  WHERE c.doc_status = 'CO'
    AND c.contract_status = 'AC'
    AND c.period_date_to IS NOT NULL
    AND julianday(c.period_date_to) - julianday('now') BETWEEN 0 AND 90
  ORDER BY c.period_date_to ASC
```

### 6.2 monthly_billing.yml

```yaml
name: monthly_billing
displayName: 月次請求サマリー
schedule: "0 8 1 * *"   # 毎月1日8時
type: sql_to_csv
outputPath: jobs/output/monthly_billing_{date}.csv
query: |
  SELECT
    bp.name AS partner_name,
    SUM(b.grand_total) AS total_billed,
    SUM(b.pay_amt) AS total_paid,
    SUM(b.outstanding_amt) AS total_outstanding,
    COUNT(*) AS bill_count
  FROM bills b
  JOIN business_partners bp ON b.business_partner_id = bp.id
  WHERE b.doc_status = 'CO'
    AND strftime('%Y-%m', b.date_billed) = strftime('%Y-%m', 'now', '-1 month')
  GROUP BY bp.id, bp.name
  ORDER BY total_outstanding DESC
```

---

## 7. ダッシュボード設計（dashboard.yml）

```yaml
stats:
  - name: active_contracts
    label: 有効契約数
    icon: file-contract
    color: blue
    query: SELECT COUNT(*) FROM contracts WHERE contract_status = 'AC' AND is_active = 1

  - name: monthly_revenue
    label: 今月の月次売上
    icon: yen-sign
    color: green
    query: |
      SELECT COALESCE(SUM(monthly_revenue_amt), 0)
      FROM contracts
      WHERE contract_status = 'AC' AND is_active = 1

  - name: expiring_contracts
    label: 90日以内に期限切れ
    icon: clock
    color: orange
    query: |
      SELECT COUNT(*) FROM contracts
      WHERE contract_status = 'AC'
        AND period_date_to IS NOT NULL
        AND julianday(period_date_to) - julianday('now') BETWEEN 0 AND 90

  - name: outstanding_bills
    label: 未収残高合計
    icon: file-invoice
    color: red
    query: SELECT COALESCE(SUM(outstanding_amt), 0) FROM bills WHERE doc_status = 'CO'

charts:
  - name: contracts_by_type
    label: 契約種別分布
    type: pie
    query: |
      SELECT contract_type AS label, COUNT(*) AS value
      FROM contracts WHERE is_active = 1
      GROUP BY contract_type

  - name: monthly_revenue_trend
    label: 月次売上トレンド（直近12ヶ月）
    type: bar
    query: |
      SELECT strftime('%Y-%m', date_acct) AS label, SUM(grand_total) AS value
      FROM recognitions
      WHERE is_active = 1
        AND date_acct >= date('now', '-12 months')
      GROUP BY strftime('%Y-%m', date_acct)
      ORDER BY label ASC
```

---

## 8. ページ設計（カスタムページ）

### 8.1 Dashboard.yaml — トップページ

| セクション | 内容 |
|-----------|------|
| 統計カード | 有効契約数 / 月次売上 / 期限切れ予告 / 未収残高 |
| グラフ | 契約種別円グラフ / 月次売上トレンド棒グラフ |
| 期限切れ予告リスト | 90日以内に終了する契約一覧 |
| 未完了TODOリスト | 自分宛・未完了タスク top10 |

### 8.2 ContractDetail.yaml — 契約詳細ページ

| セクション | 内容 |
|-----------|------|
| ヘッダ情報 | 契約番号 / 名称 / ステータス / 取引先 / 期間 / 金額 |
| 明細タブ | contract_lines 一覧（インライン編集対応） |
| 請求タブ | 紐づく bills 一覧 |
| 売上計上タブ | 紐づく recognitions 一覧 |
| TODOタブ | 紐づく todos 一覧 |

### 8.3 EstimationDetail.yaml — 見積詳細

| セクション | 内容 |
|-----------|------|
| ヘッダ情報 | 見積番号 / 日付 / ステータス / 取引先 / 合計 |
| 明細タブ | estimation_lines 一覧 |
| 金額サマリー | 小計 / 税額 / 合計（税込） |
| アクション | 受注確定ボタン（doc_status → CO） |

---

## 9. テストデータ設計

### 9.1 マスタデータ

**product_categories（5件）**:
- ソフトウェア / ハードウェア / SIサービス / 保守サービス / その他

**products（15件）**:
- 基幹システムライセンス、クラウドストレージ、ERP 導入支援、保守年間契約 など

**business_partners（10件）**:
- 株式会社アルファテック（顧客）
- ベータソリューションズ株式会社（顧客）
- ガンマ商事株式会社（仕入先）
- 他7社（顧客・仕入先混在）

**contract_categories（3件）**:
- ソフトウェアライセンス契約 / SI・開発契約 / 保守・運用契約

### 9.2 トランザクションデータ

**contracts（8件）**:
- 有効契約 5件（内 90日以内期限切れ 2件）
- 交渉中 2件
- 解約済み 1件

**estimations（5件）**:
- 下書き 2件 / 提出済み 2件 / 受注確定 1件

**bills（6件）**:
- 確定済み（一部未収）4件 / 下書き 2件

**todos（10件）**:
- 未着手 4件 / 進行中 3件 / 完了 3件

---

## 10. 画面遷移・ナビゲーション設計（layout.yml）

```
トップ（ダッシュボード）
│
├── 取引先管理
│   ├── 取引先一覧
│   └── 取引先詳細（登録/編集）
│
├── 商品管理
│   ├── 商品カテゴリ
│   └── 商品マスタ
│
├── 契約管理
│   ├── 契約一覧
│   ├── 契約詳細（ヘッダ + 明細）
│   └── 契約テンプレート
│
├── 見積管理
│   ├── 見積一覧
│   └── 見積詳細（ヘッダ + 明細）
│
├── 請求管理
│   ├── 請求一覧
│   └── 請求詳細
│
├── 売上計上
│   ├── 売上計上一覧
│   └── 売上計上詳細
│
└── タスク管理
    ├── TODOカテゴリ
    └── TODO一覧
```

---

## 11. ユーザーロール設計

| ロール | 権限 |
|-------|------|
| `Admin` | すべての操作 |
| `SalesManager` | 契約・見積・請求の確定、全データ参照 |
| `SalesRep` | 担当案件の契約・見積登録・編集 |
| `AccountingStaff` | 請求・売上計上の操作 |
| `Viewer` | 参照のみ |

---

## 12. 実装ロードマップ

### Phase 1: 基盤構築（優先度: 高）

| # | 作業 | 成果物 |
|---|------|-------|
| 1 | プロジェクト初期化 | `project.yaml` |
| 2 | データベーススキーマ作成 | `init.sql` |
| 3 | マスタエンティティ YAML | `business_partners.yml`, `products.yml`, etc. |
| 4 | テストデータ投入 | `init.sql` に INSERT 文追加 |

### Phase 2: コアビジネス機能（優先度: 高）

| # | 作業 | 成果物 |
|---|------|-------|
| 5 | 契約エンティティ YAML + Hooks | `contracts.yml`, `ContractHooks.cs` |
| 6 | 見積エンティティ YAML + Hooks | `estimations.yml`, `EstimationHooks.cs` |
| 7 | 請求エンティティ YAML + Hooks | `bills.yml`, `BillingHooks.cs` |

### Phase 3: ダッシュボード・バッチ（優先度: 中）

| # | 作業 | 成果物 |
|---|------|-------|
| 8 | ダッシュボード統計・グラフ | `dashboard.yml` |
| 9 | バッチジョブ設定 | `contract_expiry_alert.yml`, `monthly_billing.yml` |
| 10 | カスタムページ | `ContractDetail.yaml` |

### Phase 4: 売上計上・TODO（優先度: 中）

| # | 作業 | 成果物 |
|---|------|-------|
| 11 | 売上計上エンティティ | `recognitions.yml`, `recognition_lines.yml` |
| 12 | TODOタスク | `todos.yml`, `todo_categories.yml` |

### Phase 5: テスト・品質（優先度: 中）

| # | 作業 | 成果物 |
|---|------|-------|
| 13 | 単体テスト | `JpiereHookTests.cs` |
| 14 | YAML スキーマ検証 | `YamlSchemaValidationTests.cs` に追加 |

---

## 13. 設計上の注意点・制約

### 13.1 iDempiere → NetYamlForge 簡略化方針

| iDempiere の機能 | 簡略化内容 |
|----------------|----------|
| 多組織（AD_ORG） | 単一組織に固定 |
| 多通貨 | JPY 固定（currency カラムは記録のみ） |
| 会計エンジン（FACT_ACCT） | スコープ外 |
| ワークフロー承認 | doc_status による簡易ステータス管理のみ |
| document_no 採番 | C# Hook で自動採番 |

### 13.2 SQLite 制約対応

| PostgreSQL 機能 | SQLite 代替 |
|----------------|------------|
| `SERIAL` / `SEQUENCE` | `AUTOINCREMENT` |
| `TIMESTAMP` 型 | `TEXT` + `datetime()` |
| `NUMERIC(10,0)` | `INTEGER` |
| `CHARACTER(1)` フラグ | `INTEGER` (0/1) または `TEXT` enum |
| `CHECK` 制約 | YAML の `enum` + Hook バリデーション |

---

*設計書終了 — 次のステップ: Phase 1 の実装を開始する*
