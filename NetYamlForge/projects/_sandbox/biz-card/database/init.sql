-- ============================================================
-- biz-card: 名刺管理システム データベーススキーマ
-- ============================================================

PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

-- ─── 会社テーブル ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS companies (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    name            TEXT    NOT NULL,
    name_kana       TEXT,
    industry        TEXT,
    website         TEXT,
    address         TEXT,
    phone           TEXT,
    card_count      INTEGER DEFAULT 0,
    created_at      TEXT    DEFAULT (datetime('now','localtime'))
);

-- ─── 名刺テーブル ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS business_cards (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    card_no             TEXT    UNIQUE,
    last_name           TEXT,
    first_name          TEXT,
    last_name_kana      TEXT,
    first_name_kana     TEXT,
    title               TEXT,
    department          TEXT,
    company_id          INTEGER REFERENCES companies(id),
    company_name        TEXT,
    company_name_kana   TEXT,
    industry            TEXT,
    email               TEXT,
    email2              TEXT,
    phone               TEXT,
    mobile              TEXT,
    fax                 TEXT,
    postal_code         TEXT,
    address             TEXT,
    website             TEXT,
    file_path           TEXT,
    thumb_path          TEXT,
    source_type         TEXT    DEFAULT 'manual',
    parse_status        TEXT    DEFAULT 'done',
    embedding_status    TEXT    DEFAULT 'pending',
    notes               TEXT,
    tags                TEXT,
    is_favorite         INTEGER DEFAULT 0,
    received_at         TEXT,
    created_at          TEXT    DEFAULT (datetime('now','localtime')),
    updated_at          TEXT    DEFAULT (datetime('now','localtime'))
);

-- ─── 取込ジョブテーブル ─────────────────────────────────────
CREATE TABLE IF NOT EXISTS import_jobs (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    file_name       TEXT,
    file_path       TEXT,
    file_type       TEXT,
    file_size       INTEGER,
    status          TEXT    DEFAULT 'pending',
    result_card_id  INTEGER REFERENCES business_cards(id),
    parsed_data     TEXT,
    error_message   TEXT,
    created_at      TEXT    DEFAULT (datetime('now','localtime')),
    completed_at    TEXT
);

-- ─── 商業活動テーブル ───────────────────────────────────────
CREATE TABLE IF NOT EXISTS biz_activities (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    card_id          INTEGER REFERENCES business_cards(id),
    activity_type    TEXT,
    subject          TEXT    NOT NULL,
    description      TEXT,
    activity_date    TEXT,
    outcome          TEXT,
    next_action      TEXT,
    next_action_date TEXT,
    priority         TEXT    DEFAULT 'normal',
    status           TEXT    DEFAULT 'planned',
    created_at       TEXT    DEFAULT (datetime('now','localtime'))
);

-- ─── インデックス ────────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_bc_company_id     ON business_cards(company_id);
CREATE INDEX IF NOT EXISTS idx_bc_industry       ON business_cards(industry);
CREATE INDEX IF NOT EXISTS idx_bc_source_type    ON business_cards(source_type);
CREATE INDEX IF NOT EXISTS idx_bc_embedding      ON business_cards(embedding_status);
CREATE INDEX IF NOT EXISTS idx_bc_created_at     ON business_cards(created_at);
CREATE INDEX IF NOT EXISTS idx_act_card_id       ON biz_activities(card_id);
CREATE INDEX IF NOT EXISTS idx_act_status        ON biz_activities(status);
CREATE INDEX IF NOT EXISTS idx_act_next_date     ON biz_activities(next_action_date);
CREATE INDEX IF NOT EXISTS idx_job_status        ON import_jobs(status);
