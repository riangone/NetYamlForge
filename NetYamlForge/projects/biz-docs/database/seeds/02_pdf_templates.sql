-- PDF テンプレート 初期データ
-- 作成日：2026-03-24
-- ソース：templates/business/ 以下の PDF ファイル

-- カテゴリ ID 取得用（後で置換）
-- INVOICE: 1, INVOICE_JP: 2, INVOICE_EN: 3
-- ESTIMATE: 4, QUOTE: 5, DELIVERY: 6
-- RECEIPT: 7, ORDER: 8, CONTRACT: 9
-- MINUTES: 10, REPORT: 11, FAX: 12
-- MEMO: 13, RESUME: 14, ATTENDANCE: 15, OTHER: 16

-- ═══════════════════════════════════════════════════════════════════════════════
-- 請求書テンプレート (INVOICE_JP: カテゴリ 2)
-- ═══════════════════════════════════════════════════════════════════════════════

INSERT INTO PdfTemplate (TemplateNo, CategoryId, Name, NameEn, Description, FileName, PageSize, Orientation, Theme, HeaderColor, IsDefault, SortOrder, Status, Remarks) VALUES
('INV-STD-001', 2, '請求書 - 標準', 'Invoice Standard', '日本標準の請求書テンプレート。青系の配色。', 'invoice-standard.pdf', 'A4', 'portrait', 'standard', '#1c3658', 1, 1, 'active', '基本テンプレート'),
('INV-BANK-001', 2, '請求書 - 銀行振込', 'Invoice with Bank Details', '銀行振込先詳細付き請求書。', 'invoice-bank.pdf', 'A4', 'portrait', 'bank', '#0F4C75', 0, 2, 'active', '振込先明記'),
('INV-BLUE-001', 2, '請求書 - 青', 'Invoice Blue', 'モダンな青系デザイン。', 'invoice-blue.pdf', 'A4', 'portrait', 'blue', '#0066CC', 0, 3, 'active', 'シンプルデザイン'),
('INV-GRAY-001', 2, '請求書 - グレー', 'Invoice Gray', '落ち着いたグレー系。', 'invoice-gray.pdf', 'A4', 'portrait', 'gray', '#4B5563', 0, 4, 'active', 'ビジネス向け'),
('INV-ORANGE-001', 2, '請求書 - オレンジ', 'Invoice Orange', '暖かみのあるオレンジ系。', 'invoice-orange.pdf', 'A4', 'portrait', 'orange', '#EA580C', 0, 5, 'active', 'クリエイティブ向け'),
('INV-SNOW-001', 2, '請求書 - スノー', 'Invoice Snow', '清潔感のある白系。', 'invoice-snow.pdf', 'A4', 'portrait', 'snow', '#94A3B8', 0, 6, 'active', 'ミニマル'),

-- 英語インボイス (INVOICE_EN: カテゴリ 3)
('INV-EN2-001', 3, 'Invoice English 2', 'English Invoice 2', '英語形式の請求書。', 'invoice-english2.pdf', 'A4', 'portrait', 'standard', '#1c3658', 0, 1, 'active', '輸出用'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- 領収書テンプレート (RECEIPT: カテゴリ 7)
-- ═══════════════════════════════════════════════════════════════════════════════

('RCP-02-001', 7, '領収書 - 02', 'Receipt 02', '標準的な領収書フォーマット。', 'receipt-02.pdf', 'A4', 'portrait', 'standard', '#2C5F2D', 0, 1, 'active', '緑系配色'),
('RCP-SIMPLE-001', 7, '領収書 - 簡易', 'Receipt Simple', 'シンプルな領収書。', 'receipt-simple.pdf', 'A4', 'portrait', 'standard', '#1c3658', 0, 2, 'active', '最小限'),
('RCP-VERT-001', 7, '領収書 - 縦書', 'Receipt Vertical', '縦書き明細の領収書。', 'receipt-item-vertical.pdf', 'A4', 'portrait', 'standard', '#2C5F2D', 0, 3, 'active', '明細詳細'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- 見積書テンプレート (ESTIMATE: カテゴリ 4)
-- ═══════════════════════════════════════════════════════════════════════════════

('EST-STD-001', 4, '見積書 - 標準', 'Estimate Standard', '日本標準の見積書。', 'estimate-standard.pdf', 'A4', 'portrait', 'standard', '#312E81', 0, 1, 'active', '基本テンプレート'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- 納品書テンプレート (DELIVERY: カテゴリ 6)
-- ═══════════════════════════════════════════════════════════════════════════════

('DLV-06-001', 6, '納品書 - 06', 'Delivery Slip 06', '標準的な納品書フォーマット。', 'delivery-slip06.pdf', 'A4', 'portrait', 'standard', '#064E3B', 0, 1, 'active', '緑系配色'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- 注文書テンプレート (ORDER: カテゴリ 8)
-- ═══════════════════════════════════════════════════════════════════════════════

('ORD-04-001', 8, '注文書 - 04', 'Purchase Order 04', '標準的な購入注文書。', 'purchase-order4.pdf', 'A4', 'portrait', 'standard', '#5B4D7A', 0, 1, 'active', '基本テンプレート'),
('ORD-08-001', 8, '注文書 - 08', 'Purchase Order 08', '詳細版購入注文書。', 'purchase-order8.pdf', 'A4', 'portrait', 'standard', '#5B4D7A', 0, 2, 'active', '明細詳細'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- 議事録テンプレート (MINUTES: カテゴリ 10)
-- ═══════════════════════════════════════════════════════════════════════════════

('MIN-03-001', 10, '議事録 - 03', 'Minutes 03', '標準的な議事録フォーマット。', 'minutes-03.pdf', 'A4', 'portrait', 'standard', '#4A5568', 0, 1, 'active', '基本テンプレート'),
('MIN-06-001', 10, '議事録 - 06', 'Minutes 06', '簡易版議事録。', 'minutes-06.pdf', 'A4', 'portrait', 'standard', '#4A5568', 0, 2, 'active', 'シンプル'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- 報告書テンプレート (REPORT: カテゴリ 11)
-- ═══════════════════════════════════════════════════════════════════════════════

('RPT-WEEKLY-001', 11, '週報', 'Weekly Report', '週次報告書テンプレート。', 'report-weekly.pdf', 'A4', 'portrait', 'standard', '#1c3658', 0, 1, 'active', '週報用'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- FAX 送付状テンプレート (FAX: カテゴリ 12)
-- ═══════════════════════════════════════════════════════════════════════════════

('FAX-03-001', 12, 'FAX 送付状 - 03', 'Fax Cover 03', '標準的な FAX 送付状。', 'fax03.pdf', 'A4', 'portrait', 'standard', '#8B4513', 0, 1, 'active', '茶系配色'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- 履歴書テンプレート (RESUME: カテゴリ 14)
-- ═══════════════════════════════════════════════════════════════════════════════

('RES-JIS-001', 14, '履歴書 - JIS 規格', 'Resume JIS Standard', '日本工業規格の履歴書。', 'resume-jis.pdf', 'A4', 'portrait', 'standard', '#1a1a1a', 1, 1, 'active', 'JIS 規格'),

-- ═══════════════════════════════════════════════════════════════════════════════
-- その他テンプレート
-- ═══════════════════════════════════════════════════════════════════════════════

-- 契約書台帳 (CONTRACT: カテゴリ 9)
('CNT-COVER-001', 9, '契約書台帳 - カバー', 'Contract Cover', '契約書カバーテンプレート。', 'contract-cover-template-1.pdf', 'A4', 'portrait', 'standard', '#4C1D95', 0, 1, 'active', '表紙'),

-- 見積書明細 (ESTIMATE: カテゴリ 4)
('EST-DET-001', 4, '見積書 - 詳細', 'Estimate Detail', '詳細見積書。', 'estimate-detail.pdf', 'A4', 'portrait', 'standard', '#312E81', 0, 2, 'active', '明細付き'),

-- カバーレター (OTHER: カテゴリ 16)
('CL-02-001', 16, 'カバーレター - 02', 'Cover Letter 02', '求职信用テンプレート。', 'cover-letter-02.pdf', 'A4', 'portrait', 'standard', '#1c3658', 0, 1, 'active', '求职信用'),
('CL-05-001', 16, 'カバーレター - 05', 'Cover Letter 05', '求职信用テンプレートバリエーション。', 'cover-letter-05.pdf', 'A4', 'portrait', 'standard', '#1c3658', 0, 2, 'active', '求职信用'),

-- チェックリスト (MEMO: カテゴリ 13)
('CHK-03-001', 13, 'チェックリスト - 03', 'Check List 03', '汎用チェックリスト。', 'check-list3.pdf', 'A4', 'portrait', 'standard', '#78716C', 0, 1, 'active', '汎用'),

-- 連絡先リスト (MEMO: カテゴリ 13)
('CALL-01-001', 13, '連絡先リスト', 'Call Sheet', '連絡先一覧表。', 'call-sheet-1.pdf', 'A4', 'portrait', 'standard', '#607D8B', 0, 2, 'active', '連絡先用'),

-- 作業証明書 (OTHER: カテゴリ 16)
('WRK-CERT-001', 16, '作業証明書', 'Work Certificate', '作業完了証明書。', 'work-certificate.pdf', 'A4', 'portrait', 'standard', '#1c3658', 0, 3, 'active', '証明書'),

-- グリーティングカード (OTHER: カテゴリ 16)
('GRD-SUMMER-001', 16, '暑中見舞い', 'Summer Greeting', '夏季挨拶状。', '2021-summer-greeting-card-tem-08.pdf', 'A4', 'portrait', 'standard', '#EA580C', 0, 4, 'active', '季節挨拶');
