-- biz-docs 数据库初始化脚本
-- 所有 Entity 定义对应的表を作成

-- 取引先マスタ
CREATE TABLE IF NOT EXISTS Customer (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    NameEn TEXT,
    Country TEXT NOT NULL,
    ContactPerson TEXT,
    Phone TEXT,
    Email TEXT,
    TaxId TEXT,
    Address TEXT,
    AddressEn TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS IX_Customer_Code ON Customer(Code);
CREATE INDEX IF NOT EXISTS IX_Customer_Name ON Customer(Name);

-- 報価単
CREATE TABLE IF NOT EXISTS Quotation (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    QuoteNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    ExpiryDate DATE,
    Currency TEXT NOT NULL,
    Subtotal DECIMAL(18,2),
    TaxRate DECIMAL(5,2),
    TaxAmount DECIMAL(18,2),
    Total DECIMAL(18,2) NOT NULL,
    Status TEXT NOT NULL,
    PaymentTerms TEXT,
    DeliveryTerms TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_Quotation_QuoteNo ON Quotation(QuoteNo);
CREATE INDEX IF NOT EXISTS IX_Quotation_CustomerId ON Quotation(CustomerId);
CREATE INDEX IF NOT EXISTS IX_Quotation_Status ON Quotation(Status);

-- 請款書
CREATE TABLE IF NOT EXISTS Invoice (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    QuotationId INTEGER,
    IssueDate DATE NOT NULL,
    DueDate DATE NOT NULL,
    Currency TEXT NOT NULL,
    Subtotal DECIMAL(18,2),
    TaxRate DECIMAL(5,2),
    TaxAmount DECIMAL(18,2),
    Total DECIMAL(18,2) NOT NULL,
    PaidAmount DECIMAL(18,2),
    Status TEXT NOT NULL,
    PaymentTerms TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id),
    FOREIGN KEY (QuotationId) REFERENCES Quotation(Id)
);
CREATE INDEX IF NOT EXISTS IX_Invoice_InvoiceNo ON Invoice(InvoiceNo);
CREATE INDEX IF NOT EXISTS IX_Invoice_CustomerId ON Invoice(CustomerId);
CREATE INDEX IF NOT EXISTS IX_Invoice_Status ON Invoice(Status);

-- 報関単
CREATE TABLE IF NOT EXISTS CustomsDeclaration (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DeclarationNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    InvoiceId INTEGER,
    IssueDate DATE NOT NULL,
    ExportDate DATE,
    Currency TEXT NOT NULL DEFAULT 'USD',
    Incoterms TEXT,
    CountryOfOrigin TEXT,
    PortOfLoading TEXT,
    PortOfDischarge TEXT,
    TotalValue DECIMAL(18,2) NOT NULL DEFAULT 0,
    GrossWeight DECIMAL(10,3),
    NetWeight DECIMAL(10,3),
    Status TEXT NOT NULL DEFAULT 'draft',
    Notes TEXT,
    ExporterName TEXT,
    ImporterName TEXT,
    CargoDescription TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id),
    FOREIGN KEY (InvoiceId) REFERENCES Invoice(Id)
);
CREATE INDEX IF NOT EXISTS IX_CustomsDeclaration_DeclarationNo ON CustomsDeclaration(DeclarationNo);
CREATE INDEX IF NOT EXISTS IX_CustomsDeclaration_CustomerId ON CustomsDeclaration(CustomerId);
CREATE INDEX IF NOT EXISTS IX_CustomsDeclaration_Status ON CustomsDeclaration(Status);

-- PDF テンプレートカテゴリ
CREATE TABLE IF NOT EXISTS PdfTemplateCategory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    Description TEXT,
    SortOrder INTEGER DEFAULT 0,
    IsEnabled INTEGER DEFAULT 1,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS IX_PdfTemplateCategory_Code ON PdfTemplateCategory(Code);

-- PDF テンプレート
CREATE TABLE IF NOT EXISTS PdfTemplate (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TemplateNo TEXT NOT NULL,
    CategoryId INTEGER NOT NULL,
    Name TEXT NOT NULL,
    NameEn TEXT,
    Description TEXT,
    FileName TEXT NOT NULL,
    PageSize TEXT NOT NULL,
    Orientation TEXT NOT NULL,
    Theme TEXT,
    HeaderColor TEXT,
    IsDefault INTEGER DEFAULT 0,
    SortOrder INTEGER DEFAULT 0,
    Status TEXT NOT NULL,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CategoryId) REFERENCES PdfTemplateCategory(Id)
);
CREATE INDEX IF NOT EXISTS IX_PdfTemplate_TemplateNo ON PdfTemplate(TemplateNo);
CREATE INDEX IF NOT EXISTS IX_PdfTemplate_CategoryId ON PdfTemplate(CategoryId);
CREATE INDEX IF NOT EXISTS IX_PdfTemplate_Status ON PdfTemplate(Status);

-- 見積書（日本国内用）
CREATE TABLE IF NOT EXISTS JpEstimate (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EstimateNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    ExpiryDate DATE,
    Subtotal DECIMAL(18,2),
    TaxRate DECIMAL(5,2),
    TaxAmount DECIMAL(18,2),
    Total DECIMAL(18,2) NOT NULL,
    Status TEXT NOT NULL,
    ValidityPeriod TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpEstimate_EstimateNo ON JpEstimate(EstimateNo);
CREATE INDEX IF NOT EXISTS IX_JpEstimate_CustomerId ON JpEstimate(CustomerId);

-- 請求書（日本国内用）
CREATE TABLE IF NOT EXISTS JpInvoice (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    DueDate DATE NOT NULL,
    Subtotal DECIMAL(18,2),
    TaxRate DECIMAL(5,2),
    TaxAmount DECIMAL(18,2),
    Total DECIMAL(18,2) NOT NULL,
    PaidAmount DECIMAL(18,2),
    Status TEXT NOT NULL,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpInvoice_InvoiceNo ON JpInvoice(InvoiceNo);
CREATE INDEX IF NOT EXISTS IX_JpInvoice_CustomerId ON JpInvoice(CustomerId);

-- 納品書（日本国内用）
CREATE TABLE IF NOT EXISTS JpDelivery (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DeliveryNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    DeliveryDate DATE,
    Total DECIMAL(18,2) NOT NULL,
    Status TEXT NOT NULL,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpDelivery_DeliveryNo ON JpDelivery(DeliveryNo);
CREATE INDEX IF NOT EXISTS IX_JpDelivery_CustomerId ON JpDelivery(CustomerId);

-- 契約書台帳
CREATE TABLE IF NOT EXISTS JpContract (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ContractNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    Title TEXT NOT NULL,
    StartDate DATE,
    EndDate DATE,
    Amount DECIMAL(18,2),
    Status TEXT NOT NULL,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpContract_ContractNo ON JpContract(ContractNo);
CREATE INDEX IF NOT EXISTS IX_JpContract_CustomerId ON JpContract(CustomerId);

-- 領収書（日本国内用）
CREATE TABLE IF NOT EXISTS JpReceipt (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReceiptNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    PaymentMethod TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpReceipt_ReceiptNo ON JpReceipt(ReceiptNo);
CREATE INDEX IF NOT EXISTS IX_JpReceipt_CustomerId ON JpReceipt(CustomerId);

-- 送付状（日本国内用）
CREATE TABLE IF NOT EXISTS JpDeliverySlip (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SlipNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    DeliveryDate DATE,
    TotalPackages INTEGER,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpDeliverySlip_SlipNo ON JpDeliverySlip(SlipNo);
CREATE INDEX IF NOT EXISTS IX_JpDeliverySlip_CustomerId ON JpDeliverySlip(CustomerId);

-- 請求書（銀行用）
CREATE TABLE IF NOT EXISTS JpInvoiceBank (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    DueDate DATE NOT NULL,
    Subtotal DECIMAL(18,2),
    TaxRate DECIMAL(5,2),
    TaxAmount DECIMAL(18,2),
    Total DECIMAL(18,2) NOT NULL,
    BankName TEXT,
    BranchName TEXT,
    AccountType TEXT,
    AccountNumber TEXT,
    AccountHolder TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpInvoiceBank_InvoiceNo ON JpInvoiceBank(InvoiceNo);
CREATE INDEX IF NOT EXISTS IX_JpInvoiceBank_CustomerId ON JpInvoiceBank(CustomerId);

-- 請求書（青いデザイン）
CREATE TABLE IF NOT EXISTS JpInvoiceBlue (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    DueDate DATE NOT NULL,
    Subtotal DECIMAL(18,2),
    TaxRate DECIMAL(5,2),
    TaxAmount DECIMAL(18,2),
    Total DECIMAL(18,2) NOT NULL,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpInvoiceBlue_InvoiceNo ON JpInvoiceBlue(InvoiceNo);
CREATE INDEX IF NOT EXISTS IX_JpInvoiceBlue_CustomerId ON JpInvoiceBlue(CustomerId);

-- 請求書（標準デザイン）
CREATE TABLE IF NOT EXISTS JpInvoiceStandard (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNo TEXT NOT NULL,
    CustomerId INTEGER NOT NULL,
    IssueDate DATE NOT NULL,
    DueDate DATE NOT NULL,
    Subtotal DECIMAL(18,2),
    TaxRate DECIMAL(5,2),
    TaxAmount DECIMAL(18,2),
    Total DECIMAL(18,2) NOT NULL,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
);
CREATE INDEX IF NOT EXISTS IX_JpInvoiceStandard_InvoiceNo ON JpInvoiceStandard(InvoiceNo);
CREATE INDEX IF NOT EXISTS IX_JpInvoiceStandard_CustomerId ON JpInvoiceStandard(CustomerId);

-- 履歴書（日本用）
CREATE TABLE IF NOT EXISTS JpResume (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ResumeNo TEXT NOT NULL,
    ApplicantName TEXT NOT NULL,
    BirthDate DATE,
    Gender TEXT,
    Address TEXT,
    Phone TEXT,
    Email TEXT,
    EducationHistory TEXT,
    WorkHistory TEXT,
    Skills TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS IX_JpResume_ResumeNo ON JpResume(ResumeNo);

-- 会議録
CREATE TABLE IF NOT EXISTS Meeting (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MeetingNo TEXT NOT NULL,
    Title TEXT NOT NULL,
    MeetingDate DATETIME NOT NULL,
    Location TEXT,
    Organizer TEXT,
    Participants TEXT,
    Agenda TEXT,
    Decisions TEXT,
    ActionItems TEXT,
    NextMeetingDate DATETIME,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS IX_Meeting_MeetingNo ON Meeting(MeetingNo);
CREATE INDEX IF NOT EXISTS IX_Meeting_MeetingDate ON Meeting(MeetingDate);

-- ファックス表紙
CREATE TABLE IF NOT EXISTS FaxCover (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FaxNo TEXT NOT NULL,
    SenderName TEXT NOT NULL,
    SenderCompany TEXT,
    SenderPhone TEXT,
    SenderFax TEXT,
    RecipientName TEXT NOT NULL,
    RecipientCompany TEXT,
    RecipientFax TEXT,
    SendDate DATETIME NOT NULL,
    TotalPages INTEGER,
    Subject TEXT,
    Message TEXT,
    Notes TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS IX_FaxCover_FaxNo ON FaxCover(FaxNo);
CREATE INDEX IF NOT EXISTS IX_FaxCover_SendDate ON FaxCover(SendDate);

-- 初期データ登録
-- PDF テンプレートカテゴリ
INSERT OR IGNORE INTO PdfTemplateCategory (Code, Name, Description, SortOrder, IsEnabled) VALUES
('TRADE', '貿易文書', '輸出入関連の文書テンプレート', 10, 1),
('DOMESTIC', '国内文書', '日本国内向けの文書テンプレート', 20, 1),
('TEMPLATE', 'テンプレート管理', 'PDF テンプレート設定', 30, 1);

-- 取引先マスタ（サンプル）
INSERT OR IGNORE INTO Customer (Code, Name, NameEn, Country, ContactPerson, Phone, Email, TaxId, Address) VALUES
('CUST-001', '上海貿易有限公司', 'Shanghai Trading Co., Ltd.', 'CN', '張三', '+86-21-1234-5678', 'zhang@shanghai-trading.cn', '91310000MA1234567X', '上海市浦東新区世紀大道 100 号'),
('CUST-002', '北京技術株式会社', 'Beijing Technology Corp.', 'CN', '李四', '+86-10-8765-4321', 'li@beijing-tech.cn', '91110000MA9876543Y', '北京市朝陽区建国門外大街 50 号'),
('CUST-003', '東京商事株式会社', 'Tokyo Shoji Co., Ltd.', 'JP', '田中太郎', '+81-3-1234-5678', 'tanaka@tokyo-shoji.jp', '1234567890123', '東京都千代田区丸の内 1-1-1'),
('CUST-004', '大阪工業株式会社', 'Osaka Kogyo Corp.', 'JP', '山田花子', '+81-6-8765-4321', 'yamada@osaka-kogyo.jp', '2345678901234', '大阪市北区梅田 2-2-2');
