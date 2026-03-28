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

-- 車両マスタ（顧客所有車両 + ディーラー在庫）
-- 顧客所有車両（customer_id あり、status='sold'）
INSERT OR IGNORE INTO vehicles (vehicle_id, customer_id, vin, maker, brand, model, grade, year, color, mileage, vehicle_type, fuel_type, transmission, price, status, purchase_date) VALUES
('VEH-001', 'CUST-001', '1HGBH41JXMN109186', 'Toyota', 'トヨタ', 'カムリ', 'XLE', 2022, 'ホワイト', 15000, 'sedan', 'hybrid', 'CVT', 3850000, 'sold', '2022-03-15'),
('VEH-002', 'CUST-002', '2FMDK3GC8DBA12345', 'Honda', 'ホンダ', 'アコード', 'Sport', 2021, 'ブラック', 28000, 'sedan', 'gasoline', 'CVT', 3280000, 'sold', '2021-06-20'),
('VEH-003', 'CUST-003', '5UXWX7C5XBA123456', 'Nissan', '日産', 'リーフ', 'G', 2023, 'ブルー', 5000, 'sedan', 'ev', 'AT', 3990000, 'sold', '2023-01-10'),
('VEH-004', 'CUST-004', '1G1ZT53806F123456', 'Mazda', 'マツダ', 'CX-5', 'XD L Package', 2020, 'レッド', 42000, 'suv', 'diesel', 'AT', 3500000, 'sold', '2020-09-05'),
('VEH-005', 'CUST-005', 'WBADT43452G123456', 'Subaru', 'スバル', 'インプレッサ', '2.0i-S', 2023, 'シルバー', 8000, 'sedan', 'gasoline', 'CVT', 2480000, 'sold', '2023-04-22'),
('VEH-006', 'CUST-006', 'WBA3A5C55CF123456', 'Toyota', 'トヨタ', 'プリウス', 'S', 2021, 'グレー', 35000, 'sedan', 'hybrid', 'CVT', 2700000, 'sold', '2021-11-30'),
('VEH-007', 'CUST-007', 'WBAVB13596PT12345', 'Honda', 'ホンダ', 'シビック', 'Type R', 2022, 'イエロー', 12000, 'sports', 'gasoline', 'MT', 5200000, 'sold', '2022-07-14'),
('VEH-008', 'CUST-008', 'WBA3B1C50DF123456', 'Nissan', '日産', 'ノート', 'e-POWER', 2023, 'パール', 3000, 'sedan', 'hybrid', 'AT', 2100000, 'sold', '2023-02-28'),
('VEH-009', 'CUST-009', 'WBAVB13596PT23456', 'Mazda', 'マツダ', 'アクセラ', 'XD', 2020, 'メタリック', 48000, 'sedan', 'diesel', 'AT', 2250000, 'sold', '2020-12-10'),
('VEH-010', 'CUST-010', 'WBA3A5C55CF234567', 'Subaru', 'スバル', 'フォレスター', '2.0i-L', 2021, 'グリーン', 32000, 'suv', 'gasoline', 'CVT', 3180000, 'sold', '2021-08-25');

-- ディーラー在庫車両（customer_id なし、status='available'/'reserved'/'display'）
INSERT OR IGNORE INTO vehicles (vehicle_id, vin, maker, brand, model, grade, year, color, mileage, vehicle_type, fuel_type, transmission, engine_capacity, price, cost, status, arrival_date, inspection_date, features, notes) VALUES
('INV-001', 'JT3HP10V7X7089001', 'Toyota', 'トヨタ', 'プリウス PHV', 'Z', 2024, 'プラチナホワイト', 0, 'sedan', 'phev', 'CVT', 1800, 4280000, 3550000, 'available', '2024-01-15', '2026-01-15', 'パノラマルーフ,360度カメラ,ヒートシート', '新型モデル。試乗可能'),
('INV-002', 'JT3HP10V7X7089002', 'Toyota', 'トヨタ', 'ランドクルーザー 300', 'ZX', 2024, 'ブラック', 0, 'suv', 'diesel', 'AT', 3300, 8980000, 7500000, 'available', '2024-02-01', '2026-02-01', '4WD,エアサスペンション,冷却シート', '人気 SUV。在庫残 2 台'),
('INV-003', 'JT3HP10V7X7089003', 'Toyota', 'トヨタ', 'アルファード', 'Executive Lounge', 2024, 'パールホワイト', 0, 'minivan', 'hybrid', 'CVT', 2500, 9150000, 7800000, 'available', '2024-01-20', '2026-01-20', 'リアエンターテインメント,マッサージシート,電動スライドドア', '法人需要あり'),
('INV-004', 'JT3HP10V7X7089004', 'Honda', 'ホンダ', 'CR-V', 'EX', 2023, 'ソニックグレー', 0, 'suv', 'hybrid', 'CVT', 2000, 3980000, 3200000, 'available', '2023-12-10', '2025-12-10', 'ホンダセンシング,USB-C充電,ワイヤレス充電', NULL),
('INV-005', 'JT3HP10V7X7089005', 'Honda', 'ホンダ', 'フィット', 'e:HEV HOME', 2024, 'クリスタルブラック', 0, 'sedan', 'hybrid', 'CVT', 1500, 2380000, 1950000, 'available', '2024-01-05', '2026-01-05', 'Honda SENSING,ドライブレコーダー', '軽快な走り。女性に人気'),
('INV-006', 'JT3HP10V7X7089006', 'Nissan', '日産', 'アリア', 'B9 e-4ORCE', 2024, 'ミッドナイトブラック', 0, 'suv', 'ev', 'AT', 0, 6890000, 5700000, 'available', '2024-02-15', '2026-02-15', '4WD,ProPILOT 2.0,V2H対応', '航続距離 470km'),
('INV-007', 'JT3HP10V7X7089007', 'Mazda', 'マツダ', 'CX-60', 'PHEV Premium Modern', 2024, 'アーティザンレッド', 0, 'suv', 'phev', 'AT', 2500, 6210000, 5100000, 'display', '2024-01-10', '2026-01-10', 'BOSE,本革シート,ヘッドアップディスプレイ', 'ショールーム展示車'),
('INV-008', 'JT3HP10V7X7089008', 'Subaru', 'スバル', 'フォレスター', 'Advance', 2024, 'マグネタイトグレー', 0, 'suv', 'hybrid', 'CVT', 2000, 3638000, 2980000, 'available', '2024-01-25', '2026-01-25', 'EyeSight,アドバンスドセーフティパッケージ', NULL),
('INV-009', 'JT3HP10V7X7089009', 'Toyota', 'トヨタ', 'ノア', 'S-Z', 2024, 'スーパーホワイト', 0, 'minivan', 'hybrid', 'CVT', 1800, 3850000, 3100000, 'reserved', '2024-01-18', '2026-01-18', '両側電動スライドドア,9インチナビ', '商談中'),
('INV-010', 'JT3HP10V7X7089010', 'Honda', 'ホンダ', 'ヴェゼル', 'e:HEV PLaY', 2024, 'プレミアムサンセットオレンジ', 0, 'suv', 'hybrid', 'CVT', 1500, 3070000, 2500000, 'available', '2024-02-05', '2026-02-05', 'ドライブレコーダー,フロアマット', NULL);

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

-- 販売リード（AI 窓口から自動生成・手動登録）
INSERT OR IGNORE INTO sales_leads (lead_id, customer_id, vehicle_interest, budget, lead_score, status, source_conversation_id, assigned_to_user_id, last_contact_at, created_at, updated_at) VALUES
('LEAD-001', 'CUST-001', 'test_drive_request', 4500000, 85, 'contacted',  'CONV-005', 'admin', datetime('now', '-2 days'), datetime('now', '-3 days'), datetime('now', '-2 days')),
('LEAD-002', 'CUST-002', 'price_inquiry',       3000000, 62, 'new',        'CONV-003', NULL,    NULL,                       datetime('now', '-1 day'),  datetime('now', '-1 day')),
('LEAD-003', 'CUST-003', 'vehicle_inquiry',      8000000, 91, 'qualified',  NULL,       'admin', datetime('now', '-1 day'), datetime('now', '-5 days'), datetime('now', '-1 day')),
('LEAD-004', 'CUST-004', 'quote_request',        2500000, 45, 'new',        NULL,       NULL,    NULL,                       datetime('now', '-7 days'), datetime('now', '-7 days')),
('LEAD-005', 'CUST-005', 'financing_inquiry',    5000000, 78, 'proposal',   'CONV-001', 'admin', datetime('now'),           datetime('now', '-4 days'), datetime('now')),
('LEAD-006', 'CUST-006', 'test_drive_request',   6000000, 88, 'won',        NULL,       'admin', datetime('now', '-1 day'), datetime('now', '-10 days'),datetime('now', '-1 day')),
('LEAD-007', 'CUST-007', 'new_car_inquiry',       2000000, 35, 'lost',       NULL,       NULL,    datetime('now', '-14 days'),datetime('now', '-20 days'),datetime('now', '-14 days')),
('LEAD-008', 'CUST-008', 'vehicle_inquiry',      3500000, 55, 'contacted',  NULL,       'admin', datetime('now', '-3 days'), datetime('now', '-5 days'), datetime('now', '-3 days')),
('LEAD-009', 'CUST-009', 'price_inquiry',        2800000, 48, 'new',        NULL,       NULL,    NULL,                       datetime('now', '-8 days'), datetime('now', '-8 days')),
('LEAD-010', 'CUST-010', 'test_drive_request',   7000000, 92, 'qualified',  'CONV-010', 'admin', datetime('now'),           datetime('now', '-2 days'), datetime('now'));

-- リードアクティビティ（対応履歴）
INSERT OR IGNORE INTO lead_activities (activity_id, lead_id, activity_type, notes, outcome, next_action, next_action_date, created_by, created_at) VALUES
('ACT-001', 'LEAD-001', 'call',           '試乗の日程を確認。来週土曜を仮予約。', 'positive', '試乗予約確定の連絡', datetime('now', '+3 days'), 'admin', datetime('now', '-2 days')),
('ACT-002', 'LEAD-001', 'email',          '試乗車両の詳細資料を送付。', 'positive', '試乗当日のフォロー', datetime('now', '+5 days'), 'admin', datetime('now', '-1 day')),
('ACT-003', 'LEAD-003', 'visit',          '来店。法人フリート契約の条件を提示。', 'positive', '見積書の提出', datetime('now', '+2 days'), 'admin', datetime('now', '-1 day')),
('ACT-004', 'LEAD-005', 'proposal_sent',  'ファイナンスプランを含む見積書を送付。', 'neutral', '回答待ち', datetime('now', '+7 days'), 'admin', datetime('now')),
('ACT-005', 'LEAD-006', 'test_drive',     '試乗実施。お客様は非常に満足。', 'positive', '成約手続き', datetime('now', '+1 day'), 'admin', datetime('now', '-3 days')),
('ACT-006', 'LEAD-008', 'call',           '電話連絡したが不在。留守電に伝言。', 'no_answer', '再架電', datetime('now', '+1 day'), 'admin', datetime('now', '-3 days'));

-- ─────────────────────────────────────────────────────────────────────────────
-- 過去 7 日間の分析データ（AIAnalytics・ExecDashboard のチャートを充実させる）
-- ─────────────────────────────────────────────────────────────────────────────

-- 過去 7 日間の AI 対話（トレンドチャート用）
INSERT OR IGNORE INTO ai_conversations (conversation_id, customer_id, channel, status, last_intent, last_confidence, sentiment_score, assigned_to_user_id, started_at, ended_at) VALUES
-- 6日前
('CONV-H001', 'CUST-001', 'web',   'completed', 'service_inquiry',    0.91, 0.70, NULL,    datetime('now', '-6 days', '9 hours'),  datetime('now', '-6 days', '10 hours')),
('CONV-H002', 'CUST-002', 'line',  'completed', 'oil_change',         0.88, 0.65, NULL,    datetime('now', '-6 days', '11 hours'), datetime('now', '-6 days', '11 hours', '30 minutes')),
('CONV-H003', 'CUST-003', 'web',   'escalated', 'complaint',          0.42, -0.50, 'admin', datetime('now', '-6 days', '14 hours'), datetime('now', '-6 days', '15 hours')),
('CONV-H004', 'CUST-004', 'voice', 'completed', 'quote_request',      0.85, 0.55, NULL,    datetime('now', '-6 days', '16 hours'), datetime('now', '-6 days', '17 hours')),
-- 5日前
('CONV-H005', 'CUST-005', 'web',   'completed', 'test_drive_request', 0.93, 0.80, NULL,    datetime('now', '-5 days', '10 hours'), datetime('now', '-5 days', '11 hours')),
('CONV-H006', 'CUST-006', 'email', 'completed', 'maintenance_inquiry',0.89, 0.60, NULL,    datetime('now', '-5 days', '13 hours'), datetime('now', '-5 days', '14 hours')),
('CONV-H007', 'CUST-007', 'line',  'completed', 'parts_request',      0.92, 0.75, NULL,    datetime('now', '-5 days', '15 hours'), datetime('now', '-5 days', '16 hours')),
('CONV-H008', 'CUST-008', 'web',   'abandoned', 'price_inquiry',      0.35, 0.10, NULL,    datetime('now', '-5 days', '18 hours'), NULL),
-- 4日前
('CONV-H009', 'CUST-009', 'web',   'completed', 'vehicle_inquiry',    0.90, 0.65, NULL,    datetime('now', '-4 days', '9 hours'),  datetime('now', '-4 days', '10 hours')),
('CONV-H010', 'CUST-010', 'line',  'completed', 'service_inquiry',    0.87, 0.55, NULL,    datetime('now', '-4 days', '11 hours'), datetime('now', '-4 days', '12 hours')),
('CONV-H011', 'CUST-001', 'web',   'escalated', 'complaint',          0.40, -0.70, 'admin', datetime('now', '-4 days', '14 hours'), datetime('now', '-4 days', '15 hours')),
('CONV-H012', 'CUST-002', 'voice', 'completed', 'test_drive_request', 0.94, 0.85, NULL,    datetime('now', '-4 days', '16 hours'), datetime('now', '-4 days', '17 hours')),
('CONV-H013', 'CUST-003', 'web',   'completed', 'financing_inquiry',  0.91, 0.70, NULL,    datetime('now', '-4 days', '20 hours'), datetime('now', '-4 days', '21 hours')),
-- 3日前
('CONV-H014', 'CUST-004', 'line',  'completed', 'oil_change',         0.88, 0.60, NULL,    datetime('now', '-3 days', '9 hours'),  datetime('now', '-3 days', '10 hours')),
('CONV-H015', 'CUST-005', 'web',   'completed', 'warranty_inquiry',   0.86, 0.50, NULL,    datetime('now', '-3 days', '12 hours'), datetime('now', '-3 days', '13 hours')),
('CONV-H016', 'CUST-006', 'email', 'completed', 'quote_request',      0.93, 0.75, NULL,    datetime('now', '-3 days', '14 hours'), datetime('now', '-3 days', '15 hours')),
('CONV-H017', 'CUST-007', 'web',   'abandoned', 'price_inquiry',      0.30, -0.10, NULL,   datetime('now', '-3 days', '17 hours'), NULL),
('CONV-H018', 'CUST-008', 'line',  'escalated', 'high_value_deal',    0.48, 0.20, 'admin', datetime('now', '-3 days', '19 hours'), datetime('now', '-3 days', '20 hours')),
-- 2日前
('CONV-H019', 'CUST-009', 'web',   'completed', 'service_inquiry',    0.90, 0.65, NULL,    datetime('now', '-2 days', '9 hours'),  datetime('now', '-2 days', '10 hours')),
('CONV-H020', 'CUST-010', 'voice', 'completed', 'maintenance_inquiry',0.92, 0.70, NULL,    datetime('now', '-2 days', '11 hours'), datetime('now', '-2 days', '12 hours')),
('CONV-H021', 'CUST-001', 'web',   'completed', 'parts_request',      0.85, 0.50, NULL,    datetime('now', '-2 days', '14 hours'), datetime('now', '-2 days', '15 hours')),
('CONV-H022', 'CUST-002', 'line',  'completed', 'vehicle_inquiry',    0.94, 0.80, NULL,    datetime('now', '-2 days', '16 hours'), datetime('now', '-2 days', '17 hours')),
('CONV-H023', 'CUST-003', 'email', 'escalated', 'vip_customer',       0.55, 0.30, 'admin', datetime('now', '-2 days', '20 hours'), datetime('now', '-2 days', '21 hours')),
-- 1日前
('CONV-H024', 'CUST-004', 'web',   'completed', 'test_drive_request', 0.91, 0.75, NULL,    datetime('now', '-1 day', '9 hours'),   datetime('now', '-1 day', '10 hours')),
('CONV-H025', 'CUST-005', 'line',  'completed', 'oil_change',         0.89, 0.60, NULL,    datetime('now', '-1 day', '11 hours'),  datetime('now', '-1 day', '12 hours')),
('CONV-H026', 'CUST-006', 'web',   'completed', 'financing_inquiry',  0.93, 0.80, NULL,    datetime('now', '-1 day', '14 hours'),  datetime('now', '-1 day', '15 hours')),
('CONV-H027', 'CUST-007', 'voice', 'abandoned', 'complaint',          0.38, -0.60, NULL,   datetime('now', '-1 day', '17 hours'),  NULL),
('CONV-H028', 'CUST-008', 'web',   'escalated', 'complex_inquiry',    0.44, -0.30, 'admin', datetime('now', '-1 day', '19 hours'), datetime('now', '-1 day', '20 hours')),
('CONV-H029', 'CUST-009', 'line',  'completed', 'quote_request',      0.90, 0.65, NULL,    datetime('now', '-1 day', '21 hours'),  datetime('now', '-1 day', '22 hours'));

-- 過去のエスカレーション履歴（エスカレーション分析チャート用）
INSERT OR IGNORE INTO ai_handovers (handover_id, conversation_id, reason, priority, target_department, status, handover_notes, assigned_to_user_id, assigned_at, resolved_at, resolution_notes, escalated_at) VALUES
('ESC-H001', 'CONV-H003', 'complaint',          'high',   'service', 'resolved', 'クレーム対応完了', 'admin', datetime('now', '-6 days', '14 hours', '10 minutes'), datetime('now', '-6 days', '15 hours'), '修理無償で対応。再発防止を約束。', datetime('now', '-6 days', '14 hours')),
('ESC-H002', 'CONV-H011', 'negative_sentiment', 'high',   'service', 'resolved', 'ネガティブ感情検出', 'admin', datetime('now', '-4 days', '14 hours', '5 minutes'), datetime('now', '-4 days', '16 hours'), '代車提供で対応完了。', datetime('now', '-4 days', '14 hours')),
('ESC-H003', 'CONV-H018', 'high_value_deal',    'medium', 'sales',   'resolved', '高額商談。部長承認必要', 'admin', datetime('now', '-3 days', '19 hours', '15 minutes'), datetime('now', '-3 days', '21 hours'), '特別価格で成約。', datetime('now', '-3 days', '19 hours')),
('ESC-H004', 'CONV-H023', 'vip_customer',       'urgent', 'sales',   'resolved', 'VIP 顧客対応', 'admin', datetime('now', '-2 days', '20 hours', '5 minutes'), datetime('now', '-2 days', '22 hours'), 'VIP 専任担当を手配。', datetime('now', '-2 days', '20 hours')),
('ESC-H005', 'CONV-H027', 'complaint',          'high',   'service', 'resolved', 'クレーム対応', 'admin', datetime('now', '-1 day', '17 hours', '10 minutes'), datetime('now', '-1 day', '19 hours'), '修理箇所を再確認して対応完了。', datetime('now', '-1 day', '17 hours')),
('ESC-H006', 'CONV-H028', 'complex_inquiry',    'medium', 'sales',   'resolved', '複雑な問い合わせ', 'admin', datetime('now', '-1 day', '19 hours', '15 minutes'), datetime('now', '-1 day', '21 hours'), '技術担当と連携して解決。', datetime('now', '-1 day', '19 hours'));

-- フィードバック（過去 7 日間、満足度分析用）
INSERT OR IGNORE INTO ai_feedback (feedback_id, conversation_id, message_id, rating, feedback_text, category, created_at) VALUES
('FB-H001', 'CONV-H001', NULL, 5, '迅速な対応で非常に満足です。', 'helpful',   datetime('now', '-6 days', '10 hours')),
('FB-H002', 'CONV-H002', NULL, 4, '丁寧な説明でよかったです。',   'helpful',   datetime('now', '-6 days', '11 hours', '35 minutes')),
('FB-H003', 'CONV-H004', NULL, 3, 'もう少し詳しく知りたかった。', 'other',     datetime('now', '-6 days', '17 hours')),
('FB-H004', 'CONV-H005', NULL, 5, '試乗の予約がスムーズでした。', 'helpful',   datetime('now', '-5 days', '11 hours')),
('FB-H005', 'CONV-H006', NULL, 4, '素早い回答でした。',           'helpful',   datetime('now', '-5 days', '14 hours')),
('FB-H006', 'CONV-H009', NULL, 5, 'ありがとうございました。',     'helpful',   datetime('now', '-4 days', '10 hours')),
('FB-H007', 'CONV-H012', NULL, 5, '完璧な対応でした。',           'helpful',   datetime('now', '-4 days', '17 hours')),
('FB-H008', 'CONV-H014', NULL, 4, '良かったです。',               'helpful',   datetime('now', '-3 days', '10 hours')),
('FB-H009', 'CONV-H016', NULL, 5, '見積もりが明確で助かりました。','helpful',  datetime('now', '-3 days', '15 hours')),
('FB-H010', 'CONV-H019', NULL, 4, '丁寧な回答でした。',           'helpful',   datetime('now', '-2 days', '10 hours')),
('FB-H011', 'CONV-H022', NULL, 5, '在庫情報がすぐわかりました。', 'helpful',   datetime('now', '-2 days', '17 hours')),
('FB-H012', 'CONV-H024', NULL, 5, '試乗予約が簡単にできました。', 'helpful',   datetime('now', '-1 day', '10 hours')),
('FB-H013', 'CONV-H025', NULL, 3, '回答が少し遅かったです。',     'unhelpful', datetime('now', '-1 day', '12 hours')),
('FB-H014', 'CONV-H026', NULL, 5, '詳細な説明ありがとうございます。','helpful', datetime('now', '-1 day', '15 hours'));

-- ナレッジベース利用統計の更新（KB 効果分析用）
UPDATE ai_knowledge SET usage_count = 45, helpful_count = 38, not_helpful_count = 7, last_used_at = datetime('now', '-1 hour')  WHERE knowledge_id = 'KNOW-001';
UPDATE ai_knowledge SET usage_count = 32, helpful_count = 28, not_helpful_count = 4, last_used_at = datetime('now', '-3 hours') WHERE knowledge_id = 'KNOW-002';
UPDATE ai_knowledge SET usage_count = 67, helpful_count = 58, not_helpful_count = 9, last_used_at = datetime('now', '-30 minutes') WHERE knowledge_id = 'KNOW-003';
UPDATE ai_knowledge SET usage_count = 21, helpful_count = 17, not_helpful_count = 4, last_used_at = datetime('now', '-2 hours') WHERE knowledge_id = 'KNOW-004';
UPDATE ai_knowledge SET usage_count = 15, helpful_count = 12, not_helpful_count = 3, last_used_at = datetime('now', '-4 hours') WHERE knowledge_id = 'KNOW-005';

-- 追加ナレッジベース項目（車両情報・キャンペーン）
INSERT OR IGNORE INTO ai_knowledge (knowledge_id, category, intent, question, answer, language, is_active, usage_count, helpful_count, not_helpful_count, created_by, last_used_at) VALUES
('KNOW-006', 'vehicle_info',   'ev_inquiry',           'EV の航続距離は？',             '当社取扱い EV モデルの航続距離は 300〜470km（WLTCモード）です。充電インフラについてもご案内できます。',   'ja', 1, 28, 24, 4, 'admin', datetime('now', '-2 hours')),
('KNOW-007', 'vehicle_info',   'hybrid_info',          'ハイブリッドと EV の違いは？',  'ハイブリッドはガソリンエンジンとモーターを組み合わせ、EV は電気のみで走行します。用途に応じてご提案します。', 'ja', 1, 35, 30, 5, 'admin', datetime('now', '-1 hour')),
('KNOW-008', 'campaign',       'campaign_inquiry',     '現在のキャンペーンは？',        '只今、春の下取り強化キャンペーン中です。対象車種の下取り額が最大 20% アップとなります。詳細は担当者にお問い合わせください。', 'ja', 1, 52, 45, 7, 'admin', datetime('now', '-30 minutes')),
('KNOW-009', 'service_menu',   'maintenance_menu',     'メンテナンスパックとは？',      'メンテナンスパックはオイル交換・点検・消耗品交換をパッケージ化したお得なプランです。月額 3,000 円〜です。', 'ja', 1, 19, 16, 3, 'admin', datetime('now', '-5 hours')),
('KNOW-010', 'business_hours', 'business_hours_inquiry','営業時間を教えてください。',   '営業時間は 9:00〜18:00（月〜土）、10:00〜17:00（日・祝）です。水曜定休です。', 'ja', 1, 41, 38, 3, 'admin', datetime('now', '-10 minutes'));

-- 過去の完了済みサービス予約（収益チャート用）
INSERT OR IGNORE INTO service_appointments (appointment_id, customer_id, vehicle_id, appointment_type, preferred_date, status, service_menu, estimated_cost, actual_cost, duration_minutes, completed_at) VALUES
('APPT-H001', 'CUST-001', 'VEH-001', 'oil_change',   datetime('now', '-6 days', '10 hours'), 'completed', 'オイル交換',           8000,  8500,  45, datetime('now', '-6 days', '11 hours')),
('APPT-H002', 'CUST-002', 'VEH-002', 'inspection',   datetime('now', '-5 days', '9 hours'),  'completed', '車検整備',            120000, 135000, 480, datetime('now', '-5 days', '18 hours')),
('APPT-H003', 'CUST-003', 'VEH-003', 'repair',       datetime('now', '-5 days', '13 hours'), 'completed', 'ブレーキ交換',         25000,  28000, 120, datetime('now', '-5 days', '15 hours')),
('APPT-H004', 'CUST-004', 'VEH-004', 'oil_change',   datetime('now', '-4 days', '11 hours'), 'completed', 'オイル・エレメント交換', 12000,  12000,  60, datetime('now', '-4 days', '12 hours')),
('APPT-H005', 'CUST-005', 'VEH-005', 'inspection',   datetime('now', '-4 days', '9 hours'),  'completed', '定期点検 12 ヶ月',      25000,  22000, 180, datetime('now', '-4 days', '12 hours')),
('APPT-H006', 'CUST-006', 'VEH-006', 'repair',       datetime('now', '-3 days', '10 hours'), 'completed', 'エアコン修理',          35000,  42000, 240, datetime('now', '-3 days', '14 hours')),
('APPT-H007', 'CUST-007', 'VEH-007', 'oil_change',   datetime('now', '-3 days', '15 hours'), 'completed', 'オイル交換',            8000,   8000,  40, datetime('now', '-3 days', '16 hours')),
('APPT-H008', 'CUST-008', 'VEH-008', 'delivery',     datetime('now', '-2 days', '14 hours'), 'completed', '新車納車',                  0,      0,  60, datetime('now', '-2 days', '15 hours')),
('APPT-H009', 'CUST-009', 'VEH-009', 'repair',       datetime('now', '-2 days', '10 hours'), 'completed', 'タイヤ 4 本交換',       60000,  64000, 120, datetime('now', '-2 days', '12 hours')),
('APPT-H010', 'CUST-010', 'VEH-010', 'oil_change',   datetime('now', '-1 day', '9 hours'),   'completed', 'オイル交換',            8000,   8000,  45, datetime('now', '-1 day', '10 hours')),
('APPT-H011', 'CUST-001', 'VEH-001', 'inspection',   datetime('now', '-1 day', '10 hours'),  'completed', '車検整備',            120000, 128000, 420, datetime('now', '-1 day', '17 hours')),
('APPT-H012', 'CUST-003', 'VEH-003', 'test_drive',   datetime('now', '-6 days', '14 hours'), 'completed', '試乗（新型プリウス PHV）', 0,      0,  60, datetime('now', '-6 days', '15 hours'));
