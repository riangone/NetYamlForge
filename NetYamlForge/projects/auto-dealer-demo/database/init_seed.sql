-- auto-dealer-demo プロジェクトのテストデータ
-- 自動車ディーラー管理システムのサンプルデータを初期化します

-- 顧客マスタ
INSERT OR IGNORE INTO customers (customer_id, customer_type, name, name_kana, gender, phone, mobile, email, tier_level, preferred_contact, login_username) VALUES
('CUST-001', 'individual', '山田太郎', 'ヤマダタロウ', 'male', '03-1234-5678', '090-1234-5678', 'yamada@example.com', 'gold', 'email', 'customer1'),
('CUST-002', 'individual', '佐藤花子', 'サトウハナコ', 'female', '03-2345-6789', '090-2345-6789', 'hanako@example.com', 'silver', 'phone', 'customer2'),
('CUST-003', 'corporate', '東京自動車株式会社', 'トウキョウジドウシャ', NULL, '03-3456-7890', '090-3456-7890', 'info@tokyo-auto.co.jp', 'platinum', 'email', 'customer3'),
('CUST-004', 'individual', '鈴木一郎', 'スズキイチロウ', 'male', '04-4567-8901', '090-4567-8901', 'suzuki@example.com', 'regular', 'line', 'customer4'),
('CUST-005', 'individual', '高橋美咲', 'タカハシミサキ', 'female', '05-5678-9012', '090-5678-9012', 'misaki@example.com', 'vip', 'phone', 'customer5'),
('CUST-006', 'corporate', '大阪モータース株式会社', 'オオサカモータース', NULL, '06-6789-0123', '090-6789-0123', 'sales@osaka-motors.co.jp', 'gold', 'email', 'customer6'),
('CUST-007', 'individual', '伊藤健太', 'イトウケンタ', 'male', '07-7890-1234', '090-7890-1234', 'kenta@example.com', 'regular', 'phone', 'customer7'),
('CUST-008', 'individual', '中村愛', 'ナカムラアイ', 'female', '08-8901-2345', '090-8901-2345', 'ai.nakamura@example.com', 'silver', 'email', 'customer8'),
('CUST-009', 'individual', '小林大輔', 'コバヤシダイスケ', 'male', '09-9012-3456', '090-9012-3456', 'daisuke@example.com', 'regular', 'sms', 'customer9'),
('CUST-010', 'corporate', '横浜カーズ株式会社', 'ヨコハマカーズ', NULL, '04-0123-4567', '090-0123-4567', 'info@yokohama-cars.co.jp', 'silver', 'email', 'customer10');

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

-- 販売リード
INSERT OR IGNORE INTO sales_leads (lead_id, customer_id, vehicle_interest, budget, lead_score, status, assigned_to_user_id, assigned_sales, last_contact_at, created_at, updated_at) VALUES
('LEAD-001', 'CUST-001', 'test_drive_request', 4500000, 85, 'contacted',  'admin', 'sales_rep1', datetime('now', '-2 days'), datetime('now', '-3 days'), datetime('now', '-2 days')),
('LEAD-002', 'CUST-002', 'price_inquiry',       3000000, 62, 'new',        NULL,    'sales_rep1', NULL,                       datetime('now', '-1 day'),  datetime('now', '-1 day')),
('LEAD-003', 'CUST-003', 'vehicle_inquiry',      8000000, 91, 'qualified',  'admin','sales_rep1', datetime('now', '-1 day'), datetime('now', '-5 days'), datetime('now', '-1 day')),
('LEAD-004', 'CUST-004', 'quote_request',        2500000, 45, 'new',        NULL,   'sales_rep2', NULL,                       datetime('now', '-7 days'), datetime('now', '-7 days')),
('LEAD-005', 'CUST-005', 'financing_inquiry',    5000000, 78, 'proposal',   'admin','sales_rep2', datetime('now'),           datetime('now', '-4 days'), datetime('now')),
('LEAD-006', 'CUST-006', 'test_drive_request',   6000000, 88, 'won',        'admin','sales_rep2', datetime('now', '-1 day'), datetime('now', '-10 days'),datetime('now', '-1 day')),
('LEAD-007', 'CUST-007', 'new_car_inquiry',       2000000, 35, 'lost',       NULL,   'sales_rep3', datetime('now', '-14 days'),datetime('now', '-20 days'),datetime('now', '-14 days')),
('LEAD-008', 'CUST-008', 'vehicle_inquiry',      3500000, 55, 'contacted',  'admin','sales_rep1', datetime('now', '-3 days'), datetime('now', '-5 days'), datetime('now', '-3 days')),
('LEAD-009', 'CUST-009', 'price_inquiry',        2800000, 48, 'new',        NULL,   'sales_rep3', NULL,                       datetime('now', '-8 days'), datetime('now', '-8 days')),
('LEAD-010', 'CUST-010', 'test_drive_request',   7000000, 92, 'qualified',  'admin','sales_rep2', datetime('now'),           datetime('now', '-2 days'), datetime('now'));

-- リードアクティビティ（対応履歴）
INSERT OR IGNORE INTO lead_activities (activity_id, lead_id, activity_type, notes, outcome, next_action, next_action_date, created_by, created_at) VALUES
('ACT-001', 'LEAD-001', 'call',           '試乗の日程を確認。来週土曜を仮予約。', 'positive', '試乗予約確定の連絡', datetime('now', '+3 days'), 'admin', datetime('now', '-2 days')),
('ACT-002', 'LEAD-001', 'email',          '試乗車両の詳細資料を送付。', 'positive', '試乗当日のフォロー', datetime('now', '+5 days'), 'admin', datetime('now', '-1 day')),
('ACT-003', 'LEAD-003', 'visit',          '来店。法人フリート契約の条件を提示。', 'positive', '見積書の提出', datetime('now', '+2 days'), 'admin', datetime('now', '-1 day')),
('ACT-004', 'LEAD-005', 'proposal_sent',  'ファイナンスプランを含む見積書を送付。', 'neutral', '回答待ち', datetime('now', '+7 days'), 'admin', datetime('now')),
('ACT-005', 'LEAD-006', 'test_drive',     '試乗実施。お客様は非常に満足。', 'positive', '成約手続き', datetime('now', '+1 day'), 'admin', datetime('now', '-3 days')),
('ACT-006', 'LEAD-008', 'call',           '電話連絡したが不在。留守電に伝言。', 'no_answer', '再架電', datetime('now', '+1 day'), 'admin', datetime('now', '-3 days'));

-- 過去 7 日間のサービス予約実績（収益チャート用）
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

-- 従業員マスタ（テストデータ）
INSERT OR IGNORE INTO employees (employee_id, user_name, employee_number, name, name_kana, gender, email, department, position, role, supervisor_id, hire_date, employment_type, status) VALUES
-- 経営陣
('EMP-001', 'admin', '0001', '田中一郎', 'タナカイチロウ', 'male', 'tanaka@auto-dealer.com', 'management', 'general_manager', 'admin', NULL, '2015-04-01', 'full_time', 'active'),
('EMP-002', 'executive', '0002', '鈴木美咲', 'スズキミサキ', 'female', 'suzuki@auto-dealer.com', 'management', 'executive', 'executive', 'EMP-001', '2016-04-01', 'full_time', 'active'),
-- 営業部
('EMP-003', 'sales_manager', '1001', '佐藤健太', 'サトウケンタ', 'male', 'sato@auto-dealer.com', 'sales', 'manager', 'sales_manager', 'EMP-001', '2017-04-01', 'full_time', 'active'),
('EMP-004', 'sales_rep1', '1002', '高橋愛', 'タカハシアイ', 'female', 'takahashi@auto-dealer.com', 'sales', 'senior_staff', 'sales_rep', 'EMP-003', '2018-04-01', 'full_time', 'active'),
('EMP-005', 'sales_rep2', '1003', '伊藤大輔', 'イトウダイスケ', 'male', 'ito@auto-dealer.com', 'sales', 'staff', 'sales_rep', 'EMP-003', '2020-04-01', 'full_time', 'active'),
('EMP-006', 'sales_rep3', '1004', '中村結衣', 'ナカムラユイ', 'female', 'nakamura@auto-dealer.com', 'sales', 'staff', 'sales_rep', 'EMP-003', '2021-04-01', 'full_time', 'active'),
('EMP-007', 'sales_intern', '1005', '小林拓也', 'コバヤシタクヤ', 'male', 'kobayashi@auto-dealer.com', 'sales', 'intern', 'operator', 'EMP-003', '2025-04-01', 'intern', 'active'),
-- サービス部
('EMP-008', 'service_manager', '2001', '渡辺浩', 'ワタナベヒロシ', 'male', 'watanabe@auto-dealer.com', 'service', 'manager', 'service_staff', 'EMP-001', '2016-04-01', 'full_time', 'active'),
('EMP-009', 'service_staff1', '2002', '木村誠', 'キムラマコト', 'male', 'kimura@auto-dealer.com', 'service', 'senior_staff', 'service_staff', 'EMP-008', '2018-04-01', 'full_time', 'active'),
('EMP-010', 'service_staff2', '2003', '山本恵', 'ヤマモトメグミ', 'female', 'yamamoto@auto-dealer.com', 'service', 'staff', 'service_staff', 'EMP-008', '2020-04-01', 'full_time', 'active'),
('EMP-011', 'service_staff3', '2004', '松本隆', 'マツモトタカシ', 'male', 'matsumoto@auto-dealer.com', 'service', 'staff', 'service_staff', 'EMP-008', '2022-04-01', 'full_time', 'active'),
-- 管理部
('EMP-012', 'admin_staff', '3001', '井上千秋', 'イノウチアキ', 'female', 'inoue@auto-dealer.com', 'administration', 'staff', 'operator', 'EMP-001', '2019-04-01', 'full_time', 'active'),
-- パーツ部
('EMP-013', 'parts_staff', '4001', '林大樹', 'ハヤシダイキ', 'male', 'hayashi@auto-dealer.com', 'parts', 'staff', 'service_staff', 'EMP-008', '2021-04-01', 'full_time', 'active');
