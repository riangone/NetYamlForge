-- Phase 4-A: 多拠点管理（branches テーブル）

CREATE TABLE IF NOT EXISTS branches (
  branch_id   TEXT PRIMARY KEY,
  branch_name TEXT NOT NULL,
  branch_type TEXT NOT NULL DEFAULT 'main',
  address     TEXT,
  phone       TEXT,
  email       TEXT,
  manager_id  TEXT,
  is_active   INTEGER NOT NULL DEFAULT 1,
  sort_order  INTEGER DEFAULT 0,
  notes       TEXT,
  created_at  TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

-- シードデータ
INSERT OR IGNORE INTO branches (branch_id, branch_name, branch_type, address, phone, email, manager_id, is_active, sort_order, created_at, updated_at) VALUES
  ('BR-001', '本店',     'main',    '東京都渋谷区代々木1-1-1',    '03-1234-5678', 'honten@example-dealer.co.jp',    'EMP-001', 1, 1, datetime('now'), datetime('now')),
  ('BR-002', '横浜支店', 'sub',     '神奈川県横浜市西区高島2-2-2', '045-234-5678', 'yokohama@example-dealer.co.jp',  'EMP-002', 1, 2, datetime('now'), datetime('now')),
  ('BR-003', 'サービスセンター', 'service', '東京都世田谷区三軒茶屋3-3-3', '03-5678-1234', 'service@example-dealer.co.jp', NULL, 1, 3, datetime('now'), datetime('now'));

-- 既存テーブルへの branch_id カラム追加（既存行はデフォルト BR-001）
ALTER TABLE vehicles   ADD COLUMN branch_id TEXT DEFAULT 'BR-001';
ALTER TABLE employees  ADD COLUMN branch_id TEXT DEFAULT 'BR-001';
ALTER TABLE sales_leads ADD COLUMN branch_id TEXT DEFAULT 'BR-001';
ALTER TABLE service_appointments ADD COLUMN branch_id TEXT DEFAULT 'BR-001';
