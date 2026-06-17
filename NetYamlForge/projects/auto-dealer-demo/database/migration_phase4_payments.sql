-- Phase 4-F: ローン・支払プラン管理（payment_plans テーブル）

CREATE TABLE IF NOT EXISTS payment_plans (
  plan_id          TEXT PRIMARY KEY,
  lead_id          TEXT,
  customer_id      TEXT NOT NULL,
  vehicle_id       TEXT NOT NULL,
  plan_type        TEXT NOT NULL DEFAULT 'loan',
  total_amount     DECIMAL(12,2) NOT NULL,
  down_payment     DECIMAL(12,2) NOT NULL DEFAULT 0,
  loan_amount      DECIMAL(12,2),
  interest_rate    DECIMAL(5,3),
  term_months      INTEGER,
  monthly_payment  DECIMAL(10,2),
  balloon_payment  DECIMAL(12,2),
  status           TEXT NOT NULL DEFAULT 'simulation',
  contract_date    TEXT,
  notes            TEXT,
  created_by       TEXT,
  created_at       TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at       TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_payment_plans_customer ON payment_plans(customer_id);
CREATE INDEX IF NOT EXISTS idx_payment_plans_status   ON payment_plans(status);

-- シードデータ
INSERT OR IGNORE INTO payment_plans VALUES
  ('PP-001', 'L-001', 'C-001', 'V-001', 'loan',
   3200000, 500000, 2700000, 1.9, 60, 47500, NULL,
   'approved', date('now', '-2 days'), '60回均等払い。金利優遇適用。',
   'EMP-002', datetime('now', '-2 days'), datetime('now', '-2 days')),
  ('PP-002', 'L-003', 'C-003', 'V-006', 'lease',
   4500000, 0, NULL, NULL, 36, 85000, 1500000,
   'simulation', NULL, '残価設定型ローンシミュレーション。',
   'EMP-003', datetime('now', '-1 day'), datetime('now', '-1 day')),
  ('PP-003', 'L-005', 'C-005', 'V-002', 'cash',
   2800000, 2800000, NULL, NULL, NULL, NULL, NULL,
   'approved', date('now'), '一括現金払い。',
   'EMP-002', datetime('now'), datetime('now'));
