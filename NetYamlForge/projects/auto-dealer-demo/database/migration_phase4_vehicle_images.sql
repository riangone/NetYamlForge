-- Phase 4-B: 車両複数画像管理（vehicle_images テーブル）

CREATE TABLE IF NOT EXISTS vehicle_images (
  image_id    TEXT PRIMARY KEY,
  vehicle_id  TEXT NOT NULL,
  image_url   TEXT NOT NULL,
  caption     TEXT,
  image_type  TEXT NOT NULL DEFAULT 'exterior',
  sort_order  INTEGER DEFAULT 0,
  is_primary  INTEGER DEFAULT 0,
  uploaded_by TEXT,
  created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_vehicle_images_vehicle_id ON vehicle_images(vehicle_id);

-- シードデータ（サンプル車両の画像）
INSERT OR IGNORE INTO vehicle_images (image_id, vehicle_id, image_url, caption, image_type, sort_order, is_primary, uploaded_by, created_at) VALUES
  ('IMG-001', 'V-001', 'https://images.unsplash.com/photo-1549399542-7e3f8b79c341?w=800', '外観（フロント）', 'exterior', 1, 1, 'EMP-002', datetime('now')),
  ('IMG-002', 'V-001', 'https://images.unsplash.com/photo-1580273916550-e323be2ae537?w=800', '外観（サイド）', 'exterior', 2, 0, 'EMP-002', datetime('now')),
  ('IMG-003', 'V-001', 'https://images.unsplash.com/photo-1494976388531-d1058494cdd8?w=800', 'インテリア', 'interior', 3, 0, 'EMP-002', datetime('now')),
  ('IMG-004', 'V-002', 'https://images.unsplash.com/photo-1621007947382-bb3c3994e3fb?w=800', '外観（フロント）', 'exterior', 1, 1, 'EMP-003', datetime('now')),
  ('IMG-005', 'V-002', 'https://images.unsplash.com/photo-1552519507-da3b142c6e3d?w=800', 'エンジンルーム', 'engine', 2, 0, 'EMP-003', datetime('now')),
  ('IMG-006', 'V-003', 'https://images.unsplash.com/photo-1606664515524-ed2f786a0bd6?w=800', '外観（全景）', 'exterior', 1, 1, 'EMP-002', datetime('now')),
  ('IMG-007', 'V-004', 'https://images.unsplash.com/photo-1541348263662-e068662d82af?w=800', '外観（フロント）', 'exterior', 1, 1, 'EMP-003', datetime('now')),
  ('IMG-008', 'V-004', 'https://images.unsplash.com/photo-1555215695-3004980ad54e?w=800', 'インテリア（ダッシュボード）', 'interior', 2, 0, 'EMP-003', datetime('now')),
  ('IMG-009', 'V-005', 'https://images.unsplash.com/photo-1558618666-fcd25c85cd64?w=800', '外観（フロント）', 'exterior', 1, 1, 'EMP-002', datetime('now')),
  ('IMG-010', 'V-006', 'https://images.unsplash.com/photo-1617788138017-80ad40651399?w=800', '外観（フロント）', 'exterior', 1, 1, 'EMP-002', datetime('now'));
