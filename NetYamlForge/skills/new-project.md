---
name: 新規プロジェクト
icon: 🏗️
description: 新しいサブプロジェクトをスキャフォールド
needsInput: true
inputPlaceholder: プロジェクト名を入力...
order: 1
---

新しいサブプロジェクトを作成してください。

## コマンド

```bash
# SQLite（最小構成）
dotnet run -- --init-project \
  --project=<name> \
  --display-name="<表示名>" \
  --db-type=sqlite

# SQLite（DB パス指定）
dotnet run -- --init-project \
  --project=<name> \
  --display-name="<表示名>" \
  --db-type=sqlite \
  --db-path=database/<name>.db

# SQL Server
dotnet run -- --init-project \
  --project=<name> \
  --display-name="<表示名>" \
  --db-type=sqlserver \
  --db-connection="Server=...;Database=...;..."

# PostgreSQL / MySQL も同様（--db-type=postgresql|mysql）
```

## オプション

| オプション | デフォルト | 説明 |
|---|---|---|
| `--db-type` | sqlite | sqlite / sqlserver / postgresql / mysql |
| `--db-path` | database/\<name\>.db | SQLite ファイルパス |
| `--db-connection` | — | SQLite 以外は必須 |
| `--no-auto-scaffold` | false | DB からのエンティティ自動生成をスキップ |
| `--force` | false | 既存ディレクトリを上書き |

## 生成されるファイル

- `projects/<name>/project.yaml` — DB設定・機能フラグ
- `projects/<name>/dashboard.yml` — ダッシュボード定義
- `projects/<name>/config/layout.yml` — ナビゲーション
- `projects/<name>/config/i18n.yml` — ローカライズ
- `projects/<name>/entities/*.yml` — エンティティ定義（自動生成）
- `projects/<name>/views/` — Razor レイアウト

実行後に `dotnet build` でビルドを確認してください。

プロジェクト名:
