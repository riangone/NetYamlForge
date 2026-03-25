-- ============================================================
-- PDF Template Sample Data for framework-showcase
-- ============================================================
-- This script creates sample data for demonstrating PDF templates.
-- Run after init_seed.sql to add PDF-ready sample transactions.
-- ============================================================

-- ============================================================
-- Sample Customer data for PDF templates
-- ============================================================
INSERT OR REPLACE INTO Customer (Code, Name, NameEn, Address, AddressEn, ContactPerson, Phone, Email, TaxId, Country) VALUES
  ('PDF-C001', '山田商事株式会社', 'Yamada Shoji Co., Ltd.', '東京都港区赤坂 1-2-3', '1-2-3 Akasaka, Minato-ku, Tokyo', '山田太郎', '+81-3-1234-5678', 'yamada@example.co.jp', 'T1234567890123', 'JP'),
  ('PDF-C002', '鈴木工業株式会社', 'Suzuki Kogyo Co., Ltd.', '大阪府大阪市北区梅田 4-5-6', '4-5-6 Umeda, Kita-ku, Osaka', '鈴木一郎', '+81-6-9876-5432', 'suzuki@example.co.jp', 'T9876543210987', 'JP'),
  ('PDF-C003', '佐藤物産株式会社', 'Sato Bussan Co., Ltd.', '愛知県名古屋市中区栄 7-8-9', '7-8-9 Sakae, Naka-ku, Nagoya', '佐藤花子', '+81-52-1111-2222', 'sato@example.co.jp', 'T1111222233334', 'JP');

-- ============================================================
-- Sample JpInvoice data for invoice.yaml PDF template
-- ============================================================
INSERT OR REPLACE INTO JpInvoice (InvoiceNo, CustomerId, Title, IssueDate, DueDate,
  RegistrationNo, Subtotal10, TaxAmount10, Subtotal8, TaxAmount8,
  Subtotal, TaxAmount, Total,
  BankName, BranchName, AccountType, AccountNo, AccountHolder,
  TransferFeeNote, Status, PreparedBy, CompanyStamp) VALUES
  ('INV-PDF-001', 1, 'Web システム開発費用（2026 年 3 月分）', '2026-03-25', '2026-04-25',
   'T1234567890123', 500000, 50000, 0, 0, 500000, 50000, 550000,
   'みずほ銀行', '赤坂支店', '普通', '1234567', 'カ）ヤマダショウジ',
   '振込手数料はご負担くださいますようお願い申し上げます。', 'issued', '田中 太郎', '株式会社テックソリューション'),
  ('INV-PDF-002', 2, 'サーバー保守サービス（2026 年 4 月分）', '2026-04-01', '2026-04-30',
   'T1234567890123', 150000, 15000, 0, 0, 150000, 15000, 165000,
   '三井住友銀行', '梅田支店', '普通', '7654321', 'カ）スズキコウギョウ',
   '振込手数料はご負担くださいますようお願い申し上げます。', 'issued', '鈴木 花子', '株式会社テックソリューション'),
  ('INV-PDF-003', 3, 'ネットワーク機器導入費用', '2026-04-10', '2026-05-10',
   'T1234567890123', 800000, 80000, 0, 0, 800000, 80000, 880000,
   '三菱 UFJ 銀行', '栄支店', '普通', '9876543', 'カ）サトウブッサン',
   '振込手数料はご負担くださいますようお願い申し上げます。', 'draft', '佐藤 次郎', '株式会社テックソリューション');

-- ============================================================
-- Sample JpInvoiceItem data for invoice.yaml PDF template
-- ============================================================
INSERT OR REPLACE INTO JpInvoiceItem (InvoiceId, LineNo, ItemCode, ItemName, Spec, Unit, Quantity, UnitPrice, Amount, TaxRate) VALUES
  -- INV-PDF-001
  (1, 1, 'DEV-001', '要件定義・基本設計', '画面数 30 画面', '式', 1, 200000, 200000, 10),
  (1, 2, 'DEV-002', '詳細設計・開発', 'フロントエンド実装', '式', 1, 200000, 200000, 10),
  (1, 3, 'DEV-003', 'テスト・品質保証', '結合テスト', '式', 1, 100000, 100000, 10),
  -- INV-PDF-002
  (2, 1, 'SVC-001', 'サーバー監視（24 時間 365 日）', '月次レポート付き', '月', 1, 100000, 100000, 10),
  (2, 2, 'SVC-002', 'セキュリティパッチ適用', '月 2 回', '回', 2, 25000, 50000, 10),
  -- INV-PDF-003
  (3, 1, 'NET-001', 'コアスイッチ', 'Cisco Catalyst 9300', '台', 2, 200000, 400000, 10),
  (3, 2, 'NET-002', 'アクセススイッチ', 'Cisco Catalyst 9200', '台', 4, 100000, 400000, 10);

-- ============================================================
-- Sample JpEstimate data for estimate.yaml PDF template
-- ============================================================
INSERT OR REPLACE INTO JpEstimate (EstimateNo, CustomerId, Title, IssueDate, ExpiryDate,
  Subtotal10, TaxAmount10, Subtotal8, TaxAmount8, Subtotal, TaxAmount, Total,
  PaymentTerms, DeliveryDays, DeliveryPlace,
  ValidityNote, ExclusionNote, Status, PreparedBy, CompanyStamp) VALUES
  ('EST-PDF-001', 1, 'モバイルアプリ開発見積', '2026-03-25', '2026-04-25',
   1500000, 150000, 0, 0, 1500000, 150000, 1650000,
   '着手時 50%・完了時 50%', '受注後 90 営業日', '貴社指定場所',
   '本見積の有効期限は発行日より 30 日間とします。',
   'サーバー・インフラ費用、既存データ移行費用は別途請求となります。',
   'sent', '山田 太郎', '株式会社テックソリューション'),
  ('EST-PDF-002', 2, 'クラウド移行サービス見積', '2026-04-01', '2026-05-01',
   800000, 80000, 0, 0, 800000, 80000, 880000,
   '納品後 30 日以内', '受注後 60 営業日', '弊社オフィス',
   '本見積の有効期限は発行日より 30 日間とします。',
   'クラウドサービス利用料（AWS/Azure 等）は含まれません。',
   'draft', '鈴木 花子', '株式会社テックソリューション'),
  ('EST-PDF-003', 3, '社内研修プログラム見積', '2026-04-10', '2026-05-10',
   350000, 35000, 0, 0, 350000, 35000, 385000,
   '研修実施後 30 日以内', '要相談', '貴社会議室',
   '本見積の有効期限は発行日より 30 日間とします。',
   '交通費・宿泊費は別途実費精算となります。',
   'sent', '佐藤 次郎', '株式会社テックソリューション');

-- ============================================================
-- Sample JpEstimateItem data for estimate.yaml PDF template
-- ============================================================
INSERT OR REPLACE INTO JpEstimateItem (EstimateId, LineNo, ItemCode, ItemName, Spec, Unit, Quantity, UnitPrice, Amount, TaxRate) VALUES
  -- EST-PDF-001
  (1, 1, 'MOB-001', 'iOS アプリ開発', 'Swift/SwiftUI', '式', 1, 600000, 600000, 10),
  (1, 2, 'MOB-002', 'Android アプリ開発', 'Kotlin/Jetpack Compose', '式', 1, 600000, 600000, 10),
  (1, 3, 'MOB-003', 'API 連携', 'REST API 実装', '式', 1, 300000, 300000, 10),
  -- EST-PDF-002
  (2, 1, 'CLD-001', 'As-Is 調査・現状分析', NULL, '式', 1, 200000, 200000, 10),
  (2, 2, 'CLD-002', 'To-Be 設計・移行計画策定', NULL, '式', 1, 300000, 300000, 10),
  (2, 3, 'CLD-003', '移行実施支援（2 ヶ月）', NULL, '月', 2, 150000, 300000, 10),
  -- EST-PDF-003
  (3, 1, 'TRN-001', 'Python プログラミング研修（3 日間）', '定員 10 名', '回', 1, 250000, 250000, 10),
  (3, 2, 'TRN-002', 'テキスト・教材費', '10 名分', '冊', 10, 10000, 100000, 10);

-- ============================================================
-- Sample JpDelivery data for delivery.yaml PDF template
-- ============================================================
INSERT OR REPLACE INTO JpDelivery (DeliveryNo, CustomerId, Title, DeliveryDate, DeliveryPlace,
  Subtotal10, TaxAmount10, Subtotal8, TaxAmount8, Subtotal, TaxAmount, Total,
  InspectionPeriodDays, ReceiptConfirmedDate, Status, PreparedBy, CompanyStamp) VALUES
  ('DLV-PDF-001', 1, 'Web システム 成果物一式（第 2 フェーズ）', '2026-03-25', '山田商事株式会社 IT 部門',
   750000, 75000, 0, 0, 750000, 75000, 825000, 5, NULL, 'delivered', '田中 太郎', '株式会社テックソリューション'),
  ('DLV-PDF-002', 2, 'サーバー監視ツール導入セット', '2026-04-01', '鈴木工業株式会社 サーバー室',
   180000, 18000, 0, 0, 180000, 18000, 198000, 5, '2026-04-03', 'confirmed', '鈴木 花子', '株式会社テックソリューション'),
  ('DLV-PDF-003', 3, 'ネットワーク機器一式', '2026-04-10', '佐藤物産株式会社 本社',
   800000, 80000, 0, 0, 800000, 80000, 880000, 5, NULL, 'delivered', '佐藤 次郎', '株式会社テックソリューション');

-- ============================================================
-- Sample JpDeliveryItem data for delivery.yaml PDF template
-- ============================================================
INSERT OR REPLACE INTO JpDeliveryItem (DeliveryId, LineNo, ItemCode, ItemName, Spec, Unit, Quantity, UnitPrice, Amount, TaxRate) VALUES
  -- DLV-PDF-001
  (1, 1, 'DOC-001', '詳細設計書', 'PDF + Word 形式', '式', 1, 0, 0, 10),
  (1, 2, 'DEV-010', 'ソースコード一式', 'Git リポジトリ', '式', 1, 750000, 750000, 10),
  -- DLV-PDF-002
  (2, 1, 'SFT-001', '監視ツール ライセンス', '1 年間', '式', 1, 120000, 120000, 10),
  (2, 2, 'SFT-002', 'セキュリティエージェント', '10 ノード', '式', 1, 60000, 60000, 10),
  -- DLV-PDF-003
  (3, 1, 'NET-001', 'コアスイッチ Cisco Catalyst 9300', NULL, '台', 2, 200000, 400000, 10),
  (3, 2, 'NET-002', 'アクセススイッチ Cisco Catalyst 9200', NULL, '台', 4, 100000, 400000, 10);

-- ============================================================
-- Sample JpContract data for contract.yaml PDF template
-- ============================================================
INSERT OR REPLACE INTO JpContract (ContractNo, CustomerId, Title, ContractType, StartDate, EndDate,
  AutoRenew, ContractAmount, PaymentTerms, Status, SignedDate,
  IsElectronic, StampTaxAmount, JurisdictionCourt, GoverningLaw,
  OurSignatory, TheirSignatory, Remarks) VALUES
  ('CTR-PDF-001', 1, 'Web システム開発請負契約', '請負契約', '2026-03-01', '2026-08-31',
   0, 1650000, '着手金 50%・完了時 50%', 'active', '2026-02-25',
   1, NULL, '東京地方裁判所', '日本法',
   '代表取締役 山田 一郎', '代表取締役 山田 太郎', '仕様変更は別途書面協議'),
  ('CTR-PDF-002', 2, 'サーバー保守サービス委託契約', '業務委託契約', '2026-01-01', '2026-12-31',
   1, 1980000, '月末締め翌月払い', 'active', '2025-12-20',
   1, NULL, '東京地方裁判所', '日本法',
   '代表取締役 山田 一郎', '代表取締役 鈴木 一郎', '年次更新・前月末までに解約申告要'),
  ('CTR-PDF-003', 3, '秘密保持契約（NDA）', '秘密保持契約', '2026-01-10', NULL,
   0, NULL, NULL, 'active', '2026-01-10',
   1, NULL, '東京地方裁判所', '日本法',
   '代表取締役 山田 一郎', '代表取締役 佐藤 花子', '期間の定めなし・書面解除まで有効');
