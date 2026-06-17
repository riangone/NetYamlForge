-- Phase 4-D: 売上目標・配額管理（sales_quotas テーブル）

CREATE TABLE IF NOT EXISTS sales_quotas (
  quota_id        TEXT PRIMARY KEY,
  employee_id     TEXT NOT NULL,
  branch_id       TEXT DEFAULT 'BR-001',
  year            INTEGER NOT NULL,
  month           INTEGER NOT NULL,
  quota_amount    DECIMAL(12,2) NOT NULL DEFAULT 0,
  quota_units     INTEGER NOT NULL DEFAULT 0,
  achieved_amount DECIMAL(12,2) NOT NULL DEFAULT 0,
  achieved_units  INTEGER NOT NULL DEFAULT 0,
  notes           TEXT,
  created_at      TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at      TEXT NOT NULL DEFAULT (datetime('now')),
  UNIQUE(employee_id, year, month)
);

-- シードデータ（2026年6月・5名分の目標）
INSERT OR IGNORE INTO sales_quotas (quota_id, employee_id, branch_id, year, month, quota_amount, quota_units, achieved_amount, achieved_units, created_at, updated_at) VALUES
  ('QT-001', 'EMP-002', 'BR-001', 2026, 6, 15000000, 5, 8500000, 3, datetime('now'), datetime('now')),
  ('QT-002', 'EMP-003', 'BR-001', 2026, 6, 12000000, 4, 9200000, 3, datetime('now'), datetime('now')),
  ('QT-003', 'EMP-007', 'BR-002', 2026, 6, 10000000, 3, 4800000, 1, datetime('now'), datetime('now')),
  ('QT-004', 'EMP-008', 'BR-002', 2026, 6, 10000000, 3, 6300000, 2, datetime('now'), datetime('now')),
  ('QT-005', 'EMP-009', 'BR-001', 2026, 6, 8000000,  2, 3200000, 1, datetime('now'), datetime('now')),
  -- 5月実績
  ('QT-006', 'EMP-002', 'BR-001', 2026, 5, 15000000, 5, 16200000, 5, datetime('now'), datetime('now')),
  ('QT-007', 'EMP-003', 'BR-001', 2026, 5, 12000000, 4, 11800000, 4, datetime('now'), datetime('now')),
  ('QT-008', 'EMP-007', 'BR-002', 2026, 5, 10000000, 3, 10500000, 3, datetime('now'), datetime('now'));
