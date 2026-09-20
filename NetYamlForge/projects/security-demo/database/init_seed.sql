-- ============================================================
-- security-demo - SQLite Schema & Seed Data
--
-- This is the framework's single official demo project. It exists to prove,
-- with a real running app, that NetYamlForge is a "safe by construction"
-- scaffold: a small CRUD app (Note / Attachment) plus a documented set of
-- guardrails that reject unsafe patterns an AI code generator might produce.
-- See ../README.md for the full list of guardrails and which automated test
-- proves each one.
-- ============================================================

CREATE TABLE IF NOT EXISTS Note (
  Id           INTEGER PRIMARY KEY AUTOINCREMENT,
  Title        TEXT    NOT NULL,
  Slug         TEXT    NOT NULL UNIQUE,
  Summary      TEXT,
  Content      TEXT,
  AuthorName   TEXT    NOT NULL DEFAULT 'Admin',
  Status       TEXT    NOT NULL DEFAULT 'draft',
  FeaturedFlag INTEGER NOT NULL DEFAULT 0,
  ViewCount    INTEGER NOT NULL DEFAULT 0,
  PublishedAt  TEXT,
  CreatedAt    TEXT    NOT NULL DEFAULT (datetime('now')),
  UpdatedAt    TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS Attachment (
  Id         INTEGER PRIMARY KEY AUTOINCREMENT,
  NoteId     INTEGER NOT NULL,
  FileName   TEXT    NOT NULL,
  StoredPath TEXT    NOT NULL,
  CreatedAt  TEXT    NOT NULL DEFAULT (datetime('now')),
  FOREIGN KEY (NoteId) REFERENCES Note(Id)
);

INSERT OR IGNORE INTO Note (Id, Title, Slug, Summary, Content, AuthorName, Status, FeaturedFlag, ViewCount, PublishedAt, CreatedAt, UpdatedAt) VALUES
  (1,
   'Why Guardrails Beat Another Demo',
   'why-guardrails-beat-another-demo',
   'NetYamlForge stops treating "add one more vertical demo" as the roadmap and starts treating compile-time and runtime guardrails as the product.',
   '## The problem

Adding another business-vertical demo (a CRM, a blog, an inventory tool) does not make a low-code framework safer. It only makes the repository bigger.

## What actually matters

NetYamlForge validates YAML-declared entity metadata (table/column/join names,
filter and column expressions) through `DynamicCrudMetadataValidator` and
`SqlSafetyGuard` before any query is built, and validates filesystem paths
through `PathSafetyGuard` before any file is touched. Both reject unsafe
input by throwing, not by silently sanitizing.

## This demo

This project (`security-demo`) is intentionally the *only* first-class demo
shipped with the framework. It is a small CRUD app (Note / Attachment) with
one custom action (`publish`) — just enough surface to exercise the generic
DynamicEntity pipeline end to end. See `README.md` for what is deliberately
*not* included, and why.',
   'Admin', 'published', 1, 128,
   '2026-01-10 09:00:00', '2026-01-10 08:00:00', '2026-01-10 09:00:00'),

  (2,
   'Safe by Construction: A Field Guide',
   'safe-by-construction-field-guide',
   'A short tour of the three guardrails this framework enforces, and the automated test that proves each one actually rejects unsafe input.',
   '## The three guardrails

1. `DynamicCrudMetadataValidator` — rejects unsafe identifiers/expressions/join
   types found in YAML-declared entity metadata at load time.
2. `SqlSafetyGuard` — the shared regex/keyword allowlist the validator above
   is built on; also used directly by tool-call validation for AI agents.
3. `PathSafetyGuard` — rejects any resolved path that escapes its declared
   base directory (classic `../../` traversal), used wherever user input
   reaches the filesystem.

See `NetYamlForge.Tests/SecurityDemoGuardRailTests.cs` for the runnable proof
of all three.',
   'Admin', 'published', 0, 64,
   '2026-01-12 09:00:00', '2026-01-12 08:00:00', '2026-01-12 09:00:00');

INSERT OR IGNORE INTO Attachment (Id, NoteId, FileName, StoredPath, CreatedAt) VALUES
  (1, 1, 'guardrails-overview.pdf', 'notes/1/guardrails-overview.pdf', '2026-01-10 09:05:00');
