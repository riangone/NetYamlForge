-- JPiere Contract Service (JPCS) — Database Schema & Test Data
-- Target: SQLite

-- ============================================================
-- 1. business_partners
-- ============================================================
CREATE TABLE IF NOT EXISTS business_partners (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    code            TEXT NOT NULL UNIQUE,
    name            TEXT NOT NULL,
    name2           TEXT,
    bp_type         TEXT NOT NULL DEFAULT 'C',
    is_customer     INTEGER NOT NULL DEFAULT 1,
    is_vendor       INTEGER NOT NULL DEFAULT 0,
    tax_id          TEXT,
    url             TEXT,
    phone           TEXT,
    email           TEXT,
    address1        TEXT,
    address2        TEXT,
    city            TEXT,
    postal_code     TEXT,
    credit_limit    REAL DEFAULT 0,
    payment_rule    TEXT DEFAULT 'T',
    payment_term_days INTEGER DEFAULT 30,
    description     TEXT,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 2. product_categories
-- ============================================================
CREATE TABLE IF NOT EXISTS product_categories (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    code        TEXT NOT NULL UNIQUE,
    name        TEXT NOT NULL,
    parent_id   INTEGER REFERENCES product_categories(id),
    description TEXT,
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 3. products
-- ============================================================
CREATE TABLE IF NOT EXISTS products (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    code                TEXT NOT NULL UNIQUE,
    name                TEXT NOT NULL,
    description         TEXT,
    product_category_id INTEGER NOT NULL REFERENCES product_categories(id),
    uom                 TEXT NOT NULL DEFAULT 'EA',
    product_type        TEXT NOT NULL DEFAULT 'I',
    is_purchased        INTEGER NOT NULL DEFAULT 1,
    is_sold             INTEGER NOT NULL DEFAULT 1,
    list_price          REAL DEFAULT 0,
    std_price           REAL DEFAULT 0,
    cost_price          REAL DEFAULT 0,
    tax_rate            REAL DEFAULT 0.10,
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 4. contract_categories
-- ============================================================
CREATE TABLE IF NOT EXISTS contract_categories (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    code        TEXT NOT NULL UNIQUE,
    name        TEXT NOT NULL,
    parent_id   INTEGER REFERENCES contract_categories(id),
    description TEXT,
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 5. contract_templates
-- ============================================================
CREATE TABLE IF NOT EXISTS contract_templates (
    id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    code                        TEXT NOT NULL UNIQUE,
    name                        TEXT NOT NULL,
    contract_type               TEXT NOT NULL DEFAULT 'PUR',
    contract_category_id        INTEGER NOT NULL REFERENCES contract_categories(id),
    description                 TEXT,
    default_payment_term_days   INTEGER DEFAULT 30,
    auto_renewal                INTEGER DEFAULT 0,
    is_active                   INTEGER NOT NULL DEFAULT 1,
    created_at                  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at                  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 6. contracts
-- ============================================================
CREATE TABLE IF NOT EXISTS contracts (
    id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no                 TEXT NOT NULL UNIQUE,
    name                        TEXT NOT NULL,
    contract_type               TEXT NOT NULL DEFAULT 'SAL',
    doc_status                  TEXT NOT NULL DEFAULT 'DR',
    contract_status             TEXT NOT NULL DEFAULT 'WP',
    contract_category_id        INTEGER REFERENCES contract_categories(id),
    contract_template_id        INTEGER REFERENCES contract_templates(id),
    business_partner_id         INTEGER NOT NULL REFERENCES business_partners(id),
    sales_rep                   TEXT,
    date_acct                   TEXT NOT NULL,
    period_date_from            TEXT NOT NULL,
    period_date_to              TEXT,
    auto_renewal                INTEGER DEFAULT 0,
    cancel_deadline             TEXT,
    cancel_date                 TEXT,
    cancel_cause                TEXT,
    monthly_revenue_amt         REAL DEFAULT 0,
    monthly_expense_amt         REAL DEFAULT 0,
    total_doc_amt               REAL DEFAULT 0,
    currency                    TEXT NOT NULL DEFAULT 'JPY',
    description                 TEXT,
    is_active                   INTEGER NOT NULL DEFAULT 1,
    created_at                  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at                  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 7. contract_lines
-- ============================================================
CREATE TABLE IF NOT EXISTS contract_lines (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    contract_id         INTEGER NOT NULL REFERENCES contracts(id),
    line_no             INTEGER NOT NULL,
    product_id          INTEGER REFERENCES products(id),
    description         TEXT,
    qty                 REAL NOT NULL DEFAULT 1,
    uom                 TEXT DEFAULT 'EA',
    unit_price          REAL NOT NULL DEFAULT 0,
    line_amt            REAL NOT NULL DEFAULT 0,
    tax_rate            REAL DEFAULT 0.10,
    tax_amt             REAL DEFAULT 0,
    billing_policy      TEXT DEFAULT 'M',
    billing_start_date  TEXT,
    billing_end_date    TEXT,
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 8. estimations
-- ============================================================
CREATE TABLE IF NOT EXISTS estimations (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,
    estimation_date     TEXT NOT NULL DEFAULT (date('now')),
    version             INTEGER NOT NULL DEFAULT 1,
    doc_status          TEXT NOT NULL DEFAULT 'DR',
    is_so_trx           INTEGER NOT NULL DEFAULT 1,
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    sales_rep           TEXT,
    date_promised       TEXT,
    currency            TEXT NOT NULL DEFAULT 'JPY',
    total_lines         REAL DEFAULT 0,
    grand_total         REAL DEFAULT 0,
    tax_base_amt        REAL DEFAULT 0,
    tax_amt             REAL DEFAULT 0,
    description         TEXT,
    linked_contract_id  INTEGER REFERENCES contracts(id),
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 9. estimation_lines
-- ============================================================
CREATE TABLE IF NOT EXISTS estimation_lines (
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
    discount        REAL DEFAULT 0,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 10. bills
-- ============================================================
CREATE TABLE IF NOT EXISTS bills (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,
    doc_status          TEXT NOT NULL DEFAULT 'DR',
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    date_billed         TEXT NOT NULL DEFAULT (date('now')),
    date_due            TEXT,
    date_sent           TEXT,
    payment_rule        TEXT DEFAULT 'T',
    payment_term_days   INTEGER DEFAULT 30,
    currency            TEXT NOT NULL DEFAULT 'JPY',
    total_lines         REAL DEFAULT 0,
    grand_total         REAL DEFAULT 0,
    tax_base_amt        REAL DEFAULT 0,
    tax_amt             REAL DEFAULT 0,
    pay_amt             REAL DEFAULT 0,
    outstanding_amt     REAL DEFAULT 0,
    description         TEXT,
    linked_contract_id  INTEGER REFERENCES contracts(id),
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 11. bill_lines
-- ============================================================
CREATE TABLE IF NOT EXISTS bill_lines (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id     INTEGER NOT NULL REFERENCES bills(id),
    line_no     INTEGER NOT NULL,
    description TEXT,
    period_from TEXT,
    period_to   TEXT,
    total_lines REAL DEFAULT 0,
    grand_total REAL DEFAULT 0,
    tax_amt     REAL DEFAULT 0,
    pay_amt     REAL DEFAULT 0,
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 12. recognitions
-- ============================================================
CREATE TABLE IF NOT EXISTS recognitions (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,
    doc_status          TEXT NOT NULL DEFAULT 'DR',
    is_so_trx           INTEGER NOT NULL DEFAULT 1,
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    date_acct           TEXT NOT NULL,
    grand_total         REAL NOT NULL DEFAULT 0,
    description         TEXT,
    linked_contract_id  INTEGER REFERENCES contracts(id),
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 13. recognition_lines
-- ============================================================
CREATE TABLE IF NOT EXISTS recognition_lines (
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

-- ============================================================
-- 14. todo_categories
-- ============================================================
CREATE TABLE IF NOT EXISTS todo_categories (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT NOT NULL,
    description TEXT,
    color       TEXT DEFAULT '#3498db',
    is_active   INTEGER NOT NULL DEFAULT 1,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 15. todos
-- ============================================================
CREATE TABLE IF NOT EXISTS todos (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    title               TEXT NOT NULL,
    description         TEXT,
    todo_type           TEXT NOT NULL DEFAULT 'T',
    todo_status         TEXT NOT NULL DEFAULT 'NY',
    todo_category_id    INTEGER REFERENCES todo_categories(id),
    assigned_to         TEXT,
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

-- ============================================================
-- 16. accounts (勘定科目マスタ)
-- ============================================================
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

-- ============================================================
-- 17. journals (仕訳ヘッダ)
-- ============================================================
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

-- ============================================================
-- 18. journal_lines (仕訳明細)
-- ============================================================
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

-- ============================================================
-- 19. purchase_orders (発注書ヘッダ)
-- ============================================================
CREATE TABLE IF NOT EXISTS purchase_orders (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,   -- PO-YYYYMM-XXXX
    doc_status          TEXT NOT NULL DEFAULT 'DR',
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    date_ordered        TEXT NOT NULL,
    date_promised       TEXT,
    total_lines         REAL DEFAULT 0,
    grand_total         REAL DEFAULT 0,
    tax_amt             REAL DEFAULT 0,
    approved_by         TEXT,
    approved_at         TEXT,
    approval_status     TEXT DEFAULT 'PENDING',
    linked_contract_id  INTEGER REFERENCES contracts(id),
    description         TEXT,
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 20. purchase_order_lines (発注明細)
-- ============================================================
CREATE TABLE IF NOT EXISTS purchase_order_lines (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    purchase_order_id INTEGER NOT NULL REFERENCES purchase_orders(id),
    line_no         INTEGER NOT NULL,
    product_id      INTEGER REFERENCES products(id),
    description     TEXT,
    qty_ordered     REAL NOT NULL DEFAULT 1,
    uom             TEXT DEFAULT 'EA',
    unit_price      REAL NOT NULL DEFAULT 0,
    line_amt        REAL NOT NULL DEFAULT 0,
    qty_received    REAL DEFAULT 0,
    tax_rate        REAL DEFAULT 0.10,
    tax_amt         REAL DEFAULT 0,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 21. purchase_receipts (受入処理ヘッダ)
-- ============================================================
CREATE TABLE IF NOT EXISTS purchase_receipts (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,   -- REC-YYYYMM-XXXX
    doc_status          TEXT NOT NULL DEFAULT 'DR',
    purchase_order_id   INTEGER NOT NULL REFERENCES purchase_orders(id),
    date_received       TEXT NOT NULL,
    total_received_amt  REAL DEFAULT 0,
    description         TEXT,
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 22. purchase_receipt_lines (受入明細)
-- ============================================================
CREATE TABLE IF NOT EXISTS purchase_receipt_lines (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    receipt_id      INTEGER NOT NULL REFERENCES purchase_receipts(id),
    po_line_id      INTEGER REFERENCES purchase_order_lines(id),
    product_id      INTEGER REFERENCES products(id),
    qty_received    REAL NOT NULL DEFAULT 1,
    unit_cost       REAL DEFAULT 0,
    line_amt        REAL DEFAULT 0,
    description     TEXT,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 23. ap_invoices (仕入請求書ヘッダ)
-- ============================================================
CREATE TABLE IF NOT EXISTS ap_invoices (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,   -- APBILL-YYYYMM-XXXX
    doc_status          TEXT NOT NULL DEFAULT 'DR',
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    purchase_order_id   INTEGER REFERENCES purchase_orders(id),
    date_invoiced       TEXT,
    date_due            TEXT,
    total_lines         REAL DEFAULT 0,
    grand_total         REAL DEFAULT 0,
    tax_base_amt        REAL DEFAULT 0,
    tax_amt             REAL DEFAULT 0,
    pay_amt             REAL DEFAULT 0,
    outstanding_amt     REAL DEFAULT 0,
    description         TEXT,
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 24. ap_invoice_lines (仕入請求明細)
-- ============================================================
CREATE TABLE IF NOT EXISTS ap_invoice_lines (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ap_invoice_id   INTEGER NOT NULL REFERENCES ap_invoices(id),
    line_no         INTEGER NOT NULL,
    product_id      INTEGER REFERENCES products(id),
    description     TEXT,
    qty             REAL NOT NULL DEFAULT 1,
    unit_price      REAL NOT NULL DEFAULT 0,
    line_amt        REAL NOT NULL DEFAULT 0,
    tax_rate        REAL DEFAULT 0.10,
    tax_amt         REAL DEFAULT 0,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 25. payments (入金/支払処理)
-- ============================================================
CREATE TABLE IF NOT EXISTS payments (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    document_no         TEXT NOT NULL UNIQUE,   -- PAY-YYYYMM-XXXX
    payment_type        TEXT NOT NULL,          -- AR=入金, AP=支払
    doc_status          TEXT NOT NULL DEFAULT 'DR',
    business_partner_id INTEGER NOT NULL REFERENCES business_partners(id),
    payment_date        TEXT NOT NULL,
    payment_method      TEXT,                   -- T=銀行振込, C=クレジット, CH=小切手
    pay_amt             REAL NOT NULL DEFAULT 0,
    bill_id             INTEGER REFERENCES bills(id),
    ap_invoice_id       INTEGER REFERENCES ap_invoices(id),
    description         TEXT,
    is_active           INTEGER NOT NULL DEFAULT 1,
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at          TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 26. stock_moves (在庫移動)
-- ============================================================
CREATE TABLE IF NOT EXISTS stock_moves (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    move_type       TEXT NOT NULL,      -- IN=入庫, OUT=出庫, TF=転送
    product_id      INTEGER NOT NULL REFERENCES products(id),
    qty             REAL NOT NULL DEFAULT 0,
    unit_cost       REAL DEFAULT 0,
    date_moved      TEXT NOT NULL,
    source_table    TEXT,               -- purchase_receipts, shipments, etc.
    source_id       INTEGER,
    description     TEXT,
    is_active       INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 27. approval_requests (承認申請)
-- ============================================================
CREATE TABLE IF NOT EXISTS approval_requests (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    source_table    TEXT NOT NULL,      -- purchase_orders, contracts, etc.
    source_id       INTEGER NOT NULL,
    requester       TEXT,
    current_step    INTEGER DEFAULT 1,
    total_steps     INTEGER DEFAULT 1,
    status          TEXT NOT NULL DEFAULT 'PENDING',  -- PENDING / APPROVED / REJECTED / CANCELLED
    grand_total     REAL DEFAULT 0,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

-- ============================================================
-- 28. approval_steps (承認ステップ)
-- ============================================================
CREATE TABLE IF NOT EXISTS approval_steps (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id      INTEGER NOT NULL REFERENCES approval_requests(id),
    step_no         INTEGER NOT NULL,
    approver        TEXT,
    approver_role   TEXT,
    label           TEXT,
    status          TEXT NOT NULL DEFAULT 'PENDING',  -- PENDING / APPROVED / REJECTED
    approved_at     TEXT,
    comments        TEXT,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);


-- ============================================================
-- TEST DATA — Master
-- ============================================================

-- accounts (勘定科目マスタ — iDempiere 準拠)
INSERT OR IGNORE INTO accounts (id, code, name, account_type, normal_balance) VALUES
-- 資産
(1, '1000', '流動資産合計',      'A', 'D'),
(2, '1100', '売掛金',            'A', 'D'),
(3, '1110', '完成工事未収入金',  'A', 'D'),
(4, '1200', '棚卸資産',          'A', 'D'),
(5, '1900', '現金及び預金',      'A', 'D'),
-- 負債
(6, '2000', '流動負債合計',      'L', 'C'),
(7, '2100', '買掛金',            'L', 'C'),
(8, '2300', '前受金',            'L', 'C'),
(9, '2400', '仮受消費税',        'L', 'C'),
(10, '2410', '仮払消費税',       'A', 'D'),
-- 収益
(11, '4000', '売上高合計',       'R', 'C'),
(12, '4100', 'サービス売上高',   'R', 'C'),
(13, '4200', '商品売上高',       'R', 'C'),
-- 費用
(14, '5000', '売上原価',         'X', 'D'),
(15, '5100', '仕入高',           'X', 'D'),
(16, '6000', '販売管理費',       'X', 'D'),
(17, '6100', '外注費',           'X', 'D');

-- product_categories (5件)
INSERT OR IGNORE INTO product_categories (id, code, name, parent_id, description) VALUES
(1, 'SW', 'ソフトウェア', NULL, 'ソフトウェア製品'),
(2, 'HW', 'ハードウェア', NULL, 'ハードウェア機器'),
(3, 'SI', 'SIサービス', NULL, 'システム開発・導入支援'),
(4, 'MAINT', '保守サービス', NULL, '年間保守契約'),
(5, 'OTHER', 'その他', NULL, 'その他の製品');

-- products (15件)
INSERT OR IGNORE INTO products (id, code, name, description, product_category_id, uom, product_type, list_price, std_price, cost_price, tax_rate) VALUES
(1, 'PROD-ERP-001', '基幹システムライセンス', 'ERPライセンス（1ユーザ）', 1, 'LIC', 'S', 150000, 120000, 80000, 0.10),
(2, 'PROD-CLD-001', 'クラウドストレージ（1TB/月）', '月額サブスクリプション', 1, 'MO', 'S', 5000, 5000, 2000, 0.10),
(3, 'PROD-SVR-001', '物理サーバ Xeon 64GB', 'ラックマウント型 2U', 2, 'EA', 'I', 850000, 750000, 500000, 0.10),
(4, 'PROD-SW-002', 'DBライセンス（コア単位）', 'RDBMSエンタープライズ版', 1, 'LIC', 'S', 200000, 180000, 100000, 0.10),
(5, 'PROD-SI-001', 'ERP導入支援（人日）', '要件定義〜構築', 3, 'MD', 'S', 120000, 100000, 70000, 0.10),
(6, 'PROD-SI-002', 'カスタム開発（人日）', '機能追加・改修', 3, 'MD', 'S', 100000, 90000, 60000, 0.10),
(7, 'PROD-MA-001', '年間保守契約（ERP）', '電話・リモートサポート', 4, 'YR', 'S', 300000, 280000, 150000, 0.10),
(8, 'PROD-MA-002', 'サーバ保守（年間）', 'ハードウェア交換含む', 4, 'YR', 'S', 120000, 100000, 60000, 0.10),
(9, 'PROD-NW-001', 'ネットワークスイッチ 48ポート', 'L3マネージドスイッチ', 2, 'EA', 'I', 250000, 220000, 150000, 0.10),
(10, 'PROD-BK-001', 'バックアップソリューション', '日次バックアップ設定', 1, 'MO', 'S', 15000, 15000, 5000, 0.10),
(11, 'PROD-SI-003', '要件定義（人日）', '業務フロー整理', 3, 'MD', 'S', 110000, 95000, 65000, 0.10),
(12, 'PROD-SEC-001', 'セキュリティ監視（月額）', '24時間監視レポート', 4, 'MO', 'S', 80000, 70000, 35000, 0.10),
(13, 'PROD-TRN-001', 'ユーザトレーニング（日）', '操作研修', 3, 'DAY', 'S', 90000, 80000, 50000, 0.10),
(14, 'PROD-DB-001', 'DBチューニング（一式）', 'パフォーマン解析〜改善', 3, 'LS', 'S', 500000, 450000, 300000, 0.10),
(15, 'PROD-OT-001', '消耗品（セット）', 'ケーブル等付属品', 5, 'SET', 'I', 5000, 5000, 3000, 0.10);

-- business_partners (10件)
INSERT OR IGNORE INTO business_partners (id, code, name, name2, bp_type, is_customer, is_vendor, tax_id, phone, email, address1, city, postal_code, credit_limit, payment_term_days, description) VALUES
(1, 'BP-001', '株式会社アルファテック', 'カブシキガイシャアルファテック', 'C', 1, 0, '1010001000001', '03-1234-5678', 'info@alphatech.co.jp', '港区芝公園1-2-3', '東京都', '105-0011', 5000000, 30, '主要顧客—ERP導入'),
(2, 'BP-002', 'ベータソリューションズ株式会社', 'ベータソリューションズカブシキガイシャ', 'C', 1, 0, '2020001000002', '03-2345-6789', 'contact@beta-sol.co.jp', '新宿区西新宿2-3-4', '東京都', '160-0023', 3000000, 30, 'クラウド移行プロジェクト'),
(3, 'BP-003', 'ガンマ商事株式会社', 'ガンマショウジカブシキガイシャ', 'V', 0, 1, '3030001000003', '06-3456-7890', 'purchase@gamma-shoji.co.jp', '北区梅田3-4-5', '大阪市', '530-0001', 0, 30, 'ハードウェア仕入先'),
(4, 'BP-004', 'デルシステム開発株式会社', 'デルシステムカイハツカブシキガイシャ', 'B', 1, 1, '4040001000004', '052-4567-8901', 'info@del-sys.co.jp', '中区栄4-5-6', '名古屋市', '460-0008', 2000000, 30, '共同開発パートナー'),
(5, 'BP-005', 'イプシロン電子株式会社', 'イプシロンデンシカブシキガイシャ', 'V', 0, 1, '5050001000005', '022-5678-9012', 'order@epsilon-densi.co.jp', '青葉区中央5-6-7', '仙台市', '980-0021', 0, 30, 'ネットワーク機器仕入先'),
(6, 'BP-006', 'ゼータコンサルティング合同会社', 'ゼータコンサルティングゴウドウガイシャ', 'C', 1, 0, '6060001000006', '03-6789-0123', 'sales@zeta-consulting.jp', '渋谷区恵比寿6-7-8', '東京都', '150-0013', 1000000, 30, 'コンサル紹介案件'),
(7, 'BP-007', 'エータ印刷株式会社', 'エータインサツカブシキガイシャ', 'C', 1, 0, '7070001000007', '045-7890-1234', 'info@eta-print.co.jp', '西区みなとみらい7-8-9', '横浜市', '220-0012', 800000, 30, '帳票システム刷新'),
(8, 'BP-008', 'シータ物流株式会社', 'シータブツリュウカブシキガイシャ', 'C', 1, 0, '8080001000008', '075-8901-2345', 'system@theta-logi.co.jp', '下京区烏丸8-9-0', '京都市', '600-8216', 4000000, 30, 'WMS導入'),
(9, 'BP-009', 'アイオタ食品株式会社', 'アイオタショクヒンカブシキガイシャ', 'B', 1, 1, '9090001000009', '092-9012-3456', 'dev@iota-foods.co.jp', '博多区博多駅東9-0-1', '福岡市', '812-0013', 1500000, 30, '受注・仕入両方'),
(10, 'BP-010', 'カッパメディカル株式会社', 'カッパメディカルカブシキガイシャ', 'C', 1, 0, '1010001000010', '03-0123-4567', 'info@kappa-medical.co.jp', '千代田区大手町1-0-2', '東京都', '100-0004', 6000000, 30, '医療機関向けERP');

-- contract_categories (3件)
INSERT OR IGNORE INTO contract_categories (id, code, name, parent_id, description) VALUES
(1, 'SW-LIC', 'ソフトウェアライセンス契約', NULL, '製品ライセンスの契約'),
(2, 'SI-DEV', 'SI・開発契約', NULL, 'システム開発・導入契約'),
(3, 'MAINT', '保守・運用契約', NULL, '年間保守・運用委託契約');

-- contract_templates (3件)
INSERT OR IGNORE INTO contract_templates (id, code, name, contract_type, contract_category_id, description, default_payment_term_days, auto_renewal) VALUES
(1, 'TPL-SW-001', '標準ソフトウェア販売契約', 'SAL', 1, 'ライセンス販売の標準契約', 30, 1),
(2, 'TPL-SI-001', 'システム開発請負契約', 'SAL', 2, 'カスタム開発の標準請負契約', 30, 0),
(3, 'TPL-MA-001', '年間保守契約', 'SAL', 3, '標準保守契約（自動更新）', 30, 1);


-- ============================================================
-- TEST DATA — Transactions
-- ============================================================

-- contracts (8件)
INSERT OR IGNORE INTO contracts (id, document_no, name, contract_type, doc_status, contract_status, contract_category_id, business_partner_id, sales_rep, date_acct, period_date_from, period_date_to, auto_renewal, monthly_revenue_amt, total_doc_amt, currency, description) VALUES
(1, 'CON-202504-0001', 'アルファテック ERP導入契約', 'SAL', 'CO', 'AC', 2, 1, '山田太郎', '2025-04-01', '2025-04-01', '2026-03-31', 0, 2500000, 30000000, 'JPY', '基幹システム刷新プロジェクト'),
(2, 'CON-202504-0002', 'ベータ ソリューションズ クラウド契約', 'SAL', 'CO', 'AC', 1, 2, '佐藤花子', '2025-05-01', '2025-05-01', '2026-04-30', 1, 150000, 1800000, 'JPY', 'クラウドストレージ年間契約'),
(3, 'CON-202506-0003', 'ガンマ商事 ハードウェア購買契約', 'PUR', 'CO', 'AC', 1, 3, '鈴木一郎', '2025-06-01', '2025-06-01', '2026-05-31', 0, 500000, 6000000, 'JPY', 'サーバ機器年間購買'),
(4, 'CON-202504-0004', 'ゼータコンサルティング 保守契約', 'SAL', 'CO', 'AC', 3, 6, '山田太郎', '2025-04-01', '2025-04-01', '2025-09-30', 1, 80000, 480000, 'JPY', '半年保守（6月以内期限切れ）'),
(5, 'CON-202507-0005', 'シータ物流 WMS開発契約', 'SAL', 'CO', 'AC', 2, 8, '佐藤花子', '2025-07-01', '2025-07-01', '2026-06-30', 0, 3000000, 36000000, 'JPY', '倉庫管理システム構築'),
(6, 'CON-202604-0006', 'デルシステム 共同開発契約', 'SAL', 'IN', 'WP', 2, 4, '鈴木一郎', '2026-04-01', '2026-04-01', '2026-09-30', 0, 0, 0, 'JPY', '交渉中案件'),
(7, 'CON-202604-0007', 'イプシロン ネットワーク構築', 'SAL', 'IN', 'WP', 2, 5, '山田太郎', '2026-04-01', '2026-05-01', NULL, 0, 0, 0, 'JPY', '交渉中—期間未定'),
(8, 'CON-202404-0008', '旧カッパメディカル 解約済み契約', 'SAL', 'CL', 'CA', 3, 10, '佐藤花子', '2024-04-01', '2024-04-01', '2025-03-31', 0, 0, 1200000, 'JPY', '2025年3月解約');

-- contract_lines (15件)
INSERT OR IGNORE INTO contract_lines (id, contract_id, line_no, product_id, description, qty, uom, unit_price, line_amt, tax_rate, tax_amt, billing_policy, billing_start_date, billing_end_date) VALUES
(1, 1, 10, 5, 'ERP導入支援（要件定義〜）', 60, 'MD', 100000, 6000000, 0.10, 600000, 'M', '2025-04-01', '2025-09-30'),
(2, 1, 20, 6, 'カスタム開発', 80, 'MD', 90000, 7200000, 0.10, 720000, 'M', '2025-07-01', '2026-02-28'),
(3, 1, 30, 13, 'ユーザトレーニング', 10, 'DAY', 80000, 800000, 0.10, 80000, 'O', '2025-10-01', '2025-10-31'),
(4, 1, 40, 7, '年間保守', 1, 'YR', 280000, 280000, 0.10, 28000, 'O', '2025-10-01', '2026-09-30'),
(5, 2, 10, 2, 'クラウドストレージ 1TB/月', 12, 'MO', 5000, 60000, 0.10, 6000, 'M', '2025-05-01', '2026-04-30'),
(6, 2, 20, 10, 'バックアップソリューション', 12, 'MO', 15000, 180000, 0.10, 18000, 'M', '2025-05-01', '2026-04-30'),
(7, 2, 30, 12, 'セキュリティ監視', 12, 'MO', 70000, 840000, 0.10, 84000, 'M', '2025-05-01', '2026-04-30'),
(8, 3, 10, 3, '物理サーバ Xeon 64GB', 4, 'EA', 750000, 3000000, 0.10, 300000, 'M', '2025-06-01', '2026-05-31'),
(9, 3, 20, 9, 'NWスイッチ 48ポート', 4, 'EA', 220000, 880000, 0.10, 88000, 'M', '2025-06-01', '2026-05-31'),
(10, 4, 10, 7, '年間保守契約（ERP）', 1, 'YR', 280000, 280000, 0.10, 28000, 'M', '2025-04-01', '2025-09-30'),
(11, 4, 20, 8, 'サーバ保守（年間）', 1, 'YR', 100000, 100000, 0.10, 10000, 'M', '2025-04-01', '2025-09-30'),
(12, 5, 10, 5, 'WMS導入支援', 100, 'MD', 100000, 10000000, 0.10, 1000000, 'M', '2025-07-01', '2026-03-31'),
(13, 5, 20, 6, 'WMSカスタム開発', 120, 'MD', 90000, 10800000, 0.10, 1080000, 'M', '2025-09-01', '2026-06-30'),
(14, 5, 30, 14, 'DBチューニング', 1, 'LS', 450000, 450000, 0.10, 45000, 'O', '2026-01-01', '2026-01-31'),
(15, 5, 40, 13, 'ユーザトレーニング', 20, 'DAY', 80000, 1600000, 0.10, 160000, 'O', '2026-04-01', '2026-04-30');

-- estimations (5件)
INSERT OR IGNORE INTO estimations (id, document_no, estimation_date, version, doc_status, is_so_trx, business_partner_id, sales_rep, date_promised, currency, total_lines, grand_total, tax_base_amt, tax_amt, description, linked_contract_id) VALUES
(1, 'EST-202603-0001', '2026-03-15', 1, 'DR', 1, 1, '山田太郎', '2026-06-30', 'JPY', 1800000, 1980000, 1800000, 180000, 'ERP追加機能開発—下書き', NULL),
(2, 'EST-202603-0002', '2026-03-20', 1, 'DR', 1, 7, '佐藤花子', '2026-07-31', 'JPY', 560000, 616000, 560000, 56000, '帳票印刷モジュール追加—下書き', NULL),
(3, 'EST-202604-0003', '2026-04-01', 2, 'IN', 1, 2, '鈴木一郎', '2026-09-30', 'JPY', 2400000, 2640000, 2400000, 240000, 'クラウド容量増強—提出済み', NULL),
(4, 'EST-202604-0004', '2026-04-05', 1, 'IN', 1, 6, '山田太郎', '2026-08-31', 'JPY', 950000, 1045000, 950000, 95000, 'コンサル追加支援—提出済み', NULL),
(5, 'EST-202602-0005', '2026-02-10', 1, 'CO', 1, 8, '佐藤花子', '2026-07-01', 'JPY', 5000000, 5500000, 5000000, 500000, 'WMS第2期—受注確定', 5);

-- estimation_lines (12件)
INSERT OR IGNORE INTO estimation_lines (id, estimation_id, line_no, product_id, description, date_ordered, date_promised, qty_ordered, uom, unit_price, line_amt, tax_rate, tax_amt, discount) VALUES
(1, 1, 10, 6, '追加機能開発', '2026-03-15', '2026-06-30', 10, 'MD', 90000, 900000, 0.10, 90000, 0),
(2, 1, 20, 11, '業務フロー整理', '2026-03-15', '2026-05-31', 5, 'MD', 95000, 475000, 0.10, 47500, 0),
(3, 1, 30, 13, '操作研修', '2026-03-15', '2026-06-30', 5, 'DAY', 80000, 400000, 0.10, 40000, 5),
(4, 2, 10, 7, '帳票保守（年間）', '2026-03-20', '2026-07-31', 1, 'YR', 280000, 280000, 0.10, 28000, 0),
(5, 2, 20, 13, '研修2日', '2026-03-20', '2026-07-31', 2, 'DAY', 80000, 160000, 0.10, 16000, 0),
(6, 2, 30, 15, '消耗品セット', '2026-03-20', '2026-07-31', 24, 'SET', 5000, 120000, 0.10, 12000, 0),
(7, 3, 10, 2, 'クラウド容量 2TB/月', '2026-04-01', '2026-09-30', 6, 'MO', 10000, 60000, 0.10, 6000, 0),
(8, 3, 20, 10, 'バックアップ増強', '2026-04-01', '2026-09-30', 6, 'MO', 30000, 180000, 0.10, 18000, 0),
(9, 3, 30, 12, 'セキュリティ監視', '2026-04-01', '2026-09-30', 6, 'MO', 70000, 420000, 0.10, 42000, 0),
(10, 3, 40, 14, 'DBチューニング', '2026-04-01', '2026-08-31', 1, 'LS', 450000, 450000, 0.10, 45000, 0),
(11, 3, 50, 5, '導入支援', '2026-04-01', '2026-09-30', 10, 'MD', 100000, 1000000, 0.10, 100000, 10),
(12, 5, 10, 5, 'WMS第2期導入支援', '2026-02-10', '2026-07-01', 50, 'MD', 100000, 5000000, 0.10, 500000, 0);

-- bills (6件)
INSERT OR IGNORE INTO bills (id, document_no, doc_status, business_partner_id, date_billed, date_due, date_sent, payment_rule, payment_term_days, currency, total_lines, grand_total, tax_base_amt, tax_amt, pay_amt, outstanding_amt, description, linked_contract_id) VALUES
(1, 'BILL-202504-0001', 'CO', 1, '2025-04-30', '2025-05-30', '2025-04-30', 'T', 30, 'JPY', 909090, 999999, 909090, 90909, 500000, 499999, 'ERP導入—第1回請求', 1),
(2, 'BILL-202505-0002', 'CO', 2, '2025-05-31', '2025-06-30', '2025-05-31', 'T', 30, 'JPY', 136363, 149999, 136363, 13636, 149999, 0, 'クラウド月額請求（5月分）', 2),
(3, 'BILL-202506-0003', 'CO', 3, '2025-06-30', '2025-07-30', '2025-06-30', 'T', 30, 'JPY', 3500000, 3850000, 3500000, 350000, 2000000, 1850000, 'HW購買—一部入金', 3),
(4, 'BILL-202604-0004', 'CO', 8, '2026-04-01', '2026-04-30', '2026-04-01', 'T', 30, 'JPY', 2727272, 2999999, 2727272, 272727, 0, 2999999, 'WMS開発—第1回請求', 5),
(5, 'BILL-202604-0005', 'DR', 1, '2026-04-05', NULL, NULL, 'T', 30, 'JPY', 1818181, 1999999, 1818181, 181818, 0, 1999999, 'ERP追加機能—下書き', NULL),
(6, 'BILL-202604-0006', 'DR', 7, '2026-04-05', NULL, NULL, 'T', 30, 'JPY', 454545, 499999, 454545, 45454, 0, 499999, '帳票モジュール—下書き', NULL);

-- bill_lines (8件)
INSERT OR IGNORE INTO bill_lines (id, bill_id, line_no, description, period_from, period_to, total_lines, grand_total, tax_amt, pay_amt) VALUES
(1, 1, 10, 'ERP導入支援（4月分）', '2025-04-01', '2025-04-30', 454545, 499999, 45454, 250000),
(2, 1, 20, '要件定義（4月分）', '2025-04-01', '2025-04-30', 454545, 500000, 45455, 250000),
(3, 2, 10, 'クラウドストレージ+バックアップ+監視', '2025-05-01', '2025-05-31', 136363, 149999, 13636, 149999),
(4, 3, 10, 'HW購買—第1回', '2025-06-01', '2025-06-30', 3500000, 3850000, 350000, 2000000),
(5, 4, 10, 'WMS開発—マイルストーン1', '2025-07-01', '2025-09-30', 1363636, 1499999, 136363, 0),
(6, 4, 20, 'WMS開発—マイルストーン2', '2025-10-01', '2025-12-31', 1363636, 1500000, 136364, 0),
(7, 5, 10, '追加機能開発（請求予定）', '2026-04-01', '2026-06-30', 1818181, 1999999, 181818, 0),
(8, 6, 10, '帳票モジュール（請求予定）', '2026-04-01', '2026-07-31', 454545, 499999, 45454, 0);

-- recognitions (3件)
INSERT OR IGNORE INTO recognitions (id, document_no, doc_status, is_so_trx, business_partner_id, date_acct, grand_total, description, linked_contract_id) VALUES
(1, 'REC-202504-0001', 'CO', 1, 1, '2025-04-30', 999999, 'ERP導入—4月分売上計上', 1),
(2, 'REC-202505-0002', 'CO', 1, 2, '2025-05-31', 149999, 'クラウド月額—5月分売上計上', 2),
(3, 'REC-202604-0003', 'DR', 1, 8, '2026-04-30', 1500000, 'WMS開発—4月分計上予定', 5);

-- recognition_lines (6件)
INSERT OR IGNORE INTO recognition_lines (id, recognition_id, line_no, contract_line_id, product_id, description, qty_recognized, unit_price, line_amt, tax_rate, tax_amt, period_from, period_to) VALUES
(1, 1, 10, 1, 5, 'ERP導入支援（4月分 5MD）', 5, 100000, 500000, 0.10, 50000, '2025-04-01', '2025-04-30'),
(2, 1, 20, 1, 5, 'ERP導入支援（4月分追加 5MD）', 5, 100000, 500000, 0.10, 50000, '2025-04-01', '2025-04-30'),
(3, 2, 10, 5, 2, 'クラウドストレージ（5月分）', 1, 5000, 5000, 0.10, 500, '2025-05-01', '2025-05-31'),
(4, 2, 20, 6, 10, 'バックアップ（5月分）', 1, 15000, 15000, 0.10, 1500, '2025-05-01', '2025-05-31'),
(5, 2, 30, 7, 12, 'セキュリティ監視（5月分）', 1, 70000, 70000, 0.10, 7000, '2025-05-01', '2025-05-31'),
(6, 3, 10, 12, 5, 'WMS導入支援（4月分 15MD）', 15, 100000, 1500000, 0.10, 150000, '2026-04-01', '2026-04-30');

-- todo_categories (5件)
INSERT OR IGNORE INTO todo_categories (id, name, description, color) VALUES
(1, '営業活動', '顧客訪問・商談関連', '#3498db'),
(2, 'プロジェクト', '開発・導入タスク', '#e67e22'),
(3, '保守対応', '問い合わせ・トラブル', '#e74c3c'),
(4, '請求・会計', '請求書発行・計上', '#2ecc71'),
(5, '内部作業', '定例・研修', '#9b59b6');

-- todos (10件)
INSERT OR IGNORE INTO todos (id, title, description, todo_type, todo_status, todo_category_id, assigned_to, scheduled_start, scheduled_end, actual_start, actual_end, linked_contract_id, linked_partner_id) VALUES
(1, 'アルファテック 要件ヒアリング', '業務フロー整理のヒアリング日程調整', 'M', 'DN', 1, '山田太郎', '2025-04-10', '2025-04-10', '2025-04-10 09:00', '2025-04-10 12:00', 1, 1),
(2, 'ベータSOL 契約書最終確認', 'クラウド契約書の法務確認', 'T', 'DN', 2, '佐藤花子', '2025-04-15', '2025-04-20', '2025-04-15 10:00', '2025-04-18 17:00', 2, 2),
(3, 'シータ物流 キックオフMTG', 'WMSプロジェクト キックオフ', 'M', 'IP', 2, '佐藤花子', '2025-07-01', '2025-07-01', '2025-07-01 14:00', NULL, 5, 8),
(4, 'ガンマ商事 納入日程確認', 'サーバ納入日の電話確認', 'C', 'IP', 3, '鈴木一郎', '2026-04-07', '2026-04-07', '2026-04-07 09:30', NULL, 3, 3),
(5, 'ゼータコンサル 4月請求書発行', '保守契約の請求書発行', 'T', 'NY', 4, '経理担当', '2026-04-01', '2026-04-10', NULL, NULL, 4, 6),
(6, 'デルシステム 契約交渉', '共同開発契約の条件交渉', 'M', 'IP', 1, '鈴木一郎', '2026-04-08', '2026-04-15', NULL, NULL, 6, 4),
(7, 'イプシロン電子 見積提出', 'NW構築見積の提出', 'T', 'NY', 1, '山田太郎', '2026-04-10', '2026-04-15', NULL, NULL, 7, 5),
(8, 'カッパメディカル 新規提案', '新規ERP提案資料作成', 'T', 'NY', 1, '佐藤花子', '2026-04-20', '2026-04-30', NULL, NULL, NULL, 10),
(9, '月次売上報告書作成', '2026年3月分売上まとめ', 'T', 'DN', 4, '経理担当', '2026-04-01', '2026-04-05', '2026-04-01 09:00', '2026-04-04 18:00', NULL, NULL),
(10, '社内研修 セキュリティ対策', '新セキュリティポリシー研修', 'M', 'DN', 5, '全員', '2026-03-28', '2026-03-28', '2026-03-28 13:00', '2026-03-28 17:00', NULL, NULL);

-- ============================================================
-- TEST DATA — Accounting (Phase 1)
-- ============================================================

-- journals (3件 — テスト用仕訳)
INSERT OR IGNORE INTO journals (id, document_no, doc_status, journal_type, date_acct, description, source_table, source_id, total_debit, total_credit, is_balanced) VALUES
(1, 'JNL-202504-0001', 'CO', 'AR', '2025-04-30', '請求確定: BILL-202504-0001', 'bills', 1, 999999, 999999, 1),
(2, 'JNL-202505-0002', 'CO', 'AR', '2025-05-31', '請求確定: BILL-202505-0002', 'bills', 2, 149999, 149999, 1),
(3, 'JNL-202506-0003', 'CO', 'AR', '2025-06-30', '請求確定: BILL-202506-0003', 'bills', 3, 3850000, 3850000, 1);

-- journal_lines (9件 — テスト用仕訳明細)
INSERT OR IGNORE INTO journal_lines (id, journal_id, line_no, account_id, debit_amt, credit_amt, description) VALUES
-- 仕訳 1: 請求 #1
(1, 1, 10, 2, 999999, 0, '売掛金計上'),
(2, 1, 20, 12, 0, 909090, '売上計上'),
(3, 1, 30, 9, 0, 90909, '消費税計上'),
-- 仕訳 2: 請求 #2
(4, 2, 10, 2, 149999, 0, '売掛金計上'),
(5, 2, 20, 12, 0, 136363, '売上計上'),
(6, 2, 30, 9, 0, 13636, '消費税計上'),
-- 仕訳 3: 請求 #3
(7, 3, 10, 2, 3850000, 0, '売掛金計上'),
(8, 3, 20, 12, 0, 3500000, '売上計上'),
(9, 3, 30, 9, 0, 350000, '消費税計上');


-- ============================================================
-- TEST DATA — Phase 2: 購買フロー
-- ============================================================

-- purchase_orders (5件)
INSERT OR IGNORE INTO purchase_orders (id, document_no, doc_status, business_partner_id, date_ordered, date_promised, grand_total, tax_amt, approval_status, description) VALUES
(1, 'PO-202604-0001', 'CO', 3, '2026-04-01', '2026-05-15', 3000000, 300000, 'APPROVED', 'サーバ機器購買'),
(2, 'PO-202604-0002', 'IP', 5, '2026-04-05', '2026-05-01', 880000, 88000, 'PENDING', 'NW機器購買'),
(3, 'PO-202604-0003', 'DR', 4, '2026-04-10', '2026-06-01', 50000, 5000, NULL, '消耗品購買'),
(4, 'PO-202604-0004', 'DR', 3, '2026-04-15', '2026-05-30', 1500000, 150000, NULL, 'ストレージ装置'),
(5, 'PO-202604-0005', 'CO', 5, '2026-04-20', '2026-05-20', 250000, 25000, 'APPROVED', 'ケーブル・付属品');

-- purchase_order_lines (10件)
INSERT OR IGNORE INTO purchase_order_lines (id, purchase_order_id, line_no, product_id, qty_ordered, unit_price, line_amt, qty_received, tax_rate, tax_amt) VALUES
(1, 1, 10, 3, 2, 750000, 1500000, 2, 0.10, 150000),
(2, 1, 20, 9, 2, 220000, 440000, 2, 0.10, 44000),
(3, 1, 30, 15, 10, 5000, 50000, 0, 0.10, 5000),
(4, 2, 10, 9, 4, 220000, 880000, 0, 0.10, 88000),
(5, 3, 10, 15, 10, 5000, 50000, 0, 0.10, 5000),
(6, 4, 10, 3, 2, 750000, 1500000, 0, 0.10, 150000),
(7, 5, 10, 15, 20, 5000, 100000, 20, 0.10, 10000),
(8, 5, 20, 9, 1, 220000, 220000, 1, 0.10, 22000);

-- purchase_receipts (3件)
INSERT OR IGNORE INTO purchase_receipts (id, document_no, doc_status, purchase_order_id, date_received, total_received_amt, description) VALUES
(1, 'REC-202604-0001', 'CO', 1, '2026-04-10', 1940000, 'サーバ・NW機器受入'),
(2, 'REC-202604-0002', 'CO', 5, '2026-04-25', 320000, '消耗品受入'),
(3, 'REC-202604-0003', 'DR', 2, '2026-04-28', 0, 'NW機器一部受入');

-- purchase_receipt_lines (5件)
INSERT OR IGNORE INTO purchase_receipt_lines (id, receipt_id, po_line_id, product_id, qty_received, unit_cost, line_amt, description) VALUES
(1, 1, 1, 3, 2, 750000, 1500000, 'サーバ受入'),
(2, 1, 2, 9, 2, 220000, 440000, 'NWスイッチ受入'),
(3, 2, 7, 15, 20, 5000, 100000, '消耗品受入'),
(4, 2, 8, 9, 1, 220000, 220000, 'NWスイッチ受入'),
(5, 3, 4, 9, 2, 220000, 440000, 'NWスイッチ一部受入');

-- ap_invoices (3件)
INSERT OR IGNORE INTO ap_invoices (id, document_no, doc_status, business_partner_id, purchase_order_id, date_invoiced, date_due, grand_total, tax_base_amt, tax_amt, pay_amt, outstanding_amt, description) VALUES
(1, 'APBILL-202604-0001', 'CO', 3, 1, '2026-04-15', '2026-05-15', 2134000, 1940000, 194000, 1000000, 1134000, 'サーバ機器請求'),
(2, 'APBILL-202604-0002', 'CO', 5, 5, '2026-04-28', '2026-05-28', 352000, 320000, 32000, 352000, 0, '消耗品請求'),
(3, 'APBILL-202604-0003', 'DR', 3, 4, '2026-05-01', '2026-06-01', 1650000, 1500000, 150000, 0, 1650000, 'ストレージ装置請求');

-- ap_invoice_lines (5件)
INSERT OR IGNORE INTO ap_invoice_lines (id, ap_invoice_id, line_no, product_id, qty, unit_price, line_amt, tax_rate, tax_amt) VALUES
(1, 1, 10, 3, 2, 750000, 1500000, 0.10, 150000),
(2, 1, 20, 9, 2, 220000, 440000, 0.10, 44000),
(3, 2, 10, 15, 20, 5000, 100000, 0.10, 10000),
(4, 2, 20, 9, 1, 220000, 220000, 0.10, 22000),
(5, 3, 10, 3, 2, 750000, 1500000, 0.10, 150000);

-- payments (6件)
INSERT OR IGNORE INTO payments (id, document_no, payment_type, doc_status, business_partner_id, payment_date, payment_method, pay_amt, bill_id, ap_invoice_id, description) VALUES
(1, 'PAY-202505-0001', 'AR', 'CO', 1, '2025-05-15', 'T', 500000, 1, NULL, 'アルファテック 入金'),
(2, 'PAY-202506-0002', 'AR', 'CO', 2, '2025-06-15', 'T', 149999, 2, NULL, 'ベータSOL 入金'),
(3, 'PAY-202507-0003', 'AR', 'CO', 3, '2025-07-15', 'T', 1000000, 3, NULL, 'ガンマ商事 入金'),
(4, 'PAY-202604-0004', 'AP', 'CO', 3, '2026-05-01', 'T', 1000000, NULL, 1, 'サーバ機器支払'),
(5, 'PAY-202605-0005', 'AP', 'CO', 5, '2026-05-15', 'T', 352000, NULL, 2, '消耗品支払'),
(6, 'PAY-202605-0006', 'AR', 'DR', 6, '2026-05-20', 'T', 100000, 4, NULL, 'ゼータコンサル 入金予定');

-- stock_moves (5件)
INSERT OR IGNORE INTO stock_moves (id, move_type, product_id, qty, unit_cost, date_moved, source_table, source_id, description) VALUES
(1, 'IN', 3, 2, 750000, '2026-04-10', 'purchase_receipts', 1, 'サーバ入庫'),
(2, 'IN', 9, 2, 220000, '2026-04-10', 'purchase_receipts', 1, 'NWスイッチ入庫'),
(3, 'IN', 15, 20, 5000, '2026-04-25', 'purchase_receipts', 2, '消耗品入庫'),
(4, 'IN', 9, 1, 220000, '2026-04-25', 'purchase_receipts', 2, 'NWスイッチ入庫'),
(5, 'OUT', 15, 5, 5000, '2026-04-28', 'manual', NULL, '消耗品出庫');


-- ============================================================
-- TEST DATA — Phase 3: 承認ワークフロー
-- ============================================================

-- approval_requests (4件)
INSERT OR IGNORE INTO approval_requests (id, source_table, source_id, requester, current_step, total_steps, status, grand_total) VALUES
(1, 'purchase_orders', 2, '山田太郎', 1, 1, 'PENDING', 880000),
(2, 'purchase_orders', 4, '鈴木一郎', 1, 2, 'PENDING', 1500000),
(3, 'purchase_orders', 1, '佐藤花子', 1, 1, 'APPROVED', 3000000),
(4, 'purchase_orders', 5, '山田太郎', 1, 1, 'APPROVED', 250000);

-- approval_steps (6件)
INSERT OR IGNORE INTO approval_steps (id, request_id, step_no, approver, approver_role, label, status, approved_at, comments) VALUES
(1, 1, 1, '部長', 'manager', '上長承認', 'PENDING', NULL, NULL),
(2, 2, 1, '部長', 'manager', '部長承認', 'PENDING', NULL, NULL),
(3, 2, 2, '取締役', 'director', '取締役承認', 'PENDING', NULL, NULL),
(4, 3, 1, '部長', 'manager', '上長承認', 'APPROVED', '2026-04-02 10:00:00', '承認'),
(5, 4, 1, '部長', 'manager', '上長承認', 'APPROVED', '2026-04-21 09:00:00', '承認');


-- ============================================================
-- TEST DATA — Phase 1 追加仕訳（購買フロー関連）
-- ============================================================

-- journals (追加 5件 — AP請求・支払仕訳)
INSERT OR IGNORE INTO journals (id, document_no, doc_status, journal_type, date_acct, description, source_table, source_id, total_debit, total_credit, is_balanced) VALUES
(4, 'JNL-202604-0004', 'CO', 'AP', '2026-04-15', '仕入請求確定: APBILL-202604-0001', 'ap_invoices', 1, 2134000, 2134000, 1),
(5, 'JNL-202604-0005', 'CO', 'AP', '2026-04-28', '仕入請求確定: APBILL-202604-0002', 'ap_invoices', 2, 352000, 352000, 1),
(6, 'JNL-202605-0006', 'CO', 'AP', '2026-05-01', '支払: PAY-202604-0004', 'payments', 4, 1000000, 1000000, 1),
(7, 'JNL-202605-0007', 'CO', 'AP', '2026-05-15', '支払: PAY-202605-0005', 'payments', 5, 352000, 352000, 1),
(8, 'JNL-202605-0008', 'CO', 'AR', '2026-05-15', '入金: PAY-202505-0001', 'payments', 1, 500000, 500000, 1);

-- journal_lines (追加 13件)
INSERT OR IGNORE INTO journal_lines (id, journal_id, line_no, account_id, debit_amt, credit_amt, description) VALUES
-- 仕訳 4: AP請求 #1
(10, 4, 10, 15, 1940000, 0, '仕入計上'),
(11, 4, 20, 10, 194000, 0, '仮払消費税計上'),
(12, 4, 30, 7, 0, 2134000, '買掛金計上'),
-- 仕訳 5: AP請求 #2
(13, 5, 10, 15, 320000, 0, '仕入計上'),
(14, 5, 20, 10, 32000, 0, '仮払消費税計上'),
(15, 5, 30, 7, 0, 352000, '買掛金計上'),
-- 仕訳 6: AP支払 #1
(16, 6, 10, 7, 1000000, 0, '買掛金支払'),
(17, 6, 20, 5, 0, 1000000, '現金支払'),
-- 仕訳 7: AP支払 #2
(18, 7, 10, 7, 352000, 0, '買掛金支払'),
(19, 7, 20, 5, 0, 352000, '現金支払'),
-- 仕訳 8: AR入金 #1
(20, 8, 10, 5, 500000, 0, '入金計上'),
(21, 8, 20, 2, 0, 500000, '売掛金回収');
