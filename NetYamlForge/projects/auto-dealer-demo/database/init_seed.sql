-- auto-dealer-demo プロジェクトのテストデータ
-- AI 窓口システムのサンプルデータを初期化します

-- 顧客マスタ
INSERT OR IGNORE INTO customers (customer_id, customer_type, name, name_kana, gender, phone, mobile, email, tier_level, preferred_contact) VALUES
('CUST-001', 'individual', '山田太郎', 'ヤマダタロウ', 'male', '03-1234-5678', '090-1234-5678', 'yamada@example.com', 'gold', 'email'),
('CUST-002', 'individual', '佐藤花子', 'サトウハナコ', 'female', '03-2345-6789', '090-2345-6789', 'hanako@example.com', 'silver', 'phone'),
('CUST-003', 'corporate', '東京自動車株式会社', 'トウキョウジドウシャ', NULL, '03-3456-7890', '090-3456-7890', 'info@tokyo-auto.co.jp', 'platinum', 'email'),
('CUST-004', 'individual', '鈴木一郎', 'スズキイチロウ', 'male', '04-4567-8901', '090-4567-8901', 'suzuki@example.com', 'regular', 'line'),
('CUST-005', 'individual', '高橋美咲', 'タカハシミサキ', 'female', '05-5678-9012', '090-5678-9012', 'misaki@example.com', 'vip', 'phone'),
('CUST-006', 'corporate', '大阪モータース株式会社', 'オオサカモータース', NULL, '06-6789-0123', '090-6789-0123', 'sales@osaka-motors.co.jp', 'gold', 'email'),
('CUST-007', 'individual', '伊藤健太', 'イトウケンタ', 'male', '07-7890-1234', '090-7890-1234', 'kenta@example.com', 'regular', 'phone'),
('CUST-008', 'individual', '中村愛', 'ナカムラアイ', 'female', '08-8901-2345', '090-8901-2345', 'ai.nakamura@example.com', 'silver', 'email'),
('CUST-009', 'individual', '小林大輔', 'コバヤシダイスケ', 'male', '09-9012-3456', '090-9012-3456', 'daisuke@example.com', 'regular', 'sms'),
('CUST-010', 'corporate', '横浜カーズ株式会社', 'ヨコハマカーズ', NULL, '04-0123-4567', '090-0123-4567', 'info@yokohama-cars.co.jp', 'silver', 'email');

-- 車両マスタ
INSERT OR IGNORE INTO vehicles (vehicle_id, customer_id, vin, maker, brand, model, grade, year, color, mileage, purchase_date) VALUES
('VEH-001', 'CUST-001', '1HGBH41JXMN109186', 'Toyota', 'トヨタ', 'カムリ', 'XLE', 2022, 'ホワイト', 15000, '2022-03-15'),
('VEH-002', 'CUST-002', '2FMDK3GC8DBA12345', 'Honda', 'ホンダ', 'アコード', 'Sport', 2021, 'ブラック', 28000, '2021-06-20'),
('VEH-003', 'CUST-003', '5UXWX7C5XBA123456', 'Nissan', '日産', 'リーフ', 'G', 2023, 'ブルー', 5000, '2023-01-10'),
('VEH-004', 'CUST-004', '1G1ZT53806F123456', 'Mazda', 'マツダ', 'CX-5', 'XD L Package', 2020, 'レッド', 42000, '2020-09-05'),
('VEH-005', 'CUST-005', 'WBADT43452G123456', 'Subaru', 'スバル', 'インプレッサ', '2.0i-S', 2023, 'シルバー', 8000, '2023-04-22'),
('VEH-006', 'CUST-006', 'WBA3A5C55CF123456', 'Toyota', 'トヨタ', 'プリウス', 'S', 2021, 'グレー', 35000, '2021-11-30'),
('VEH-007', 'CUST-007', 'WBAVB13596PT12345', 'Honda', 'ホンダ', 'シビック', 'Type R', 2022, 'イエロー', 12000, '2022-07-14'),
('VEH-008', 'CUST-008', 'WBA3B1C50DF123456', 'Nissan', '日産', 'ノート', 'e-POWER', 2023, 'パール', 3000, '2023-02-28'),
('VEH-009', 'CUST-009', 'WBAVB13596PT23456', 'Mazda', 'マツダ', 'アクセラ', 'XD', 2020, 'メタリック', 48000, '2020-12-10'),
('VEH-010', 'CUST-010', 'WBA3A5C55CF234567', 'Subaru', 'スバル', 'フォレスター', '2.0i-L', 2021, 'グリーン', 32000, '2021-08-25');

-- AI 対話セッション（テストデータ）
INSERT OR IGNORE INTO ai_conversations (conversation_id, customer_id, channel, status, last_intent, last_confidence, sentiment_score, started_at, ended_at) VALUES
('CONV-001', 'CUST-001', 'web', 'completed', 'service_inquiry', 0.92, 0.75, datetime('now', '-2 hours'), datetime('now', '-1 hour')),
('CONV-002', 'CUST-002', 'line', 'active', 'parts_request', 0.88, 0.60, datetime('now', '-30 minutes'), NULL),
('CONV-003', 'CUST-003', 'web', 'completed', 'quote_request', 0.95, 0.85, datetime('now', '-5 hours'), datetime('now', '-4 hours')),
('CONV-004', 'CUST-004', 'voice', 'escalated', 'complaint', 0.45, -0.30, datetime('now', '-1 hour'), NULL),
('CONV-005', 'CUST-005', 'web', 'active', 'test_drive_request', 0.91, 0.80, datetime('now', '-15 minutes'), NULL),
('CONV-006', 'CUST-006', 'email', 'completed', 'maintenance_inquiry', 0.89, 0.70, datetime('now', '-1 day'), datetime('now', '-1 day')),
('CONV-007', 'CUST-007', 'line', 'completed', 'parts_availability', 0.93, 0.65, datetime('now', '-3 hours'), datetime('now', '-2 hours')),
('CONV-008', 'CUST-008', 'web', 'active', 'warranty_inquiry', 0.87, 0.55, datetime('now', '-45 minutes'), NULL),
('CONV-009', 'CUST-009', 'voice', 'completed', 'delivery_status', 0.90, 0.72, datetime('now', '-6 hours'), datetime('now', '-5 hours')),
('CONV-010', 'CUST-010', 'web', 'escalated', 'price_negotiation', 0.40, -0.15, datetime('now', '-20 minutes'), NULL);

-- AI メッセージ
INSERT OR IGNORE INTO ai_messages (message_id, conversation_id, sender, message_type, content, intent, confidence_score, timestamp) VALUES
('MSG-001', 'CONV-001', 'customer', 'text', '車のオイル交換の予約をしたいのですが。', 'service_inquiry', 0.92, datetime('now', '-2 hours')),
('MSG-002', 'CONV-001', 'ai', 'text', '承知いたしました。オイル交換の予約ですね。ご希望の日時をお教えください。', NULL, NULL, datetime('now', '-2 hours', '+1 minute')),
('MSG-003', 'CONV-002', 'customer', 'text', 'ブレーキパッドの在庫はありますか？', 'parts_request', 0.88, datetime('now', '-30 minutes')),
('MSG-004', 'CONV-002', 'ai', 'text', 'お問い合わせありがとうございます。ブレーキパッドの在庫確認いたします。車種と年式をお教えください。', NULL, NULL, datetime('now', '-30 minutes', '+1 minute')),
('MSG-005', 'CONV-003', 'customer', 'text', '新車の見積もりをお願いします。', 'quote_request', 0.95, datetime('now', '-5 hours')),
('MSG-006', 'CONV-003', 'ai', 'text', '新車のお見積もりですね。どの車種をご検討でしょうか？', NULL, NULL, datetime('now', '-5 hours', '+1 minute')),
('MSG-007', 'CONV-004', 'customer', 'text', '前回の修理が全然できていない！どうなっているんだ！', 'complaint', 0.45, datetime('now', '-1 hour')),
('MSG-008', 'CONV-004', 'ai', 'text', 'ご迷惑をおかけして誠に申し訳ございません。担当の者に代わりますので、少々お待ちください。', NULL, NULL, datetime('now', '-1 hour', '+1 minute')),
('MSG-009', 'CONV-005', 'customer', 'text', '今週末の試乗予約は取れますか？', 'test_drive_request', 0.91, datetime('now', '-15 minutes')),
('MSG-010', 'CONV-005', 'ai', 'text', '試乗のご予約ですね。今週末でしたら、土曜日の午前中と日曜日の午後が空いております。', NULL, NULL, datetime('now', '-15 minutes', '+1 minute'));

-- AI エスカレーション
INSERT OR IGNORE INTO ai_handovers (handover_id, conversation_id, reason, priority, target_department, status, handover_notes, escalated_at) VALUES
('ESC-001', 'CONV-004', 'complaint', 'high', 'service', 'pending', 'お客様が前回の修理に不満をお持ちです。至急対応が必要です。', datetime('now', '-1 hour')),
('ESC-002', 'CONV-010', 'price_negotiation', 'medium', 'sales', 'assigned', '特別価格の承認が必要です。部長の承認を要請。', datetime('now', '-20 minutes')),
('ESC-003', 'CONV-001', 'ai_unable', 'low', 'service', 'resolved', '特殊なオイル規格についての問い合わせ。技術担当が対応済み。', datetime('now', '-2 hours'));

-- AI フィードバック
INSERT OR IGNORE INTO ai_feedback (feedback_id, conversation_id, message_id, rating, feedback_text, category, created_at) VALUES
('FB-001', 'CONV-001', 'MSG-002', 5, 'とても丁寧な対応でした。', 'helpful', datetime('now', '-1 hour')),
('FB-002', 'CONV-003', 'MSG-006', 4, '素早い回答で良かったです。', 'helpful', datetime('now', '-4 hours')),
('FB-003', 'CONV-006', NULL, 3, 'もう少し詳しく説明してほしい。', 'other', datetime('now', '-1 day')),
('FB-004', 'CONV-007', NULL, 5, '在庫状況をすぐに教えてくれて助かりました。', 'helpful', datetime('now', '-2 hours')),
('FB-005', 'CONV-009', NULL, 4, '配送状況が分かって良かったです。', 'helpful', datetime('now', '-5 hours'));

-- AI 知識ベース
INSERT OR IGNORE INTO ai_knowledge (knowledge_id, category, intent, question, answer, language, is_active, created_by) VALUES
('KNOW-001', 'faq', 'service_inquiry', 'オイル交換の目安は？', 'オイル交換の目安は走行距離 5,000km または 6 ヶ月です。過酷な使用条件では 3,000km ごとに交換することをお勧めします。', 'ja', 1, 'admin'),
('KNOW-002', 'faq', 'parts_request', 'ブレーキパッドの交換時期は？', 'ブレーキパッドの交換目安は 3 万〜5 万 km です。異音がする場合は早めの点検をお勧めします。', 'ja', 1, 'admin'),
('KNOW-003', 'faq', 'inspection', '車検の流れを教えてください。', '車検は 2 年ごとに必要です。予約→点検→整備→検査→納車の流れで、通常 2〜3 日かかります。', 'ja', 1, 'admin'),
('KNOW-004', 'faq', 'maintenance', 'タイヤ交換の目安は？', 'タイヤの交換目安は溝が 1.6mm 以下になった時、または購入から 5 年経過した時です。', 'ja', 1, 'admin'),
('KNOW-005', 'faq', 'troubleshooting', 'バッテリーが上がった時は？', 'バッテリーが上がった場合は、ジャンプスタートまたはロードサービスをご利用ください。予防策として 2〜3 年での交換をお勧めします。', 'ja', 1, 'admin');

-- サービス予約
INSERT OR IGNORE INTO service_appointments (appointment_id, customer_id, vehicle_id, appointment_type, preferred_date, status, service_menu, customer_request, estimated_cost) VALUES
('APPT-001', 'CUST-001', 'VEH-001', 'oil_change', datetime('now', '+1 day', '10 hours'), 'confirmed', 'オイル交換', '早めに終わると助かります。', 8000),
('APPT-002', 'CUST-002', 'VEH-002', 'inspection', datetime('now', '+3 days', '14 hours'), 'pending', '車検整備', '送迎車希望', 120000),
('APPT-003', 'CUST-003', 'VEH-003', 'repair', datetime('now', '+2 days', '11 hours'), 'confirmed', 'ブレーキ点検', 'ブレーキの異音確認', 15000),
('APPT-004', 'CUST-004', 'VEH-004', 'test_drive', datetime('now', '+5 days', '15 hours'), 'pending', '新型車試乗', 'CX-60 の試乗希望', 0),
('APPT-005', 'CUST-005', 'VEH-005', 'oil_change', datetime('now', '+7 days', '9 hours'), 'pending', 'オイル交換', '待ち時間短縮希望', 8000),
('APPT-006', 'CUST-006', 'VEH-006', 'repair', datetime('now', '+4 days', '13 hours'), 'confirmed', 'エアコン点検', '冷房の効きが悪い', 20000),
('APPT-007', 'CUST-007', 'VEH-007', 'inspection', datetime('now', '+14 days', '10 hours'), 'pending', '定期点検', '12 ヶ月点検', 25000),
('APPT-008', 'CUST-008', 'VEH-008', 'oil_change', datetime('now', '+6 days', '16 hours'), 'confirmed', 'オイル交換', 'エレメントも交換', 12000);

-- サービスリクエスト
INSERT OR IGNORE INTO service_requests (request_id, customer_id, vehicle_id, request_type, subject, description, priority, status, source) VALUES
('REQ-001', 'CUST-001', 'VEH-001', 'inquiry', 'オイル交換の予約について', '次回のオイル交換の予約をしたい。', 'normal', 'open', 'web'),
('REQ-002', 'CUST-002', 'VEH-002', 'quote_request', 'ブレーキパッド交換の見積もり', 'ブレーキパッド交換の費用を知りたい。', 'normal', 'in_progress', 'phone'),
('REQ-003', 'CUST-003', 'VEH-003', 'complaint', 'エアコンの効きが悪い', '最近エアコンの冷房が効かない。', 'high', 'open', 'email'),
('REQ-004', 'CUST-004', 'VEH-004', 'parts_request', 'エアフィルターの在庫確認', 'エアフィルターの在庫はありますか？', 'low', 'resolved', 'line'),
('REQ-005', 'CUST-005', 'VEH-005', 'inquiry', 'タイヤ交換の時期について', 'タイヤ交換の目安を教えてください。', 'normal', 'open', 'web'),
('REQ-006', 'CUST-006', 'VEH-006', 'quote_request', '車検の見積もり', '車検の総費用を知りたい。', 'normal', 'pending_parts', 'phone'),
('REQ-007', 'CUST-007', 'VEH-007', 'inquiry', '納車日の確認', '新車の納車はいつになりますか？', 'urgent', 'open', 'email'),
('REQ-008', 'CUST-008', 'VEH-008', 'parts_request', 'バッテリー交換', 'バッテリーの交換を依頼したい。', 'high', 'in_progress', 'walk_in'),
('REQ-009', 'CUST-009', 'VEH-009', 'complaint', '修理後の不具合', '修理後に異音がするようになった。', 'urgent', 'waiting_customer', 'phone'),
('REQ-010', 'CUST-010', 'VEH-010', 'inquiry', ' warranty について', '保証期間の延長は可能ですか？', 'normal', 'closed', 'web');
