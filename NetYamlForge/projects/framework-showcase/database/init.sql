-- ============================================================
-- framework-showcase database initialization script
-- ============================================================
-- Usage:
--   sqlite3 database/framework-showcase.db < database/init.sql
-- Note: Drops and recreates all framework-showcase tables.
--       Framework tables (AppUser, AuditLog, etc.) are left untouched.
-- ============================================================

PRAGMA foreign_keys = OFF;

-- ---- Drop tables (reverse dependency order) ----
DROP TABLE IF EXISTS ExportDemo;
DROP TABLE IF EXISTS HookDemo;
DROP TABLE IF EXISTS BatchJobDemo;
DROP TABLE IF EXISTS LayoutDemo;
DROP TABLE IF EXISTS FilterDemo;
DROP TABLE IF EXISTS FormComponent;

-- ---- Create tables ----

-- フォーム部品演示：30 種類のフォームタイプを展示
CREATE TABLE FormComponent (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    TextField         TEXT,
    EmailField        TEXT,
    UrlField          TEXT,
    TelField          TEXT,
    PasswordField     TEXT,
    TextArea          TEXT,
    RichText          TEXT,
    Markdown          TEXT,
    Code              TEXT,
    NumberField       INTEGER,
    DecimalField      REAL,
    MoneyField        REAL,
    PercentField      REAL,
    RangeField        REAL,
    RatingField       REAL,
    DateField         TEXT,
    DateTimeField     TEXT,
    DateRangeField    TEXT,
    SelectField       TEXT,
    RadioField        TEXT,
    ToggleGroupField  TEXT,
    MultiSelectField  TEXT,
    CheckboxGroupField TEXT,
    SwitchGroupField  TEXT,
    BoolToggle        INTEGER,
    FileUpload        TEXT,
    ImageUpload       TEXT,
    ColorPicker       TEXT,
    TagsField         TEXT,
    AutocompleteField TEXT,
    JsonField         TEXT,
    SignatureField    TEXT,
    MapField          TEXT,
    SortableListField TEXT
);

-- フィルター演示：10 種類のフィルターを展示
CREATE TABLE FilterDemo (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Title           TEXT    NOT NULL,
    Category        TEXT,
    Status          TEXT,
    Priority        TEXT,
    Tags            TEXT,
    Author          TEXT,
    Description     TEXT,
    ViewCount       INTEGER,
    Rating          REAL,
    IsFeatured      INTEGER,
    PublishedDate   TEXT,
    CreatedAt       TEXT
);

-- レイアウト演示：grid/tabs/accordion/wizard レイアウトを展示
CREATE TABLE LayoutDemo (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Title           TEXT    NOT NULL,
    Description     TEXT,
    Category        TEXT,
    Status          TEXT,
    SortOrder       INTEGER,
    IsPublic        INTEGER,
    PublishedDate   TEXT
);

-- バッチ処理演示：Cron スケジュールジョブを展示
CREATE TABLE BatchJobDemo (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    JobName         TEXT    NOT NULL,
    JobType         TEXT    NOT NULL,
    Schedule        TEXT,
    SqlFile         TEXT,
    OutputFile      TEXT,
    IsEnabled       INTEGER,
    LastRunAt       TEXT,
    NextRunAt       TEXT,
    Status          TEXT,
    RetryCount      INTEGER,
    MaxRetryCount   INTEGER,
    TimeoutSeconds  INTEGER,
    Description     TEXT
);

-- フック演示：before/after CRUD フックを展示
CREATE TABLE HookDemo (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Title           TEXT    NOT NULL,
    Content         TEXT,
    Status          TEXT,
    Priority        TEXT,
    Assignee        TEXT,
    DueDate         TEXT,
    CompletedAt     TEXT,
    IsArchived      INTEGER
);

-- エクスポート演示：CSV/JSON/PDF エクスポートを展示
CREATE TABLE ExportDemo (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductName     TEXT    NOT NULL,
    Category        TEXT,
    Price           REAL,
    Stock           INTEGER,
    Description     TEXT,
    IsActive        INTEGER,
    CreatedAt       TEXT
);

PRAGMA foreign_keys = ON;
