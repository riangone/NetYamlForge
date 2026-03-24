# jp-biz-doc skill

NetYamlForge プロジェクトで日本向けビジネス文書（見積書・請求書・納品書・契約書）を追加・更新するためのスキル。

## このスキルを使う場面

- 新しい日本語ビジネス文書エンティティを追加する
- 既存の `biz-docs` JP エンティティを更新する
- インボイス制度対応の請求書フィールドを追加する
- 日本の商習慣に合った PDF エクスポートを定義する

---

## 実装チェックリスト

### 1. データベーススキーマ（init_seed.sql）

#### 見積書テーブル（JpEstimate + JpEstimateItem）
```sql
CREATE TABLE JpEstimate (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EstimateNo      TEXT    NOT NULL UNIQUE,          -- MK-2026-0001
    CustomerId      INTEGER NOT NULL,
    Title           TEXT    NOT NULL,
    IssueDate       TEXT    NOT NULL,                 -- YYYY-MM-DD
    ExpiryDate      TEXT,                             -- 有効期限
    Subtotal10      REAL    NOT NULL DEFAULT 0,       -- 10%対象小計（税抜）
    TaxAmount10     REAL    NOT NULL DEFAULT 0,       -- 消費税額(10%)
    Subtotal8       REAL    NOT NULL DEFAULT 0,       -- 8%対象小計（軽減）
    TaxAmount8      REAL    NOT NULL DEFAULT 0,       -- 消費税額(8%)
    Subtotal        REAL    NOT NULL DEFAULT 0,       -- 税抜合計
    TaxAmount       REAL    NOT NULL DEFAULT 0,       -- 消費税合計
    Total           REAL    NOT NULL DEFAULT 0,       -- 税込合計
    PaymentTerms    TEXT,
    DeliveryDays    TEXT,
    DeliveryPlace   TEXT,
    ValidityNote    TEXT,   -- 「本見積の有効期限は発行日より30日間とします。」
    ExclusionNote   TEXT,   -- 「以下は見積に含まれません：...」
    Status          TEXT    NOT NULL DEFAULT 'draft', -- draft/sent/accepted/rejected/expired
    PreparedBy      TEXT,
    CompanyStamp    TEXT,
    Remarks         TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);

CREATE TABLE JpEstimateItem (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    EstimateId  INTEGER NOT NULL,
    LineNo      INTEGER NOT NULL,
    ItemCode    TEXT,
    ItemName    TEXT    NOT NULL,
    Spec        TEXT,
    Unit        TEXT    NOT NULL DEFAULT '式',
    Quantity    REAL    NOT NULL DEFAULT 1,
    UnitPrice   REAL    NOT NULL DEFAULT 0,
    Amount      REAL    NOT NULL DEFAULT 0,
    TaxRate     REAL    NOT NULL DEFAULT 10,           -- 10 or 8
    FOREIGN KEY (EstimateId) REFERENCES JpEstimate(Id)
);
```

#### 請求書テーブル（JpInvoice + JpInvoiceItem）
```sql
CREATE TABLE JpInvoice (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo       TEXT    NOT NULL UNIQUE,           -- INV-JP-0001
    CustomerId      INTEGER NOT NULL,
    Title           TEXT    NOT NULL,
    IssueDate       TEXT    NOT NULL,
    DueDate         TEXT    NOT NULL,                  -- 支払期日
    RegistrationNo  TEXT,                              -- T + 13桁（インボイス登録番号）
    Subtotal10      REAL    NOT NULL DEFAULT 0,
    TaxAmount10     REAL    NOT NULL DEFAULT 0,
    Subtotal8       REAL    NOT NULL DEFAULT 0,
    TaxAmount8      REAL    NOT NULL DEFAULT 0,
    Subtotal        REAL    NOT NULL DEFAULT 0,
    TaxAmount       REAL    NOT NULL DEFAULT 0,
    Total           REAL    NOT NULL DEFAULT 0,
    BankName        TEXT,
    BranchName      TEXT,
    AccountType     TEXT    DEFAULT '普通',            -- 普通 or 当座
    AccountNo       TEXT,
    AccountHolder   TEXT,
    TransferFeeNote TEXT    DEFAULT '振込手数料はご負担くださいますようお願い申し上げます。',
    Status          TEXT    NOT NULL DEFAULT 'draft',  -- draft/issued/paid/overdue/cancelled
    PreparedBy      TEXT,
    CompanyStamp    TEXT,
    Remarks         TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);

CREATE TABLE JpInvoiceItem (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId   INTEGER NOT NULL,
    LineNo      INTEGER NOT NULL,
    ItemCode    TEXT,
    ItemName    TEXT    NOT NULL,
    Spec        TEXT,
    Unit        TEXT    NOT NULL DEFAULT '式',
    Quantity    REAL    NOT NULL DEFAULT 1,
    UnitPrice   REAL    NOT NULL DEFAULT 0,
    Amount      REAL    NOT NULL DEFAULT 0,
    TaxRate     REAL    NOT NULL DEFAULT 10,
    FOREIGN KEY (InvoiceId) REFERENCES JpInvoice(Id)
);
```

#### 納品書テーブル（JpDelivery + JpDeliveryItem）
```sql
CREATE TABLE JpDelivery (
    Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    DeliveryNo           TEXT    NOT NULL UNIQUE,       -- DLV-JP-0001
    CustomerId           INTEGER NOT NULL,
    Title                TEXT    NOT NULL,
    DeliveryDate         TEXT    NOT NULL,
    DeliveryPlace        TEXT,
    Subtotal10           REAL    NOT NULL DEFAULT 0,
    TaxAmount10          REAL    NOT NULL DEFAULT 0,
    Subtotal8            REAL    NOT NULL DEFAULT 0,
    TaxAmount8           REAL    NOT NULL DEFAULT 0,
    Subtotal             REAL    NOT NULL DEFAULT 0,
    TaxAmount            REAL    NOT NULL DEFAULT 0,
    Total                REAL    NOT NULL DEFAULT 0,
    InspectionPeriodDays INTEGER NOT NULL DEFAULT 5,    -- 検収期間（営業日）
    ReceiptConfirmedDate TEXT,                          -- 受領確認日
    Status               TEXT    NOT NULL DEFAULT 'draft', -- draft/delivered/confirmed/returned
    PreparedBy           TEXT,
    CompanyStamp         TEXT,
    Remarks              TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);

CREATE TABLE JpDeliveryItem (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    DeliveryId  INTEGER NOT NULL,
    LineNo      INTEGER NOT NULL,
    ItemCode    TEXT,
    ItemName    TEXT    NOT NULL,
    Spec        TEXT,
    Unit        TEXT    NOT NULL DEFAULT '式',
    Quantity    REAL    NOT NULL DEFAULT 1,
    UnitPrice   REAL    NOT NULL DEFAULT 0,
    Amount      REAL    NOT NULL DEFAULT 0,
    TaxRate     REAL    NOT NULL DEFAULT 10,
    FOREIGN KEY (DeliveryId) REFERENCES JpDelivery(Id)
);
```

#### 契約書台帳（JpContract）
```sql
CREATE TABLE JpContract (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    ContractNo       TEXT    NOT NULL UNIQUE,           -- CTR-2026-0001
    CustomerId       INTEGER NOT NULL,
    Title            TEXT    NOT NULL,
    ContractType     TEXT    NOT NULL,
    StartDate        TEXT    NOT NULL,
    EndDate          TEXT,
    AutoRenew        INTEGER NOT NULL DEFAULT 0,        -- 0=なし 1=あり
    ContractAmount   REAL,
    PaymentTerms     TEXT,
    Status           TEXT    NOT NULL DEFAULT 'draft',  -- draft/review/active/expired/terminated
    SignedDate       TEXT,
    IsElectronic     INTEGER NOT NULL DEFAULT 1,        -- 1=電子（印紙不要）0=紙
    StampTaxAmount   INTEGER,                           -- 印紙税額（紙のみ）
    JurisdictionCourt TEXT DEFAULT '東京地方裁判所',
    GoverningLaw     TEXT    DEFAULT '日本法',
    OurSignatory     TEXT,
    TheirSignatory   TEXT,
    Remarks          TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
```

---

### 2. エンティティ YAML（entities/jp_*.yml）

#### 重要フィールド定義パターン

**インボイス登録番号（請求書のみ）**
```yaml
RegistrationNo:
  type: string
  label: 登録番号（T番号）
  editable: true
  placeholder: "T1234567890123"
```

**税率別集計（見積書・請求書・納品書共通）**
```yaml
Subtotal10:
  type: decimal
  label: 10%対象小計
  editable: true
  precision: 0
TaxAmount10:
  type: decimal
  label: 消費税額(10%)
  editable: true
  precision: 0
Subtotal8:
  type: decimal
  label: 8%対象小計（軽減）
  editable: true
  precision: 0
TaxAmount8:
  type: decimal
  label: 消費税額(8%)
  editable: true
  precision: 0
Subtotal:
  type: decimal
  label: 小計（税抜合計）
  editable: true
  precision: 0
TaxAmount:
  type: decimal
  label: 消費税合計
  editable: true
  precision: 0
Total:
  type: decimal
  required: true
  label: 合計金額（税込）
  editable: true
  precision: 0
```

**電子契約フラグ（契約書のみ）**
```yaml
IsElectronic:
  type: boolean
  label: 電子契約
  editable: true
StampTaxAmount:
  type: int
  label: 印紙税額（円）
  editable: true
JurisdictionCourt:
  type: string
  label: 合意管轄裁判所
  editable: true
  placeholder: "東京地方裁判所"
GoverningLaw:
  type: string
  label: 準拠法
  editable: true
  placeholder: "日本法"
```

**検収期間・受領確認（納品書のみ）**
```yaml
InspectionPeriodDays:
  type: int
  label: 検収期間（営業日）
  editable: true
ReceiptConfirmedDate:
  type: date
  label: 受領確認日
  editable: true
```

---

### 3. カスタムSQL エクスポート（exports/sql/）

#### jp_estimate_detail.sql パターン
```sql
SELECT e.EstimateNo, cu.Name AS CustomerName,
       e.Title, e.IssueDate, e.ExpiryDate, e.Status,
       ei.LineNo, ei.ItemName, ei.Unit, ei.Quantity,
       ei.UnitPrice, ei.Amount, ei.TaxRate,
       e.Total AS EstimateTotal,
       e.Subtotal10, e.TaxAmount10, e.Subtotal8, e.TaxAmount8,
       e.ValidityNote, e.PreparedBy
FROM JpEstimate e
JOIN Customer cu ON cu.Id = e.CustomerId
JOIN JpEstimateItem ei ON ei.EstimateId = e.Id
ORDER BY e.IssueDate DESC, e.Id, ei.LineNo
```

#### jp_overdue_invoices.sql パターン
```sql
SELECT inv.InvoiceNo, cu.Name AS CustomerName,
       inv.Title, inv.RegistrationNo,
       inv.IssueDate, inv.DueDate,
       ROUND(inv.Total, 0) AS Total,
       ROUND(inv.TaxAmount10, 0) AS TaxAmount10,
       CASE WHEN inv.DueDate < date('now')
            THEN CAST(julianday('now') - julianday(inv.DueDate) AS INTEGER)
            ELSE 0 END AS OverdueDays,
       inv.BankName, inv.AccountNo, inv.PreparedBy
FROM JpInvoice inv
JOIN Customer cu ON cu.Id = inv.CustomerId
WHERE inv.Status IN ('issued', 'overdue')
ORDER BY OverdueDays DESC
```

#### jp_contract_expiry_warning.sql パターン
```sql
SELECT c.ContractNo, cu.Name AS CustomerName,
       c.Title, c.ContractType, c.EndDate,
       CAST(julianday(c.EndDate) - julianday('now') AS INTEGER) AS DaysUntilExpiry,
       CASE WHEN c.AutoRenew = 1 THEN 'あり' ELSE 'なし' END AS AutoRenew,
       CASE WHEN c.IsElectronic = 1 THEN '電子' ELSE '紙' END AS ContractForm,
       c.JurisdictionCourt, c.Status
FROM JpContract c
JOIN Customer cu ON cu.Id = c.CustomerId
WHERE c.Status = 'active'
  AND c.EndDate BETWEEN date('now') AND date('now', '+90 days')
ORDER BY DaysUntilExpiry ASC
```

---

### 4. PDF エクスポート定義パターン

#### 請求書（インボイス対応）
```yaml
invoice_pdf:
  label: "請求書 PDF"
  format: pdf
  filename: "請求書_{date:yyyyMMdd}.pdf"
  pdf:
    title: "請求書"
    pageSize: A4
    orientation: portrait
    headerColor: "#0F4C75"
    oddRowColor: "#E8F4FD"
    showPageNumbers: true
    showGeneratedAt: true
    columns:
      - key: InvoiceNo
        width: 18
      - key: CustomerName
        width: 20
      - key: RegistrationNo     # インボイス登録番号
        width: 18
      - key: Total
        width: 14
        align: right
      - key: TaxAmount10        # 10%消費税
        width: 14
        align: right
```

#### 期限警告（契約書）
```yaml
expiry_warning_pdf:
  label: "期限切れ警告 PDF"
  format: pdf
  sqlFile: exports/sql/jp_contract_expiry_warning.sql
  pdf:
    title: "契約期限切れ警告（90日以内）"
    headerColor: "#92400E"   # アンバー（警告色）
    oddRowColor: "#FFFBEB"
    columns:
      - key: DaysUntilExpiry
        width: 11
        align: right
      - key: ContractForm       # 電子 or 紙
        width: 9
        align: center
```

---

## 5. 税額計算ルール

| 計算式 | 説明 |
|--------|------|
| `TaxAmount10 = FLOOR(Subtotal10 * 0.10)` | 10%消費税（切り捨て） |
| `TaxAmount8 = FLOOR(Subtotal8 * 0.08)` | 8%消費税（切り捨て） |
| `TaxAmount = TaxAmount10 + TaxAmount8` | 消費税合計 |
| `Total = Subtotal + TaxAmount` | 税込合計 |

## 6. 参照ドキュメント

- 詳細説明: `docs/jp-business-docs.md`
- インボイス制度: 国税庁 https://www.nta.go.jp/taxes/shiraberu/zeimokubetsu/shohi/keigenzeiritsu/invoice.htm
- 電子帳簿保存法: 国税庁 https://www.nta.go.jp/law/joho-zeikaishaku/sonota/jirei/index.htm
