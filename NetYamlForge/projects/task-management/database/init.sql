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
