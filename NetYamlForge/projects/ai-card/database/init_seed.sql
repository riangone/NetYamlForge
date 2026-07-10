-- AI Card (AHP) 初期データ
-- デモ用 AI ID とプロフィールを作成

-- Demo AI Identity
INSERT OR IGNORE INTO ai_identity (id, ai_id, display_name, owner_type, organization, role, email, is_active, verified, created_at, updated_at)
VALUES
  (1, 'ai://demo/yamada-taro', '山田 太郎', 'individual', 'ABC Corporation', 'Sales Director', 'yamada@example.com', 1, 1, datetime('now'), datetime('now')),
  (2, 'ai://demo/tanaka-hanako', '田中 花子', 'individual', 'XYZ Tech', 'CTO', 'tanaka@example.com', 1, 1, datetime('now'), datetime('now')),
  (3, 'ai://demo/abc-corp', 'ABC Corporation', 'organization', 'ABC Corporation', NULL, 'info@abc-corp.example.com', 1, 0, datetime('now'), datetime('now'));

-- Demo AI Profiles
INSERT OR IGNORE INTO ai_profile (id, ai_identity_id, goals_json, can_share_json, cannot_share_json, expertise_json, greeting_message, ai_instructions, updated_at)
VALUES
  (1, 1,
   '["製造業のDX推進", "海外展開の支援", "IoTソリューション導入"]',
   '["会社概要", "製品カタログ", "導入事例"]',
   '["価格表", "未発表製品情報"]',
   '["製造業DX", "IoT", "業務改善"]',
   'はじめまして。ABC Corporation の山田太郎の AI アシスタントです。製造業のDX推進について、お気軽にご相談ください。',
   '丁寧かつ専門的に対応してください。製造業のDXに関する質問には積極的に回答し、関連資料を提案してください。価格情報は共有しないでください。',
   datetime('now')),
  (2, 2,
   '["AI技術の社会実装", "スタートアップ連携"]',
   '["技術ブログ", "登壇資料", "OSS活動"]',
   '["ソースコード", "社内データ"]',
   '["AI/ML", "クラウドアーキテクチャ", "Python"]',
   'こんにちは。XYZ Tech の田中です。AI技術やクラウドアーキテクチャについてお話しましょう。',
   'テクニカルな話題には深く対応し、具体的なコード例や設計パターンを提案してください。',
   datetime('now'));

-- Demo Permissions
INSERT OR IGNORE INTO ahp_permission (id, ai_identity_id, resource_type, resource_name, access_level, is_active, created_at)
VALUES
  (1, 1, 'document', '会社概要', 'public', 1, datetime('now')),
  (2, 1, 'document', '製品カタログ', 'public', 1, datetime('now')),
  (3, 1, 'document', '導入事例', 'trusted', 1, datetime('now')),
  (4, 1, 'document', '価格表', 'private', 1, datetime('now')),
  (5, 2, 'document', '技術ブログ', 'public', 1, datetime('now')),
  (6, 2, 'document', '登壇資料', 'public', 1, datetime('now'));
