-- Phase 4-C: 試乗記録管理（test_drives テーブル）

CREATE TABLE IF NOT EXISTS test_drives (
  test_drive_id     TEXT PRIMARY KEY,
  lead_id           TEXT,
  customer_id       TEXT NOT NULL,
  vehicle_id        TEXT NOT NULL,
  assigned_staff_id TEXT,
  branch_id         TEXT DEFAULT 'BR-001',
  scheduled_at      TEXT NOT NULL,
  actual_start_at   TEXT,
  actual_end_at     TEXT,
  status            TEXT NOT NULL DEFAULT 'scheduled',
  feedback_score    INTEGER,
  feedback_notes    TEXT,
  staff_notes       TEXT,
  next_action       TEXT DEFAULT 'revisit',
  created_at        TEXT NOT NULL DEFAULT (datetime('now')),
  updated_at        TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_test_drives_customer ON test_drives(customer_id);
CREATE INDEX IF NOT EXISTS idx_test_drives_vehicle  ON test_drives(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_test_drives_status   ON test_drives(status);

-- シードデータ
INSERT OR IGNORE INTO test_drives VALUES
  ('TD-001', 'L-001', 'C-001', 'V-001', 'EMP-002', 'BR-001',
   datetime('now', '-5 days', '+10 hours'), datetime('now', '-5 days', '+10 hours'), datetime('now', '-5 days', '+11 hours'),
   'completed', 5, '乗り心地がとても良かった。購入を前向きに検討したい。', 'お客様のご反応は非常に良好。即見積提案を推奨。',
   'quote', datetime('now', '-5 days'), datetime('now', '-5 days')),
  ('TD-002', 'L-002', 'C-002', 'V-003', 'EMP-003', 'BR-001',
   datetime('now', '-3 days', '+14 hours'), datetime('now', '-3 days', '+14 hours'), datetime('now', '-3 days', '+15 hours'),
   'completed', 4, '燃費と室内空間が気に入った。もう一度考えたい。', 'ファミリー層向けアピールが有効。再来店を促す。',
   'revisit', datetime('now', '-3 days'), datetime('now', '-3 days')),
  ('TD-003', 'L-004', 'C-004', 'V-002', 'EMP-002', 'BR-002',
   datetime('now', '+1 day', '+11 hours'), NULL, NULL,
   'scheduled', NULL, NULL, NULL,
   'revisit', datetime('now'), datetime('now')),
  ('TD-004', 'L-007', 'C-007', 'V-004', 'EMP-003', 'BR-001',
   datetime('now', '-1 day', '+15 hours'), NULL, NULL,
   'no_show', NULL, NULL, 'お客様から事前連絡なしでキャンセル。翌日電話フォロー実施。',
   'revisit', datetime('now', '-2 days'), datetime('now', '-1 day')),
  ('TD-005', 'L-010', 'C-010', 'V-005', 'EMP-007', 'BR-002',
   datetime('now', '+2 days', '+10 hours'), NULL, NULL,
   'scheduled', NULL, NULL, NULL,
   'revisit', datetime('now'), datetime('now'));
