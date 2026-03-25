-- biz-docs 测试数据插入脚本
-- 为所有业务表创建示例数据

-- 取引先マスタ（追加）
INSERT INTO Customer (Code, Name, NameEn, Country, ContactPerson, Phone, Email, TaxId, Address) VALUES
('CUST-005', '深圳貿易有限公司', 'Shenzhen Trade Ltd.', 'CN', '王五', '+86-755-1234-5678', 'wang@shenzhen-trade.cn', '91440300MA5ABCDE1X', '深圳市福田区深南大道 1000 号'),
('CUST-006', '横浜商事株式会社', 'Yokohama Shoji Co., Ltd.', 'JP', '佐藤一郎', '+81-45-1234-5678', 'sato@yokohama-shoji.jp', '3456789012345', '横浜市西区みなとみらい 2-2-1'),
('CUST-007', 'シンガポール貿易 Pte Ltd', 'Singapore Trading Pte Ltd', 'SG', 'Tan Wei Ming', '+65-6123-4567', 'tan@singapore-trade.sg', '201234567K', '10 Anson Road #15-01 Singapore 079903'),
('CUST-008', '台北科技股分有限公司', 'Taipei Technology Co., Ltd.', 'TW', '陳志明', '+886-2-1234-5678', 'chen@taipei-tech.tw', '12345678', '台北市信義區信義路五段 100 号');

-- 報価単
INSERT INTO Quotation (QuoteNo, CustomerId, IssueDate, ExpiryDate, Currency, Subtotal, TaxRate, TaxAmount, Total, Status, PaymentTerms, DeliveryTerms, Notes) VALUES
('QT-2026-001', 1, '2026-01-15', '2026-02-14', 'USD', 10000.00, 10.00, 1000.00, 11000.00, 'sent', 'T/T 30 days', 'FOB Shanghai', '初期見積もり'),
('QT-2026-002', 2, '2026-01-20', '2026-02-19', 'EUR', 8500.00, 19.00, 1615.00, 10115.00, 'accepted', 'L/C at sight', 'CIF Hamburg', 'VIP 顧客向け特別価格'),
('QT-2026-003', 3, '2026-02-01', '2026-03-02', 'JPY', 1500000.00, 10.00, 150000.00, 1650000.00, 'draft', 'Bank transfer', 'EXW Tokyo', '新規顧客用サンプル見積'),
('QT-2026-004', 4, '2026-02-10', '2026-03-11', 'CNY', 50000.00, 6.00, 3000.00, 53000.00, 'rejected', 'T/T 60 days', 'DDP Beijing', '価格条件合意せず'),
('QT-2026-005', 5, '2026-02-15', '2026-03-16', 'USD', 25000.00, 8.00, 2000.00, 27000.00, 'expired', 'T/T 45 days', 'FOB Shenzhen', '有効期限切れ');

-- 請款書
INSERT INTO Invoice (InvoiceNo, CustomerId, QuotationId, IssueDate, DueDate, Currency, Subtotal, TaxRate, TaxAmount, Total, PaidAmount, Status, PaymentTerms, Notes) VALUES
('INV-2026-001', 1, 1, '2026-01-20', '2026-02-19', 'USD', 10000.00, 10.00, 1000.00, 11000.00, 11000.00, 'paid', 'T/T 30 days', '入金確認済'),
('INV-2026-002', 2, 2, '2026-01-25', '2026-02-24', 'EUR', 8500.00, 19.00, 1615.00, 10115.00, 5000.00, 'issued', 'L/C at sight', '一部入金待ち'),
('INV-2026-003', 3, NULL, '2026-02-05', '2026-03-06', 'JPY', 2000000.00, 10.00, 200000.00, 2200000.00, 0.00, 'overdue', 'Bank transfer', '支払期限超過'),
('INV-2026-004', 4, NULL, '2026-02-15', '2026-03-16', 'CNY', 75000.00, 6.00, 4500.00, 79500.00, 0.00, 'cancelled', 'T/T 60 days', 'キャンセル済'),
('INV-2026-005', 5, NULL, '2026-02-20', '2026-03-21', 'USD', 15000.00, 8.00, 1200.00, 16200.00, 0.00, 'draft', 'T/T 45 days', '下書保存中');

-- 報関単
INSERT INTO CustomsDeclaration (DeclarationNo, CustomerId, InvoiceId, IssueDate, ExportDate, Currency, Incoterms, CountryOfOrigin, PortOfLoading, PortOfDischarge, TotalValue, GrossWeight, NetWeight, Status, Notes) VALUES
('CD-2026-001', 1, 1, '2026-01-22', '2026-01-25', 'USD', 'FOB', 'CN', 'Shanghai', 'Tokyo', 11000.00, 500.500, 480.200, 'cleared', '通関完了'),
('CD-2026-002', 2, 2, '2026-01-28', '2026-02-01', 'EUR', 'CIF', 'DE', 'Hamburg', 'Yokohama', 10115.00, 750.000, 720.500, 'approved', '検査合格'),
('CD-2026-003', 3, NULL, '2026-02-08', '2026-02-12', 'JPY', 'EXW', 'JP', 'Tokyo', 'Singapore', 2200000.00, 300.000, 285.000, 'submitted', '審査中'),
('CD-2026-004', 5, NULL, '2026-02-22', '2026-02-26', 'USD', 'DDP', 'SG', 'Singapore', 'Shenzhen', 16200.00, 200.000, 190.000, 'draft', '書類作成中'),
('CD-2026-005', 4, NULL, '2026-02-18', NULL, 'CNY', 'DAP', 'CN', 'Ningbo', 'Osaka', 79500.00, NULL, NULL, 'rejected', '書類不備により差戻し');

-- PDF テンプレート（追加）
INSERT INTO PdfTemplate (TemplateNo, CategoryId, Name, NameEn, Description, FileName, PageSize, Orientation, Theme, HeaderColor, IsDefault, SortOrder, Status) VALUES
('TMPL-001', 1, '標準報価単', 'Standard Quotation', '標準的な報価単テンプレート', 'quotation-standard.pdf', 'A4', 'portrait', 'standard', '#1c3658', 1, 10, 'active'),
('TMPL-002', 1, 'ブルー請款書', 'Blue Invoice', '青いデザインの請款書', 'invoice-blue.pdf', 'A4', 'portrait', 'blue', '#2563eb', 0, 20, 'active'),
('TMPL-003', 2, '和風請求書', 'Japanese Invoice', '伝統的な和風デザインの請求書', 'invoice-japanese.pdf', 'B5', 'portrait', 'standard', '#8b4513', 0, 30, 'active'),
('TMPL-004', 3, 'シンプル納品書', 'Simple Delivery', 'シンプルな納品書テンプレート', 'delivery-simple.pdf', 'A4', 'landscape', 'gray', '#6b7280', 0, 40, 'inactive'),
('TMPL-005', 1, '英語報関単', 'English Customs Declaration', '英語版報関単テンプレート', 'customs-english.pdf', 'A4', 'portrait', 'standard', '#059669', 0, 50, 'draft');

-- 見積書（日本国内用）
INSERT INTO JpEstimate (EstimateNo, CustomerId, IssueDate, ExpiryDate, Subtotal, TaxRate, TaxAmount, Total, Status, ValidityPeriod, Notes) VALUES
('EST-2026-001', 3, '2026-01-10', '2026-02-09', 100000.00, 10.00, 10000.00, 110000.00, 'accepted', '30 日間', '国内顧客用見積'),
('EST-2026-002', 4, '2026-01-25', '2026-02-24', 250000.00, 10.00, 25000.00, 275000.00, 'sent', '60 日間', '大口顧客特別価格'),
('EST-2026-003', 6, '2026-02-05', '2026-03-06', 50000.00, 10.00, 5000.00, 55000.00, 'draft', '30 日間', '新規顧客用サンプル'),
('EST-2026-004', 3, '2026-02-15', '2026-03-16', 180000.00, 10.00, 18000.00, 198000.00, 'rejected', '30 日間', '価格条件合意せず'),
('EST-2026-005', 4, '2026-02-20', '2026-03-21', 320000.00, 10.00, 32000.00, 352000.00, 'expired', '30 日間', '有効期限切れ');

-- 請求書（日本国内用）
INSERT INTO JpInvoice (InvoiceNo, CustomerId, IssueDate, DueDate, Subtotal, TaxRate, TaxAmount, Total, PaidAmount, Status, Notes) VALUES
('JP-INV-2026-001', 3, '2026-01-15', '2026-02-14', 100000.00, 10.00, 10000.00, 110000.00, 110000.00, 'paid', '入金確認済'),
('JP-INV-2026-002', 4, '2026-01-30', '2026-02-28', 250000.00, 10.00, 25000.00, 275000.00, 100000.00, 'issued', '一部入金待ち'),
('JP-INV-2026-003', 6, '2026-02-10', '2026-03-11', 50000.00, 10.00, 5000.00, 55000.00, 0.00, 'overdue', '支払期限超過'),
('JP-INV-2026-004', 3, '2026-02-20', '2026-03-20', 180000.00, 10.00, 18000.00, 198000.00, 0.00, 'draft', '下書保存中'),
('JP-INV-2026-005', 4, '2026-02-25', '2026-03-26', 80000.00, 10.00, 8000.00, 88000.00, 0.00, 'cancelled', 'キャンセル済');

-- 請求書（標準デザイン）
INSERT INTO JpInvoiceStandard (InvoiceNo, CustomerId, IssueDate, DueDate, Subtotal, TaxRate, TaxAmount, Total, Notes) VALUES
('STD-2026-001', 3, '2026-01-20', '2026-02-19', 120000.00, 10.00, 12000.00, 132000.00, '標準テンプレート使用'),
('STD-2026-002', 4, '2026-02-01', '2026-03-02', 95000.00, 10.00, 9500.00, 104500.00, '毎月定期請求'),
('STD-2026-003', 6, '2026-02-15', '2026-03-16', 200000.00, 10.00, 20000.00, 220000.00, '大口割引適用後');

-- 請求書（青いデザイン）
INSERT INTO JpInvoiceBlue (InvoiceNo, CustomerId, IssueDate, DueDate, Subtotal, TaxRate, TaxAmount, Total, Notes) VALUES
('BLU-2026-001', 3, '2026-01-25', '2026-02-24', 75000.00, 10.00, 7500.00, 82500.00, '青いデザインテンプレート'),
('BLU-2026-002', 6, '2026-02-05', '2026-03-06', 150000.00, 10.00, 15000.00, 165000.00, '新規顧客用'),
('BLU-2026-003', 4, '2026-02-18', '2026-03-19', 88000.00, 10.00, 8800.00, 96800.00, '特別価格適用');

-- 請求書（銀行用）
INSERT INTO JpInvoiceBank (InvoiceNo, CustomerId, IssueDate, DueDate, Subtotal, TaxRate, TaxAmount, Total, BankName, BranchName, AccountType, AccountNumber, AccountHolder, Notes) VALUES
('BNK-2026-001', 3, '2026-01-28', '2026-02-27', 90000.00, 10.00, 9000.00, 99000.00, '三菱 UFJ 銀行', '東京支店', '普通', '1234567', '株式会社サンプル', '銀行振込専用'),
('BNK-2026-002', 4, '2026-02-08', '2026-03-09', 110000.00, 10.00, 11000.00, 121000.00, '三井住友銀行', '大阪支店', '当座', '7654321', '株式会社テスト', '口座振替対応'),
('BNK-2026-003', 6, '2026-02-20', '2026-03-21', 65000.00, 10.00, 6500.00, 71500.00, 'みずほ銀行', '横浜支店', '普通', '9876543', '合同会社ヤマダ', '振込手数料負担');

-- 納品書
INSERT INTO JpDelivery (DeliveryNo, CustomerId, IssueDate, DeliveryDate, Total, Status, Notes) VALUES
('DLV-2026-001', 3, '2026-01-18', '2026-01-20', 110000.00, 'delivered', '配送完了'),
('DLV-2026-002', 4, '2026-02-03', '2026-02-05', 275000.00, 'received', '受領確認済'),
('DLV-2026-003', 6, '2026-02-12', '2026-02-14', 55000.00, 'delivered', '配送中'),
('DLV-2026-004', 3, '2026-02-22', NULL, 198000.00, 'draft', '出荷準備中'),
('DLV-2026-005', 4, '2026-02-26', NULL, 88000.00, 'draft', '在庫手配中');

-- 送付状
INSERT INTO JpDeliverySlip (SlipNo, CustomerId, IssueDate, DeliveryDate, TotalPackages, Notes) VALUES
('SLP-2026-001', 3, '2026-01-18', '2026-01-20', 3, '書類 3 点同梱'),
('SLP-2026-002', 4, '2026-02-03', '2026-02-05', 5, 'サンプル品同梱'),
('SLP-2026-003', 6, '2026-02-12', '2026-02-14', 2, '請求書・納品書同封'),
('SLP-2026-004', 3, '2026-02-22', NULL, 1, '出荷予定'),
('SLP-2026-005', 4, '2026-02-26', NULL, 4, '複数口配送');

-- 領収書
INSERT INTO JpReceipt (ReceiptNo, CustomerId, IssueDate, Amount, PaymentMethod, Notes) VALUES
('RCP-2026-001', 3, '2026-01-20', 110000.00, '銀行振込', '入金確認済'),
('RCP-2026-002', 4, '2026-02-10', 100000.00, '小切手', '一部入金'),
('RCP-2026-003', 6, '2026-02-28', 55000.00, '現金', '窓口で受領'),
('RCP-2026-004', 3, '2026-03-05', 50000.00, '銀行振込', '分割 1 回目'),
('RCP-2026-005', 4, '2026-03-10', 50000.00, '銀行振込', '分割 2 回目');

-- 契約書台帳
INSERT INTO JpContract (ContractNo, CustomerId, Title, StartDate, EndDate, Amount, Status, Notes) VALUES
('CNT-2026-001', 3, '基本取引契約', '2026-01-01', '2026-12-31', NULL, 'active', '年間基本契約'),
('CNT-2026-002', 4, '製品供給契約', '2026-02-01', '2027-01-31', 5000000.00, 'active', '年間供給契約'),
('CNT-2026-003', 6, '保守サービス契約', '2026-03-01', '2027-02-28', 1200000.00, 'draft', '保守契約書'),
('CNT-2025-001', 3, '旧基本契約', '2025-01-01', '2025-12-31', NULL, 'completed', '契約期間終了'),
('CNT-2025-002', 4, '解約済契約', '2025-06-01', '2025-11-30', 3000000.00, 'terminated', '中途解約済');

-- 履歴書
INSERT INTO JpResume (ResumeNo, ApplicantName, BirthDate, Gender, Address, Phone, Email, EducationHistory, WorkHistory, Skills, Notes) VALUES
('RES-2026-001', '田中太郎', '1990-05-15', 'Male', '東京都渋谷区 1-2-3', '090-1234-5678', 'tanaka@example.com', 
'2009 年 東京大学入学/2013 年 卒業', 
'2013 年 株式会社サンプル入社/2020 年 退職', 
'Java, Python, 英語ビジネスレベル', '管理職経験あり'),
('RES-2026-002', '山田花子', '1995-08-20', 'Female', '神奈川県横浜市 4-5-6', '080-9876-5432', 'yamada@example.com',
'2014 年 慶應義塾大学入学/2018 年 卒業',
'2018 年 株式会社テスト入社/現在に至る',
'Excel, PowerPoint, 中国語 HSK5 級', '経理経験 5 年'),
('RES-2026-003', '鈴木一郎', '1988-12-03', 'Male', '大阪府大阪市 7-8-9', '070-5555-6666', 'suzuki@example.com',
'2007 年 大阪大学入学/2011 年 卒業/2013 年 修士課程修了',
'2013 年 株式会社ヤマダ入社/2018 年 独立',
'C++, JavaScript, プロジェクト管理', 'PMP 資格保有');

-- ファックス表紙
INSERT INTO FaxCover (FaxNo, SenderName, SenderCompany, SenderPhone, SenderFax, RecipientName, RecipientCompany, RecipientFax, SendDate, TotalPages, Subject, Message, Notes) VALUES
('FAX-2026-001', '田中', '株式会社サンプル', '03-1234-5678', '03-1234-5679', '佐藤', '株式会社テスト', '03-9876-5432', '2026-01-15 09:30:00', 5, '見積書送付の件', 'お世話になっております。見積書を送付いたします。', '至急確認希望'),
('FAX-2026-002', '鈴木', '株式会社ヤマダ', '06-5555-6666', '06-5555-6667', '高橋', '株式会社サクラ', '06-1111-2222', '2026-02-01 14:15:00', 3, '契約書確認のお願い', '契約書の内容をご確認ください。', '2/5 までにご返信ください'),
('FAX-2026-003', '伊藤', '合同会社イノウエ', '045-333-4444', '045-333-4445', '中村', '株式会社スズキ', '045-777-8888', '2026-02-15 11:00:00', 2, 'お問い合せの件', 'お問い合わせいただきました件につきまして、', '添付資料ご覧ください'),
('FAX-2026-004', '小林', '株式会社アオキ', '052-999-0000', '052-999-0001', '渡辺', '株式会社タナカ', '052-222-3333', '2026-02-20 16:45:00', 10, '製品カタログ送付', '新製品カタログをお送りいたします。', 'ご検討のほどよろしくお願い申し上げます'),
('FAX-2026-005', '加藤', '株式会社ハヤシ', '092-444-5555', '092-444-5556', '松本', '株式会社イシカワ', '092-666-7777', '2026-02-25 10:20:00', 1, '会議日程調整', '次回会議の日程調整につきまして、', '3 候補日を送付いたします');

-- 会議録
INSERT INTO Meeting (MeetingNo, Title, MeetingDate, Location, Organizer, Participants, Agenda, Decisions, ActionItems, NextMeetingDate, Notes) VALUES
('MTG-2026-001', '第 1 回プロジェクトキックオフ', '2026-01-10 10:00:00', '会議室 A', '田中太郎', '田中，佐藤，鈴木，山田', 
'1. プロジェクト概要説明/2. メンバー紹介/3. 役割分担/4. スケジュール確認',
'1. PM:田中/2. 開発：佐藤/3. テスト：鈴木/4. 週次報告：毎週月曜',
'1. 要件定義書作成（佐藤/1/20 まで）/2. 環境構築（鈴木/1/25 まで）', '2026-01-17 10:00:00', '資料は共有フォルダに格納'),
('MTG-2026-002', '要件定義レビュー', '2026-01-20 14:00:00', '会議室 B', '佐藤', '田中，佐藤，山田',
'1. 要件定義書確認/2. 変更点議論/3. 承認',
'1. 機能 A 追加承認/2. 機能 B 仕様変更/3. 要件定義書承認済',
'1. 基本設計書作成（佐藤/2/5 まで）/2. コスト見積もり（山田/1/25 まで）', '2026-01-27 14:00:00', '承認書は別途提出'),
('MTG-2026-003', '中間報告会', '2026-02-05 15:00:00', 'オンライン', '田中太郎', '田中，佐藤，鈴木，山田，高橋',
'1. 進捗報告/2. 課題共有/3. 対応方針決定',
'1. 進捗率 60%/2. リソース追加承認/3. 納期変更なし',
'1. 追加開発者手配（田中/2/10 まで）/2. テスト計画書作成（鈴木/2/15 まで）', '2026-02-19 15:00:00', '次回は対面開催予定'),
('MTG-2026-004', 'テスト結果報告', '2026-02-15 11:00:00', '会議室 A', '鈴木', '田中，佐藤，鈴木',
'1. テスト結果報告/2. 不具合対応方針/3. リリース判断',
'1. 軽微な不具合 3 件/2. 修正後リリース/3. リリース日：2/25',
'1. 不具合修正（佐藤/2/18 まで）/2. 再テスト（鈴木/2/20 まで）', '2026-02-22 11:00:00', 'リリース準備完了'),
('MTG-2026-005', 'プロジェクト完了報告', '2026-02-28 16:00:00', '会議室 C', '田中太郎', '田中，佐藤，鈴木，山田，高橋，松本',
'1. プロジェクト総括/2. 成果物確認/3. 振り返り',
'1. プロジェクト完了承認/2. 顧客満足度 95%/3. 予算内完了',
'1. 運用マニュアル整備（山田/3/5 まで）/2. 最終請求書発行（松本/3/10 まで）', NULL, 'プロジェクト完了おめでとうございます');

-- 更新情報確認
SELECT 'Customer' as TableName, COUNT(*) as RecordCount FROM Customer
UNION ALL SELECT 'Quotation', COUNT(*) FROM Quotation
UNION ALL SELECT 'Invoice', COUNT(*) FROM Invoice
UNION ALL SELECT 'CustomsDeclaration', COUNT(*) FROM CustomsDeclaration
UNION ALL SELECT 'PdfTemplate', COUNT(*) FROM PdfTemplate
UNION ALL SELECT 'PdfTemplateCategory', COUNT(*) FROM PdfTemplateCategory
UNION ALL SELECT 'JpEstimate', COUNT(*) FROM JpEstimate
UNION ALL SELECT 'JpInvoice', COUNT(*) FROM JpInvoice
UNION ALL SELECT 'JpInvoiceStandard', COUNT(*) FROM JpInvoiceStandard
UNION ALL SELECT 'JpInvoiceBlue', COUNT(*) FROM JpInvoiceBlue
UNION ALL SELECT 'JpInvoiceBank', COUNT(*) FROM JpInvoiceBank
UNION ALL SELECT 'JpDelivery', COUNT(*) FROM JpDelivery
UNION ALL SELECT 'JpDeliverySlip', COUNT(*) FROM JpDeliverySlip
UNION ALL SELECT 'JpReceipt', COUNT(*) FROM JpReceipt
UNION ALL SELECT 'JpContract', COUNT(*) FROM JpContract
UNION ALL SELECT 'JpResume', COUNT(*) FROM JpResume
UNION ALL SELECT 'FaxCover', COUNT(*) FROM FaxCover
UNION ALL SELECT 'Meeting', COUNT(*) FROM Meeting;
