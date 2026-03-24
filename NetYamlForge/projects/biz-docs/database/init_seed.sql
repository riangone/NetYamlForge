-- ============================================================
-- biz-docs database initialization (SQLite)
-- ============================================================
PRAGMA foreign_keys = OFF;

DROP TABLE IF EXISTS CustomsDeclaration;
DROP TABLE IF EXISTS Invoice;
DROP TABLE IF EXISTS QuotationItem;
DROP TABLE IF EXISTS Quotation;
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
