-- Framework Showcase デモデータ
-- 作成日：2026-03-24

-- フォーム部品デモデータ
INSERT INTO FormComponent (TextField, EmailField, NumberField, DateField, BoolToggle, CreatedAt) VALUES
('サンプルテキスト 1', 'test1@example.com', 100, '2026-01-15', 1, datetime('now')),
('サンプルテキスト 2', 'test2@example.com', 200, '2026-02-20', 0, datetime('now')),
('サンプルテキスト 3', 'test3@example.com', 300, '2026-03-10', 1, datetime('now')),
('デモデータ 4', 'demo4@example.com', 450, '2026-03-15', 1, datetime('now')),
('テストデータ 5', 'test5@example.com', 500, '2026-03-20', 0, datetime('now'));

-- フィルター演示デモデータ
INSERT INTO FilterDemo (Title, Category, Status, Priority, Tags, Author, ViewCount, Rating, IsFeatured, PublishedDate, CreatedAt) VALUES
('記事タイトル 1', 'news', 'published', 'high', 'important,beginner', '著者 A', 1500, 4.5, 1, '2026-03-01', datetime('now')),
('チュートリアル：入門編', 'tutorial', 'published', 'medium', 'beginner', '著者 B', 2300, 4.8, 1, '2026-03-05', datetime('now')),
('重要なお知らせ', 'announcement', 'published', 'urgent', 'important,update', '著者 C', 3500, 4.2, 1, '2026-03-10', datetime('now')),
('イベント告知：春のキャンペーン', 'event', 'published', 'high', 'important', '著者 A', 1800, 4.0, 0, '2026-03-15', datetime('now')),
('技術記事：上級者向け', 'article', 'draft', 'low', 'advanced', '著者 B', 500, 3.5, 0, '2026-03-18', datetime('now')),
('アップデート情報 v2.0', 'news', 'published', 'medium', 'update,important', '著者 C', 2800, 4.6, 1, '2026-03-20', datetime('now')),
('初心者ガイド', 'tutorial', 'published', 'low', 'beginner', '著者 A', 1200, 4.3, 0, '2026-03-22', datetime('now')),
('中間者向けテクニック', 'article', 'published', 'medium', 'intermediate', '著者 B', 900, 4.1, 0, '2026-03-23', datetime('now')),
('緊急メンテナンスのお知らせ', 'announcement', 'published', 'urgent', 'important', '著者 C', 4200, 3.8, 1, '2026-03-24', datetime('now')),
('週末イベント', 'event', 'draft', 'low', '', '著者 A', 0, 0.0, 0, '2026-03-28', datetime('now'));

-- レイアウト演示デモデータ
INSERT INTO LayoutDemo (Title, Description, Category, Status, SortOrder, IsPublic, PublishedDate) VALUES
('アイテム 1', 'これはサンプル説明 1 です', 'type_a', 'active', 1, 1, '2026-01-10'),
('アイテム 2', 'これはサンプル説明 2 です', 'type_b', 'active', 2, 1, '2026-02-15'),
('アイテム 3', 'これはサンプル説明 3 です', 'type_c', 'active', 3, 1, '2026-03-01'),
('アイテム 4', 'これはサンプル説明 4 です', 'type_a', 'inactive', 4, 0, '2026-03-10'),
('アイテム 5', 'これはサンプル説明 5 です', 'type_b', 'active', 5, 1, '2026-03-15'),
('アイテム 6', 'これはサンプル説明 6 です', 'type_c', 'active', 6, 1, '2026-03-20'),
('アイテム 7', 'これはサンプル説明 7 です', 'type_a', 'active', 7, 1, '2026-03-22'),
('アイテム 8', 'これはサンプル説明 8 です', 'type_b', 'inactive', 8, 0, '2026-03-23'),
('アイテム 9', 'これはサンプル説明 9 です', 'type_c', 'active', 9, 1, '2026-03-24'),
('アイテム 10', 'これはサンプル説明 10 です', 'type_a', 'active', 10, 1, '2026-03-24');

-- バッチ処理演示デモデータ
INSERT INTO BatchJobDemo (JobName, JobType, Schedule, SqlFile, OutputFile, IsEnabled, Status, RetryCount, MaxRetryCount, TimeoutSeconds, Description) VALUES
('日次 CSV エクスポート', 'sql_to_csv', '0 2 * * *', 'exports/sql/daily_export.sql', 'exports/daily_{date:yyyyMMdd}.csv', 1, 'success', 0, 3, 300, '毎日のデータエクスポート'),
('週次レポート生成', 'sql_to_csv', '0 9 * * 1', 'exports/sql/weekly_report.sql', 'exports/weekly_{date:yyyyMMdd}.csv', 1, 'idle', 0, 2, 600, '週次レポート PDF 生成'),
('データクリーンアップ', 'sql_command', '0 3 * * 0', 'jobs/sql/cleanup.sql', '', 0, 'idle', 0, 1, 180, '古いデータの削除'),
('月次集計', 'sql_to_csv', '0 4 1 * *', 'exports/sql/monthly_summary.sql', 'exports/monthly_{date:yyyyMMdd}.csv', 1, 'success', 0, 3, 900, '月次データ集計'),
('バックアップ', 'sql_command', '0 1 * * *', 'jobs/sql/backup.sql', '', 1, 'running', 0, 5, 1800, '毎時バックアップ');

-- フック演示デモデータ
INSERT INTO HookDemo (Title, Content, Status, Priority, Assignee, DueDate, IsArchived) VALUES
('タスク 1: 要件定義', '要件定義書を作成する', 'approved', 'high', '山田', '2026-04-01', 0),
('タスク 2: 設計書作成', '基本設計書を作成する', 'pending', 'high', '佐藤', '2026-04-05', 0),
('タスク 3: 実装', '機能 A の実装', 'draft', 'medium', '鈴木', '2026-04-10', 0),
('タスク 4: テスト', '単体テストを実施', 'draft', 'medium', '高橋', '2026-04-15', 0),
('タスク 5: 資料作成', 'ユーザーマニュアル作成', 'pending', 'low', '伊藤', '2026-04-20', 0),
('タスク 6: レビュー', 'コードレビュー', 'approved', 'high', '山田', '2026-04-08', 0),
('タスク 7: バグ修正', '報告されたバグを修正', 'rejected', 'urgent', '佐藤', '2026-03-30', 0),
('タスク 8: 機能追加', '新機能の検討', 'draft', 'low', '鈴木', '2026-04-25', 1),
('タスク 9: パフォーマンス改善', 'レスポンスタイム改善', 'pending', 'medium', '高橋', '2026-04-12', 0),
('タスク 10: セキュリティ対策', '脆弱性対応', 'approved', 'urgent', '伊藤', '2026-03-28', 0);

-- エクスポート演示デモデータ
INSERT INTO ExportDemo (ProductName, Category, Price, Stock, Rating, ReleaseDate, Description, IsActive) VALUES
('スマートフォン X1', 'electronics', 89800, 150, 4.5, '2026-01-15', '最新のスマートフォン', 1),
('ワイヤレスイヤホン', 'electronics', 12800, 300, 4.3, '2026-02-01', 'ノイズキャンセリング機能付き', 1),
('ラップトップ Pro', 'electronics', 158000, 50, 4.7, '2026-01-20', 'プロフェッショナル向けラップトップ', 1),
('スマートウォッチ', 'electronics', 29800, 200, 4.2, '2026-03-01', 'ヘルスケア機能搭載', 1),
('T シャツ（コットン）', 'clothing', 3980, 500, 4.0, '2026-02-15', '快適な着心地', 1),
('デニムジャケット', 'clothing', 8900, 100, 4.4, '2026-03-10', 'ヴィンテージ加工', 1),
('オーガニックコーヒー', 'food', 2500, 1000, 4.6, '2026-01-05', 'フェアトレード認証', 1),
('プログラミング入門書', 'books', 2980, 800, 4.8, '2026-02-20', '初心者向け解説書', 1),
('AI 技術解説書', 'books', 4500, 300, 4.5, '2026-03-15', '最新 AI 技術を解説', 1),
('高級ヘッドフォン', 'electronics', 55000, 80, 4.9, '2026-03-20', 'スタジオモニター品質', 1),
('ゲーミングモニター', 'electronics', 68000, 60, 4.6, '2026-02-10', '144Hz 駆動', 1),
('ランニングシューズ', 'clothing', 15800, 250, 4.3, '2026-03-05', '軽量設計', 1),
('スペシャルティコーヒー', 'food', 3800, 500, 4.7, '2026-03-12', 'シングルオリジン', 1),
('データサイエンス教科書', 'books', 5200, 400, 4.4, '2026-01-25', '実践的解説', 1),
('タブレット端末', 'electronics', 78000, 100, 4.5, '2026-03-18', '10 インチディスプレイ', 1);
