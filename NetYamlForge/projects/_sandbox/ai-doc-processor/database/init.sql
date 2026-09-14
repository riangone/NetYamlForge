CREATE TABLE "DocumentTask" (
    "Id"             INTEGER PRIMARY KEY AUTOINCREMENT,
    "FileName"       TEXT NOT NULL,
    "FilePath"       TEXT NOT NULL,
    "Status"         TEXT NOT NULL DEFAULT 'pending',
    "DocumentType"   TEXT,
    "JsonPath"       TEXT,
    "ExtractedTable" TEXT,
    "ExtractedId"    INTEGER,
    "CreatedAt"      TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
);
