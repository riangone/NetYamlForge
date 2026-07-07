-- Task テーブル
CREATE TABLE IF NOT EXISTS "Task" (
    "Id"          INTEGER PRIMARY KEY AUTOINCREMENT,
    "Title"       TEXT    NOT NULL,
    "AssignedTo"  TEXT    NOT NULL,
    "DueDate"     TEXT    NOT NULL,
    "Priority"    TEXT    NOT NULL DEFAULT 'medium',
    "Status"      TEXT    NOT NULL DEFAULT 'not_started',
    "Notes"       TEXT,
    "CreatedBy"   TEXT,
    "CreatedAt"   TEXT,
    "UpdatedAt"   TEXT
);

-- TaskComment テーブル
CREATE TABLE IF NOT EXISTS "TaskComment" (
    "Id"          INTEGER PRIMARY KEY AUTOINCREMENT,
    "TaskId"      INTEGER NOT NULL,
    "CommentText" TEXT    NOT NULL,
    "PostedBy"    TEXT    NOT NULL DEFAULT 'unknown',
    "PostedAt"    TEXT    NOT NULL DEFAULT (datetime('now', 'localtime')),
    FOREIGN KEY ("TaskId") REFERENCES "Task"("Id")
);

-- ===== Seed task data =====
-- デモ／テスト用の初期タスク。再初期化しても一覧・コメントが充実するよう明示IDで投入。
INSERT OR IGNORE INTO "Task"
    ("Id", "Title", "AssignedTo", "DueDate", "Priority", "Status", "Notes", "CreatedBy", "CreatedAt") VALUES
(1, '要件定義書のレビュー',       '田中 一郎', '2026-07-15', 'high',   'in_progress', '関係部署のレビューコメントを反映中。', 'taskmgr_manager', '2026-07-01 09:00:00'),
(2, 'テスト仕様書の作成',        '佐藤 次郎', '2026-07-18', 'medium', 'not_started', 'E2Eの観点表を先に用意する。',        'taskmgr_manager', '2026-07-01 09:05:00'),
(3, 'デプロイ手順書の整備',       '山田 太郎', '2026-07-22', 'low',    'not_started', NULL,                                  'taskmgr_manager', '2026-07-01 09:10:00'),
(4, 'コードレビュー対応',        '鈴木 花子', '2026-07-10', 'high',   'in_progress', '指摘事項は残り3件。',                  'taskmgr_worker1', '2026-07-01 09:15:00'),
(5, 'ステージング環境の確認',      '田中 一郎', '2026-07-12', 'medium', 'on_hold',     'インフラ側の準備待ち。',               'taskmgr_manager', '2026-07-01 09:20:00'),
(6, '本番リリース準備',          '佐藤 次郎', '2026-07-28', 'high',   'not_started', 'リリース判定会議は7/26予定。',          'taskmgr_manager', '2026-07-01 09:25:00'),
(7, '顧客向け説明会の準備',       '鈴木 花子', '2026-07-20', 'medium', 'in_progress', '資料ドラフトを共有済み。',              'taskmgr_worker1', '2026-07-01 09:30:00'),
(8, '障害対応マニュアルの更新',     '山田 太郎', '2026-07-25', 'low',    'completed',   '前回インシデントの振り返りを反映。',        'taskmgr_manager', '2026-07-01 09:35:00');

-- ===== Seed comment data =====
-- 複数タスクにコメントを分散させ、モバイル一覧・詳細でコメントが確認できるようにする。
INSERT OR IGNORE INTO TaskComment (Id, TaskId, CommentText, PostedBy, PostedAt) VALUES
(1,  1, '優先順位を再検討しました。P0タスクから着手します。', 'taskmgr_manager', '2026-07-01 09:00:00'),
(2,  1, '承知しました。P0の洗い出しを開始します。',          'taskmgr_worker1', '2026-07-01 09:30:00'),
(3,  1, 'P0リストをチームに展開しました。',                'taskmgr_manager', '2026-07-01 10:00:00'),
(4,  2, 'E2Eの観点表のテンプレートを共有します。',           'taskmgr_manager', '2026-07-01 11:00:00'),
(5,  2, '観点表を作成しました。レビューお願いします。',         'taskmgr_worker1', '2026-07-02 14:20:00'),
(6,  3, 'デプロイ先の環境変数一覧を添付します。',            'taskmgr_manager', '2026-07-02 09:10:00'),
(7,  4, '指摘のうち命名規則の件は修正済みです。',            'taskmgr_worker1', '2026-07-02 16:40:00'),
(8,  4, '残りの2件はリファクタ範囲が広いので別タスク化を提案します。', 'taskmgr_worker1', '2026-07-03 10:05:00'),
(9,  4, '別タスク化に賛成です。チケットを切ってください。',       'taskmgr_manager', '2026-07-03 10:30:00'),
(10, 5, 'インフラ準備は7/9完了予定と連絡ありました。',        'taskmgr_manager', '2026-07-03 13:00:00'),
(11, 6, 'リリースチェックリストを更新しました。',            'taskmgr_manager', '2026-07-04 09:00:00'),
(12, 7, '説明会資料のドラフトv1を共有しました。',           'taskmgr_worker1', '2026-07-04 15:15:00'),
(13, 7, '構成は良いです。デモ画面のスクショを追加しましょう。',    'taskmgr_manager', '2026-07-05 09:45:00'),
(14, 8, 'マニュアル更新完了。次回定例で周知します。',          'taskmgr_worker1', '2026-07-05 17:00:00');
