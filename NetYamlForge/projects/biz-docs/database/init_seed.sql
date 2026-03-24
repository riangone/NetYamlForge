-- ============================================================
-- biz-docs database initialization (SQLite)
-- ============================================================
PRAGMA foreign_keys = OFF;

DROP TABLE IF EXISTS CustomsDeclaration;
DROP TABLE IF EXISTS Invoice;
DROP TABLE IF EXISTS QuotationItem;
DROP TABLE IF EXISTS Quotation;
DROP TABLE IF EXISTS JpContract;
DROP TABLE IF EXISTS JpDeliveryItem;
DROP TABLE IF EXISTS JpDelivery;
DROP TABLE IF EXISTS JpInvoiceItem;
DROP TABLE IF EXISTS JpInvoice;
DROP TABLE IF EXISTS JpEstimateItem;
DROP TABLE IF EXISTS JpEstimate;
DROP TABLE IF EXISTS Customer;

-- ---- Master tables ----

CREATE TABLE Customer (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Code            TEXT    NOT NULL UNIQUE,
    Name            TEXT    NOT NULL,
    NameEn          TEXT,
    Address         TEXT,
    AddressEn       TEXT,
    ContactPerson   TEXT,
    Phone           TEXT,
    Email           TEXT,
    TaxId           TEXT,
    Country         TEXT    NOT NULL DEFAULT 'CN'
);

-- ---- Transaction tables ----

CREATE TABLE Quotation (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    QuoteNo         TEXT    NOT NULL UNIQUE,
    CustomerId      INTEGER NOT NULL,
    IssueDate       TEXT    NOT NULL,
    ExpiryDate      TEXT,
    Currency        TEXT    NOT NULL DEFAULT 'USD',
    Subtotal        REAL    NOT NULL DEFAULT 0,
    TaxRate         REAL    NOT NULL DEFAULT 0,
    TaxAmount       REAL    NOT NULL DEFAULT 0,
    Total           REAL    NOT NULL DEFAULT 0,
    Status          TEXT    NOT NULL DEFAULT 'draft',
    PaymentTerms    TEXT,
    DeliveryTerms   TEXT,
    Notes           TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);

CREATE TABLE QuotationItem (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    QuotationId     INTEGER NOT NULL,
    LineNo          INTEGER NOT NULL,
    PartNo          TEXT,
    Description     TEXT    NOT NULL,
    Unit            TEXT    NOT NULL DEFAULT 'pcs',
    Quantity        REAL    NOT NULL,
    UnitPrice       REAL    NOT NULL,
    Amount          REAL    NOT NULL,
    Remarks         TEXT,
    FOREIGN KEY (QuotationId) REFERENCES Quotation(Id)
);

CREATE TABLE Invoice (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo       TEXT    NOT NULL UNIQUE,
    QuotationId     INTEGER,
    CustomerId      INTEGER NOT NULL,
    IssueDate       TEXT    NOT NULL,
    DueDate         TEXT    NOT NULL,
    Currency        TEXT    NOT NULL DEFAULT 'USD',
    Subtotal        REAL    NOT NULL DEFAULT 0,
    TaxRate         REAL    NOT NULL DEFAULT 0,
    TaxAmount       REAL    NOT NULL DEFAULT 0,
    Total           REAL    NOT NULL DEFAULT 0,
    PaidAmount      REAL    NOT NULL DEFAULT 0,
    Status          TEXT    NOT NULL DEFAULT 'draft',
    BankName        TEXT,
    BankAccount     TEXT,
    SwiftCode       TEXT,
    PaymentTerms    TEXT,
    Notes           TEXT,
    FOREIGN KEY (CustomerId)  REFERENCES Customer(Id),
    FOREIGN KEY (QuotationId) REFERENCES Quotation(Id)
);

CREATE TABLE CustomsDeclaration (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    DeclNo          TEXT    NOT NULL UNIQUE,
    InvoiceId       INTEGER,
    DeclType        TEXT    NOT NULL DEFAULT 'export',
    ExporterName    TEXT    NOT NULL,
    ExporterAddress TEXT,
    ImporterName    TEXT    NOT NULL,
    ImporterAddress TEXT,
    PortOfLoading   TEXT,
    PortOfDischarge TEXT,
    DepartureDate   TEXT,
    ArrivalDate     TEXT,
    Incoterms       TEXT    NOT NULL DEFAULT 'FOB',
    Currency        TEXT    NOT NULL DEFAULT 'USD',
    TotalValue      REAL    NOT NULL DEFAULT 0,
    Packages        INTEGER,
    GrossWeightKg   REAL,
    NetWeightKg     REAL,
    HsCode          TEXT,
    CargoDescription TEXT   NOT NULL,
    ContainerNo     TEXT,
    VesselName      TEXT,
    Status          TEXT    NOT NULL DEFAULT 'draft',
    Notes           TEXT,
    FOREIGN KEY (InvoiceId) REFERENCES Invoice(Id)
);

PRAGMA foreign_keys = ON;

-- ---- Seed: Customers ----

INSERT INTO Customer (Code, Name, NameEn, Address, AddressEn, ContactPerson, Phone, Email, TaxId, Country) VALUES
  ('C001', '上海宏远贸易有限公司',   'Shanghai Hongyuan Trading Co., Ltd.',
   '上海市浦东新区张江高科技园区科苑路88号',
   '88 Keyuan Rd, Zhangjiang Hi-Tech Park, Pudong, Shanghai',
   '张伟', '+86-21-5000-1234', 'zhangwei@hongyuan.com', '91310115XXXXXXXX', 'CN'),
  ('C002', '深圳市蓝海科技股份有限公司', 'Shenzhen Lanhai Technology Co., Ltd.',
   '深圳市南山区科技园南区高新南七道18号',
   '18 Gaoxin South 7th Rd, Nanshan Science Park, Shenzhen',
   '李明', '+86-755-8600-7788', 'liming@lanhai-tech.com', '91440300XXXXXXXX', 'CN'),
  ('C003', 'Acme Electronics GmbH',  'Acme Electronics GmbH',
   'Industriestraße 42, 70565 Stuttgart, Germany', NULL,
   'Hans Müller', '+49-711-123456', 'h.mueller@acme-elec.de', 'DE123456789', 'DE'),
  ('C004', '广州绿洲国际物流有限公司', 'Guangzhou Oasis International Logistics Co., Ltd.',
   '广州市越秀区东风中路385号', NULL,
   '陈静', '+86-20-8760-5566', 'chenjing@oasis-log.com', '91440101XXXXXXXX', 'CN');

-- ---- Seed: Quotations ----

INSERT INTO Quotation (QuoteNo, CustomerId, IssueDate, ExpiryDate, Currency, Subtotal, TaxRate, TaxAmount, Total, Status, PaymentTerms, DeliveryTerms, Notes) VALUES
  ('QT-2026-0101', 1, '2026-01-10', '2026-02-10', 'USD', 48500.00, 0.00, 0.00, 48500.00, 'sent',
   'T/T 30 days after shipment', 'FOB Shanghai', 'Bulk order for Q1 delivery'),
  ('QT-2026-0102', 2, '2026-01-20', '2026-02-20', 'USD', 12800.00, 0.00, 0.00, 12800.00, 'accepted',
   'T/T in advance', 'EXW Shenzhen', NULL),
  ('QT-2026-0103', 3, '2026-02-01', '2026-03-01', 'EUR', 35200.00, 19.00, 6688.00, 41888.00, 'draft',
   'Net 60 days', 'CIF Hamburg', 'Include installation manual in German'),
  ('QT-2026-0104', 1, '2026-02-15', '2026-03-15', 'USD', 9750.00, 0.00, 0.00, 9750.00, 'accepted',
   'L/C at sight', 'FOB Shanghai', 'Sample order before bulk PO'),
  ('QT-2026-0105', 4, '2026-03-01', '2026-04-01', 'CNY', 186000.00, 13.00, 24180.00, 210180.00, 'sent',
   '月结30天', 'DAP 广州', '含安装调试服务');

-- ---- Seed: QuotationItems ----

INSERT INTO QuotationItem (QuotationId, LineNo, PartNo, Description, Unit, Quantity, UnitPrice, Amount) VALUES
  -- QT-2026-0101
  (1, 1, 'PCB-A100', 'Printed Circuit Board Type A, 4-layer', 'pcs', 500, 32.00, 16000.00),
  (1, 2, 'PCB-B200', 'Printed Circuit Board Type B, 6-layer', 'pcs', 300, 58.00, 17400.00),
  (1, 3, 'CAB-USB3', 'USB 3.0 Cable Assembly, 1.5m', 'pcs', 1000, 15.10, 15100.00),
  -- QT-2026-0102
  (2, 1, 'MOD-WIFI6', 'WiFi 6 Module AX200', 'pcs', 200, 38.00, 7600.00),
  (2, 2, 'ANT-2.4G', '2.4GHz Antenna with connector', 'pcs', 400, 13.00, 5200.00),
  -- QT-2026-0103
  (3, 1, 'SRV-2U-32C', '2U Rack Server 32-Core', 'unit', 4, 6800.00, 27200.00),
  (3, 2, 'SSD-2T-NVMe', '2TB NVMe SSD Enterprise', 'pcs', 16, 250.00, 4000.00),
  (3, 3, 'RAM-64G-ECC', '64GB ECC DDR5 Memory', 'pcs', 16, 250.00, 4000.00),
  -- QT-2026-0104
  (4, 1, 'PCB-A100', 'Printed Circuit Board Type A, 4-layer', 'pcs', 50, 35.00, 1750.00),
  (4, 2, 'PCB-B200', 'Printed Circuit Board Type B, 6-layer', 'pcs', 30, 62.00, 1860.00),
  (4, 3, 'MOD-WIFI6', 'WiFi 6 Module AX200', 'pcs', 50, 42.00, 2100.00),
  (4, 4, 'ANT-2.4G', '2.4GHz Antenna with connector', 'pcs', 100, 14.00, 1400.00),
  (4, 5, 'CAB-USB3', 'USB 3.0 Cable Assembly, 1.5m', 'pcs', 50, 15.28, 764.00),
  -- QT-2026-0105
  (5, 1, 'EQ-CNC-5A', 'CNC加工中心五轴联动 型号5A', '台', 2, 88000.00, 176000.00),
  (5, 2, 'SVC-INST', '安装调试服务（含培训2天）', '次', 2, 5000.00, 10000.00);

-- ---- Seed: Invoices ----

INSERT INTO Invoice (InvoiceNo, QuotationId, CustomerId, IssueDate, DueDate, Currency, Subtotal, TaxRate, TaxAmount, Total, PaidAmount, Status, BankName, BankAccount, SwiftCode, PaymentTerms, Notes) VALUES
  ('INV-2026-0201', 1, 1, '2026-02-10', '2026-03-12', 'USD',
   48500.00, 0.00, 0.00, 48500.00, 48500.00, 'paid',
   'Bank of China Shanghai Branch', '6222 0200 0012 3456 789', 'BKCHCNBJ310',
   'T/T 30 days after shipment', 'Payment received 2026-03-10'),
  ('INV-2026-0202', 2, 2, '2026-02-01', '2026-02-08', 'USD',
   12800.00, 0.00, 0.00, 12800.00, 12800.00, 'paid',
   'China Merchants Bank Shenzhen', '6225 8802 0001 9876 543', 'CMBCCNBS',
   'T/T in advance', NULL),
  ('INV-2026-0203', 4, 1, '2026-03-01', '2026-04-30', 'USD',
   9750.00, 0.00, 0.00, 9750.00, 0.00, 'issued',
   'Bank of China Shanghai Branch', '6222 0200 0012 3456 789', 'BKCHCNBJ310',
   'L/C at sight', 'Awaiting L/C issuance from buyer'),
  ('INV-2026-0204', NULL, 4, '2026-03-10', '2026-04-10', 'CNY',
   186000.00, 13.00, 24180.00, 210180.00, 0.00, 'issued',
   '中国工商银行广州越秀支行', '6222 0302 0012 1111 222', 'ICBKCNBJGZU',
   '月结30天', '含13%增值税');

-- ---- Seed: Customs Declarations ----

INSERT INTO CustomsDeclaration (DeclNo, InvoiceId, DeclType, ExporterName, ExporterAddress, ImporterName, ImporterAddress, PortOfLoading, PortOfDischarge, DepartureDate, ArrivalDate, Incoterms, Currency, TotalValue, Packages, GrossWeightKg, NetWeightKg, HsCode, CargoDescription, ContainerNo, VesselName, Status, Notes) VALUES
  ('CD-2026-SH-001', 1, 'export',
   '上海宏远贸易有限公司', '上海市浦东新区张江高科技园区科苑路88号',
   'Acme Electronics GmbH', 'Industriestraße 42, 70565 Stuttgart, Germany',
   'Shanghai Yangshan', 'Hamburg', '2026-02-15', '2026-03-08',
   'FOB', 'USD', 48500.00, 24, 1250.5, 1180.0,
   '8534.00', 'Printed Circuit Boards and Electronic Assemblies',
   'TCKU3456789', 'COSCO SHIPPING FORTUNE', 'cleared',
   'All duties paid; clearance completed 2026-03-10'),
  ('CD-2026-SZ-001', 2, 'export',
   '深圳市蓝海科技股份有限公司', '深圳市南山区科技园南区高新南七道18号',
   'TechHub Singapore Pte. Ltd.', '80 Robinson Road #08-01, Singapore 068898',
   'Yantian International', 'Singapore PSA', '2026-02-10', '2026-02-18',
   'EXW', 'USD', 12800.00, 6, 320.0, 285.0,
   '8517.62', 'WiFi Modules and RF Antennas',
   'MSDU2987654', 'MSC ATHENS', 'cleared',
   NULL),
  ('CD-2026-SH-002', 3, 'export',
   '上海宏远贸易有限公司', '上海市浦东新区张江高科技园区科苑路88号',
   'Horizon Logistics B.V.', 'Maasvlakte 2, Rotterdam, Netherlands',
   'Shanghai Yangshan', 'Rotterdam', '2026-04-01', NULL,
   'CIF', 'USD', 9750.00, 8, 580.0, 530.0,
   '8534.00;8517.62', 'Electronic Components (PCBs, WiFi Modules, Cables)',
   NULL, NULL, 'submitted',
   'Awaiting customs authority approval'),
  ('CD-2026-GZ-001', 4, 'import',
   'KOYO Machine Tools Co., Ltd.', '3-1-1 Nishimachi, Suita, Osaka 564-0034, Japan',
   '广州绿洲国际物流有限公司', '广州市越秀区东风中路385号',
   'Osaka', 'Guangzhou Nansha', '2026-03-20', '2026-04-05',
   'CIF', 'JPY', 2850000.00, 2, 8500.0, 7200.0,
   '8457.10', '5轴立式加工中心 型号5A×2台',
   'OOLU8765432', 'ONE COMMITMENT', 'draft',
   '设备进口：含免税申请（高新技术设备）');

-- ============================================================
-- 日本向けビジネス文書テーブル
-- ============================================================

-- 見積書（インボイス制度対応・有効期限注記付き）
CREATE TABLE JpEstimate (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EstimateNo      TEXT    NOT NULL UNIQUE,
    CustomerId      INTEGER NOT NULL,
    Title           TEXT    NOT NULL,
    IssueDate       TEXT    NOT NULL,
    ExpiryDate      TEXT,
    -- 税率別集計（10%通常税率 / 8%軽減税率）
    Subtotal10      REAL    NOT NULL DEFAULT 0,   -- 10%対象小計
    TaxAmount10     REAL    NOT NULL DEFAULT 0,   -- 消費税(10%)
    Subtotal8       REAL    NOT NULL DEFAULT 0,   -- 8%対象小計
    TaxAmount8      REAL    NOT NULL DEFAULT 0,   -- 消費税(8%)
    Subtotal        REAL    NOT NULL DEFAULT 0,   -- 合計小計
    TaxAmount       REAL    NOT NULL DEFAULT 0,   -- 消費税合計
    Total           REAL    NOT NULL DEFAULT 0,   -- 税込合計
    PaymentTerms    TEXT,
    DeliveryDays    TEXT,
    DeliveryPlace   TEXT,
    ValidityNote    TEXT,   -- 「本見積の有効期限は発行日より30日間」等
    ExclusionNote   TEXT,   -- 「以下は見積に含まれません」等
    Status          TEXT    NOT NULL DEFAULT 'draft',
    PreparedBy      TEXT,
    CompanyStamp    TEXT,   -- 印鑑表示用（会社名等）
    Remarks         TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);

CREATE TABLE JpEstimateItem (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EstimateId      INTEGER NOT NULL,
    LineNo          INTEGER NOT NULL,
    ItemCode        TEXT,
    ItemName        TEXT    NOT NULL,
    Spec            TEXT,
    Unit            TEXT    NOT NULL DEFAULT '式',
    Quantity        REAL    NOT NULL DEFAULT 1,
    UnitPrice       REAL    NOT NULL DEFAULT 0,
    Amount          REAL    NOT NULL DEFAULT 0,
    TaxRate         REAL    NOT NULL DEFAULT 10,  -- 品目ごとの税率（10 or 8）
    FOREIGN KEY (EstimateId) REFERENCES JpEstimate(Id)
);

-- 請求書（インボイス制度：適格請求書等保存方式 対応）
CREATE TABLE JpInvoice (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo       TEXT    NOT NULL UNIQUE,
    CustomerId      INTEGER NOT NULL,
    Title           TEXT    NOT NULL,
    IssueDate       TEXT    NOT NULL,
    DueDate         TEXT    NOT NULL,
    RegistrationNo  TEXT,   -- 適格請求書発行事業者登録番号（T + 13桁）
    -- 税率別集計（インボイス制度の必須記載事項）
    Subtotal10      REAL    NOT NULL DEFAULT 0,   -- 10%対象小計（税抜）
    TaxAmount10     REAL    NOT NULL DEFAULT 0,   -- 消費税額(10%)
    Subtotal8       REAL    NOT NULL DEFAULT 0,   -- 8%対象小計（税抜）
    TaxAmount8      REAL    NOT NULL DEFAULT 0,   -- 消費税額(8%)
    Subtotal        REAL    NOT NULL DEFAULT 0,   -- 小計（税抜合計）
    TaxAmount       REAL    NOT NULL DEFAULT 0,   -- 消費税合計
    Total           REAL    NOT NULL DEFAULT 0,   -- 税込合計
    BankName        TEXT,
    BranchName      TEXT,
    AccountType     TEXT    DEFAULT '普通',
    AccountNo       TEXT,
    AccountHolder   TEXT,
    TransferFeeNote TEXT    DEFAULT '振込手数料はご負担くださいますようお願い申し上げます。',
    Status          TEXT    NOT NULL DEFAULT 'draft',
    PreparedBy      TEXT,
    CompanyStamp    TEXT,
    Remarks         TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);

CREATE TABLE JpInvoiceItem (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId       INTEGER NOT NULL,
    LineNo          INTEGER NOT NULL,
    ItemCode        TEXT,
    ItemName        TEXT    NOT NULL,
    Spec            TEXT,
    Unit            TEXT    NOT NULL DEFAULT '式',
    Quantity        REAL    NOT NULL DEFAULT 1,
    UnitPrice       REAL    NOT NULL DEFAULT 0,
    Amount          REAL    NOT NULL DEFAULT 0,
    TaxRate         REAL    NOT NULL DEFAULT 10,  -- 品目ごとの税率（10 or 8）
    FOREIGN KEY (InvoiceId) REFERENCES JpInvoice(Id)
);

-- 納品書（検収確認・返品対応付き）
CREATE TABLE JpDelivery (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    DeliveryNo            TEXT    NOT NULL UNIQUE,
    CustomerId            INTEGER NOT NULL,
    Title                 TEXT    NOT NULL,
    DeliveryDate          TEXT    NOT NULL,
    DeliveryPlace         TEXT,
    Subtotal10            REAL    NOT NULL DEFAULT 0,
    TaxAmount10           REAL    NOT NULL DEFAULT 0,
    Subtotal8             REAL    NOT NULL DEFAULT 0,
    TaxAmount8            REAL    NOT NULL DEFAULT 0,
    Subtotal              REAL    NOT NULL DEFAULT 0,
    TaxAmount             REAL    NOT NULL DEFAULT 0,
    Total                 REAL    NOT NULL DEFAULT 0,
    InspectionPeriodDays  INTEGER NOT NULL DEFAULT 5,  -- 検収期間（営業日）
    ReceiptConfirmedDate  TEXT,   -- 受領確認日（受領印相当）
    Status                TEXT    NOT NULL DEFAULT 'draft',
    PreparedBy            TEXT,
    CompanyStamp          TEXT,
    Remarks               TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);

CREATE TABLE JpDeliveryItem (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    DeliveryId      INTEGER NOT NULL,
    LineNo          INTEGER NOT NULL,
    ItemCode        TEXT,
    ItemName        TEXT    NOT NULL,
    Spec            TEXT,
    Unit            TEXT    NOT NULL DEFAULT '式',
    Quantity        REAL    NOT NULL DEFAULT 1,
    UnitPrice       REAL    NOT NULL DEFAULT 0,
    Amount          REAL    NOT NULL DEFAULT 0,
    TaxRate         REAL    NOT NULL DEFAULT 10,
    FOREIGN KEY (DeliveryId) REFERENCES JpDelivery(Id)
);

-- 契約書台帳（印紙税・電子契約・反社条項対応）
CREATE TABLE JpContract (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ContractNo      TEXT    NOT NULL UNIQUE,
    CustomerId      INTEGER NOT NULL,
    Title           TEXT    NOT NULL,
    ContractType    TEXT    NOT NULL,
    StartDate       TEXT    NOT NULL,
    EndDate         TEXT,
    AutoRenew       INTEGER NOT NULL DEFAULT 0,
    ContractAmount  REAL,
    PaymentTerms    TEXT,
    Status          TEXT    NOT NULL DEFAULT 'draft',
    SignedDate      TEXT,
    IsElectronic    INTEGER NOT NULL DEFAULT 1,  -- 1=電子契約（印紙不要）0=紙
    StampTaxAmount  INTEGER,  -- 印紙税額（紙契約時）
    JurisdictionCourt TEXT DEFAULT '東京地方裁判所',  -- 合意管轄
    GoverningLaw    TEXT    DEFAULT '日本法',
    OurSignatory    TEXT,
    TheirSignatory  TEXT,
    Remarks         TEXT,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);

-- ---- 見積書シードデータ ----
-- Subtotal10, TaxAmount10, Subtotal8, TaxAmount8, Subtotal, TaxAmount, Total

INSERT INTO JpEstimate (EstimateNo, CustomerId, Title, IssueDate, ExpiryDate,
  Subtotal10, TaxAmount10, Subtotal8, TaxAmount8, Subtotal, TaxAmount, Total,
  PaymentTerms, DeliveryDays, DeliveryPlace,
  ValidityNote, ExclusionNote, Status, PreparedBy, CompanyStamp) VALUES
  ('MK-2026-0001', 1, 'Webシステム開発一式', '2026-01-15', '2026-02-15',
   2500000, 250000, 0, 0, 2500000, 250000, 2750000,
   '納品後30日以内', '受注後60営業日', '貴社指定場所',
   '本見積の有効期限は発行日より30日間とします。',
   '以下は本見積に含まれません：サーバー・インフラ費用、既存データ移行費用、運用保守費用。',
   'sent', '田中 太郎', '株式会社テックソリューション'),
  ('MK-2026-0002', 2, 'サーバー保守サービス（年間）', '2026-01-20', '2026-02-20',
   1200000, 120000, 0, 0, 1200000, 120000, 1320000,
   '月末締め翌月払い', '契約締結後即時', '弊社データセンター',
   '本見積の有効期限は発行日より30日間とします。',
   '物理ハードウェア交換費用は別途請求となります。',
   'accepted', '鈴木 花子', '株式会社テックソリューション'),
  ('MK-2026-0003', 3, 'ネットワーク機器導入・設定', '2026-02-01', '2026-03-01',
   850000, 85000, 0, 0, 850000, 85000, 935000,
   '納品後60日以内', '受注後30営業日', '貴社本社',
   '本見積の有効期限は発行日より30日間とします。',
   'ケーブル配線工事、電源工事は含まれません。',
   'draft', '佐藤 次郎', '株式会社テックソリューション'),
  ('MK-2026-0004', 4, '社内研修プログラム（3日間）', '2026-02-10', '2026-03-10',
   450000, 45000, 0, 0, 450000, 45000, 495000,
   '研修実施後30日以内', '要相談', '貴社会議室',
   '本見積の有効期限は発行日より30日間とします。',
   '交通費・宿泊費は別途実費精算となります。',
   'sent', '田中 太郎', '株式会社テックソリューション'),
  ('MK-2026-0005', 2, 'クラウド移行コンサルティング', '2026-03-01', '2026-04-01',
   3200000, 320000, 0, 0, 3200000, 320000, 3520000,
   '着手時50%・完了時50%', '受注後90営業日', '弊社オフィス',
   '本見積の有効期限は発行日より30日間とします。',
   'クラウドサービス利用料（AWS/Azure等）は含まれません。',
   'accepted', '山田 一郎', '株式会社テックソリューション');

INSERT INTO JpEstimateItem (EstimateId, LineNo, ItemCode, ItemName, Spec, Unit, Quantity, UnitPrice, Amount, TaxRate) VALUES
  (1, 1, 'DEV-001', '要件定義・基本設計', NULL, '式', 1, 600000, 600000, 10),
  (1, 2, 'DEV-002', '詳細設計・開発', '画面数20画面', '式', 1, 1200000, 1200000, 10),
  (1, 3, 'DEV-003', 'テスト・品質保証', NULL, '式', 1, 400000, 400000, 10),
  (1, 4, 'DEV-004', '導入・移行支援', NULL, '式', 1, 300000, 300000, 10),
  (2, 1, 'SVC-001', 'サーバー監視（24時間365日）', '月次レポート付き', '月', 12, 80000, 960000, 10),
  (2, 2, 'SVC-002', 'セキュリティパッチ適用', '月1回', '回', 12, 20000, 240000, 10),
  (3, 1, 'NET-001', 'コアスイッチ', 'Cisco Catalyst 9300', '台', 2, 180000, 360000, 10),
  (3, 2, 'NET-002', 'アクセススイッチ', 'Cisco Catalyst 9200', '台', 4, 80000, 320000, 10),
  (3, 3, 'NET-003', '設定・導入作業', NULL, '式', 1, 170000, 170000, 10),
  (4, 1, 'TRN-001', 'Pythonプログラミング研修（3日間）', '定員10名', '回', 1, 350000, 350000, 10),
  (4, 2, 'TRN-002', 'テキスト・教材費', '1名分', '冊', 10, 10000, 100000, 10),
  (5, 1, 'CON-001', 'As-Is調査・現状分析', NULL, '式', 1, 800000, 800000, 10),
  (5, 2, 'CON-002', 'To-Be設計・移行計画策定', NULL, '式', 1, 1200000, 1200000, 10),
  (5, 3, 'CON-003', '移行実施支援（3ヶ月）', NULL, '月', 3, 400000, 1200000, 10);

-- ---- 請求書シードデータ（インボイス制度対応）----
-- RegistrationNo: T1234567890123 = 弊社の適格請求書発行事業者登録番号
-- Subtotal10, TaxAmount10, Subtotal8, TaxAmount8, Subtotal, TaxAmount, Total

INSERT INTO JpInvoice (InvoiceNo, CustomerId, Title, IssueDate, DueDate,
  RegistrationNo, Subtotal10, TaxAmount10, Subtotal8, TaxAmount8,
  Subtotal, TaxAmount, Total,
  BankName, BranchName, AccountType, AccountNo, AccountHolder,
  TransferFeeNote, Status, PreparedBy, CompanyStamp) VALUES
  ('INV-JP-0001', 2, 'サーバー保守サービス（2026年1月分）', '2026-01-31', '2026-02-28',
   'T1234567890123', 100000, 10000, 0, 0, 100000, 10000, 110000,
   'みずほ銀行', '渋谷支店', '普通', '1234567', 'カ）テックソリューション',
   '振込手数料はご負担くださいますようお願い申し上げます。', 'paid', '鈴木 花子', '株式会社テックソリューション'),
  ('INV-JP-0002', 1, 'Webシステム開発 第1回請求（着手金）', '2026-02-01', '2026-03-03',
   'T1234567890123', 1250000, 125000, 0, 0, 1250000, 125000, 1375000,
   'みずほ銀行', '渋谷支店', '普通', '1234567', 'カ）テックソリューション',
   '振込手数料はご負担くださいますようお願い申し上げます。', 'paid', '田中 太郎', '株式会社テックソリューション'),
  ('INV-JP-0003', 2, 'サーバー保守サービス（2026年2月分）', '2026-02-28', '2026-03-31',
   'T1234567890123', 100000, 10000, 0, 0, 100000, 10000, 110000,
   'みずほ銀行', '渋谷支店', '普通', '1234567', 'カ）テックソリューション',
   '振込手数料はご負担くださいますようお願い申し上げます。', 'issued', '鈴木 花子', '株式会社テックソリューション'),
  ('INV-JP-0004', 4, '社内研修プログラム実施費用', '2026-03-05', '2026-04-05',
   'T1234567890123', 450000, 45000, 0, 0, 450000, 45000, 495000,
   'みずほ銀行', '渋谷支店', '普通', '1234567', 'カ）テックソリューション',
   '振込手数料はご負担くださいますようお願い申し上げます。', 'issued', '田中 太郎', '株式会社テックソリューション'),
  ('INV-JP-0005', 3, 'ネットワーク機器 第1回（機器代）', '2026-03-10', '2026-02-28',
   'T1234567890123', 680000, 68000, 0, 0, 680000, 68000, 748000,
   'みずほ銀行', '渋谷支店', '普通', '1234567', 'カ）テックソリューション',
   '振込手数料はご負担くださいますようお願い申し上げます。', 'overdue', '佐藤 次郎', '株式会社テックソリューション');

INSERT INTO JpInvoiceItem (InvoiceId, LineNo, ItemCode, ItemName, Spec, Unit, Quantity, UnitPrice, Amount, TaxRate) VALUES
  (1, 1, 'SVC-001', 'サーバー監視（2026年1月分）', NULL, '月', 1, 80000, 80000, 10),
  (1, 2, 'SVC-002', 'セキュリティパッチ適用', NULL, '回', 1, 20000, 20000, 10),
  (2, 1, 'DEV-001', 'Webシステム開発 着手金', '契約金額の50%', '式', 1, 1250000, 1250000, 10),
  (3, 1, 'SVC-001', 'サーバー監視（2026年2月分）', NULL, '月', 1, 80000, 80000, 10),
  (3, 2, 'SVC-002', 'セキュリティパッチ適用', NULL, '回', 1, 20000, 20000, 10),
  (4, 1, 'TRN-001', 'Pythonプログラミング研修（3日間）', '実施日: 2026-02-20〜22', '回', 1, 350000, 350000, 10),
  (4, 2, 'TRN-002', 'テキスト・教材費', '10名分', '式', 1, 100000, 100000, 10),
  (5, 1, 'NET-001', 'コアスイッチ Cisco Catalyst 9300', '2台', '台', 2, 180000, 360000, 10),
  (5, 2, 'NET-002', 'アクセススイッチ Cisco Catalyst 9200', '4台', '台', 4, 80000, 320000, 10);

-- ---- 納品書シードデータ ----

INSERT INTO JpDelivery (DeliveryNo, CustomerId, Title, DeliveryDate, DeliveryPlace,
  Subtotal10, TaxAmount10, Subtotal8, TaxAmount8, Subtotal, TaxAmount, Total,
  InspectionPeriodDays, ReceiptConfirmedDate, Status, PreparedBy, CompanyStamp) VALUES
  ('DLV-JP-0001', 2, 'サーバー監視ツール導入セット', '2026-01-20', '深圳市蓝海科技 サーバー室',
   180000, 18000, 0, 0, 180000, 18000, 198000, 5, '2026-01-23', 'confirmed', '鈴木 花子', '株式会社テックソリューション'),
  ('DLV-JP-0002', 3, 'ネットワーク機器一式', '2026-03-15', 'Acme Electronics GmbH 本社',
   680000, 68000, 0, 0, 680000, 68000, 748000, 5, NULL, 'delivered', '佐藤 次郎', '株式会社テックソリューション'),
  ('DLV-JP-0003', 1, 'Webシステム 成果物一式（第1フェーズ）', '2026-03-20', '上海宏远贸易 IT部門',
   1500000, 150000, 0, 0, 1500000, 150000, 1650000, 5, NULL, 'delivered', '田中 太郎', '株式会社テックソリューション'),
  ('DLV-JP-0004', 4, '研修教材・テキスト', '2026-02-18', '广州绿洲国际物流 総務部',
   100000, 10000, 0, 0, 100000, 10000, 110000, 3, '2026-02-20', 'confirmed', '田中 太郎', '株式会社テックソリューション');

INSERT INTO JpDeliveryItem (DeliveryId, LineNo, ItemCode, ItemName, Spec, Unit, Quantity, UnitPrice, Amount, TaxRate) VALUES
  (1, 1, 'SFT-001', '監視ツール ライセンス', '1年間', '式', 1, 120000, 120000, 10),
  (1, 2, 'SFT-002', 'セキュリティエージェント', '10ノード', '式', 1, 60000, 60000, 10),
  (2, 1, 'NET-001', 'コアスイッチ Cisco Catalyst 9300', NULL, '台', 2, 180000, 360000, 10),
  (2, 2, 'NET-002', 'アクセススイッチ Cisco Catalyst 9200', NULL, '台', 4, 80000, 320000, 10),
  (3, 1, 'DOC-001', '要件定義書', 'PDF + Word形式', '式', 1, 0, 0, 10),
  (3, 2, 'DOC-002', '基本設計書', 'PDF + Word形式', '式', 1, 0, 0, 10),
  (3, 3, 'DEV-010', 'ソースコード一式', 'Git リポジトリ', '式', 1, 1500000, 1500000, 10),
  (4, 1, 'TRN-002', 'Python研修テキスト', '第3版', '冊', 10, 10000, 100000, 10);

-- ---- 契約書シードデータ（電子契約・反社条項・管轄裁判所対応）----

INSERT INTO JpContract (ContractNo, CustomerId, Title, ContractType, StartDate, EndDate,
  AutoRenew, ContractAmount, PaymentTerms, Status, SignedDate,
  IsElectronic, StampTaxAmount, JurisdictionCourt, GoverningLaw,
  OurSignatory, TheirSignatory, Remarks) VALUES
  ('CTR-2026-0001', 2, 'サーバー保守サービス委託契約', '業務委託契約', '2026-01-01', '2026-12-31',
   1, 1200000, '月末締め翌月払い', 'active', '2025-12-20',
   1, NULL, '東京地方裁判所', '日本法',
   '代表取締役 山田 一郎', '代表取締役 李 明', '年次更新・前月末までに解約申告要'),
  ('CTR-2026-0002', 1, 'Webシステム開発請負契約', '請負契約', '2026-02-01', '2026-07-31',
   0, 2750000, '着手金50%・完了時50%', 'active', '2026-01-25',
   1, NULL, '東京地方裁判所', '日本法',
   '代表取締役 山田 一郎', '総経理 张 伟', '仕様変更は別途書面協議'),
  ('CTR-2026-0003', 3, '秘密保持契約（NDA）', '秘密保持契約', '2026-01-10', NULL,
   0, NULL, NULL, 'active', '2026-01-10',
   1, NULL, '東京地方裁判所', '日本法',
   '代表取締役 山田 一郎', 'CEO Hans Müller', '期間の定めなし・書面解除まで有効'),
  ('CTR-2026-0004', 4, 'IT研修サービス提供契約', '業務委託契約', '2025-04-01', '2026-03-31',
   0, 495000, '研修実施後30日以内', 'active', '2025-03-25',
   0, 200, '東京地方裁判所', '日本法',
   '代表取締役 山田 一郎', '代表取締役 陈 静', '紙契約・印紙200円貼付済'),
  ('CTR-2026-0005', 2, 'クラウド移行コンサルティング契約', '業務委託契約', '2026-04-01', '2026-09-30',
   0, 3520000, '着手時50%・完了時50%', 'review', NULL,
   1, NULL, '東京地方裁判所', '日本法',
   NULL, NULL, '契約書レビュー中（法務確認待ち）');
