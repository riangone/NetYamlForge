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
- `projects/<name>/config/home-page.yml` — ホームページ内容（**必須**）
- `projects/<name>/config/layout.yml` — ナビゲーション
- `projects/<name>/config/i18n.yml` — ローカライズ
- `projects/<name>/entities/*.yml` — エンティティ定義（自動生成）
- `projects/<name>/database/init.sql` — スキーマ定義
- `projects/<name>/database/init_seed.sql` — シードデータ（**必須**）
- `projects/<name>/pages/*.yaml` — ナビゲーションに登録した全ページ（**必須**）
- `projects/<name>/views/` — Razor レイアウト

実行後に `dotnet build` でビルドを確認してください。

---

## ✅ 完了基準チェックリスト（AI は必ず確認すること）

プロジェクト作成完了とする前に、以下をすべて満たしていること：

### 1. シードデータ
- [ ] `database/init_seed.sql` が存在する
- [ ] `dashboard.yml` の `stats` / `charts` で参照している**全エンティティ**に、各 5 行以上のシードデータがある
- [ ] シードデータは `init_seed.sql` で `INSERT` 文として実装されており、DB に適用済み

### 2. ホームページ (`config/home-page.yml`)
- [ ] `hero.primaryActionUrl` は実際に存在するページまたはルートを指している
- [ ] `hero.secondaryActionUrl` は実際に存在するページまたはルートを指している
- [ ] `metrics` の `value` / `trend` はプレースホルダー（"Optimized", "Enhanced", "N/A", "TBD" など）ではなく、実際のビジネス指標や説明文を使っている
- [ ] `quickActions` の全 `url` は `/DynamicEntity/List/<entity>`, `/Dashboard`, または `pages/` 配下に存在するページのいずれか
- [ ] `capabilities` の説明はこのプロジェクト固有の内容（汎用テンプレートの流用禁止）

### 3. ナビゲーションリンク (`project.yaml`)
- [ ] `navigation.items` の全 `url` について、以下のいずれかを満たす：
  - `pages/<PageName>.yaml` ファイルが存在する
  - `/DynamicEntity/List/<entity>` 形式で、`entities/<entity>.yml` が存在する
  - `/Dashboard` など、フレームワーク標準ルート
- [ ] ロールベースアクセス（`roles:`）を設定している場合、そのロールがプロジェクトで有効なロールである

### 4. ページ定義 (`pages/`)
- [ ] `navigation.items` で参照している全ページの `pages/<PageName>.yaml` が存在する
- [ ] 各ページ YAML でデータを表示する場合（テーブル・チャートなど）、対応するエンティティが `entities/` に存在する

### 5. 最終確認
- [ ] `dotnet build` が成功する
- [ ] プロジェクト起動後、トップページ → 各ナビゲーションリンクを順に開き、404 / 500 エラーが出ないことを確認

---

プロジェクト名:
