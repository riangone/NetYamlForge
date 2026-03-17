PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Notebook (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Name TEXT NOT NULL,
  Color TEXT NOT NULL DEFAULT 'slate',
  CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS Memo (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  NotebookId INTEGER,
  Title TEXT NOT NULL,
  Content TEXT,
  Status TEXT NOT NULL DEFAULT 'active',
  IsPinned INTEGER NOT NULL DEFAULT 0,
  DueDate TEXT,
  CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
  UpdatedAt TEXT,
  FOREIGN KEY (NotebookId) REFERENCES Notebook(Id)
);

CREATE INDEX IF NOT EXISTS IX_Memo_NotebookId ON Memo(NotebookId);
CREATE INDEX IF NOT EXISTS IX_Memo_Status ON Memo(Status);

DELETE FROM Memo;
DELETE FROM Notebook;
DELETE FROM sqlite_sequence WHERE name IN ('Memo', 'Notebook');

INSERT INTO Notebook (Name, Color) VALUES
('Personal', 'blue'),
('Work', 'emerald'),
('Ideas', 'violet');

INSERT INTO Memo (NotebookId, Title, Content, Status, IsPinned, DueDate, UpdatedAt) VALUES
(1, 'Buy groceries', 'Milk, eggs, coffee beans', 'active', 1, date('now', '+1 day'), datetime('now')),
(1, 'Call mom', 'Weekend check-in call', 'active', 0, date('now', '+2 day'), datetime('now')),
(2, 'Prepare sprint demo', 'Collect screenshots and KPI summary', 'active', 1, date('now', '+3 day'), datetime('now')),
(2, 'Refactor import service', 'Remove duplicate parsing flow', 'archived', 0, NULL, datetime('now')),
(3, 'Side project concept', 'Offline-first memo syncing design', 'active', 0, NULL, datetime('now'));
