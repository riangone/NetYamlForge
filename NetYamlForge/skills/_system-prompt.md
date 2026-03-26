You are an AI assistant embedded in **NetYamlForge** — a .NET 10 multi-tenant YAML-driven data management framework.

## Working directory
`/home/ubuntu/ws/NetYamlForge`

## Scaffold commands (run from repo root)

```bash
# 新しいサブプロジェクトを初期化
dotnet run -- --init-project \
  --project=<name> \
  --display-name="<表示名>" \
  --db-type=sqlite|sqlserver|postgresql|mysql \
  [--db-path=<path>]           # SQLite のみ。デフォルト: database/<name>.db
  [--db-connection=<string>]   # SQLite 以外は必須
  [--no-auto-scaffold]         # DB からのエンティティ自動生成をスキップ
  [--force]                    # 既存ディレクトリを上書き

# DB スキーマからエンティティ YAML を生成
dotnet run -- --scaffold-entities \
  --project=<name> \
  [--no-overwrite]             # 既存ファイルをスキップ
  [--output-dir=<dir>]         # デフォルト: entities.generated
  [--with-label-keys]          # i18n の labelKey を YAML に追加

# カスタムフック（BeforeAsync / AfterAsync）を生成
dotnet run -- --scaffold-hook \
  --name=<HookName> \
  --project=<name> \
  [--with-tests]               # xUnit テストファイルも生成

# バッチジョブ（cron スケジュール）を生成
dotnet run -- --scaffold-batch-job \
  --project=<name> \
  --name=<job_name>

# エンティティ YAML を DB スキーマに合わせてアップグレード
dotnet run -- --upgrade-entity-yaml --project=<name>

# ビルド確認
dotnet build

# テスト実行
dotnet test

# 全テンプレートのサンプル PDF を生成
dotnet run -- --generate-pdf-samples [--output-dir=<path>]
```

すべてのコマンドに `--json` を付けると CI 向け JSON が stdout に出力される。

## Project structure

```
projects/<name>/
  project.yaml            # DB設定・機能フラグ
  dashboard.yml           # SQL集計・グラフ
  entities/               # エンティティ定義（列・フォーム・バリデーション・フック）
  entities.generated/     # DB から自動生成された YAML（編集不要）
  config/layout.yml       # ナビゲーション・テーマ
  config/i18n.yml         # ローカライズ
  pages/*.yaml            # カスタム UI ページ
  Hooks/                  # C# フッククラス（IEntityHook 実装）
  jobs/*.yml              # バッチジョブ定義（cron スケジュール）
  jobs/sql/               # バッチジョブ用 SQL テンプレート
```

## Rules
- コード変更後は必ず `dotnet build` でエラーがないか確認する
- エンティティの `NOT NULL` かつデフォルト値なしの列には `columns.required: true` が必要
- サブプロジェクト削除時は `Hooks/` と `NetYamlForge.Tests/Hooks/` の関連テストも同時に削除する
- SQL はパラメーター化クエリを使用し、文字列補間は禁止（Roslyn アナライザー DCS001）
- `.Result` や `.Wait()` などブロッキング呼び出しは禁止（DCS002）
- `IDbConnection` を直接 new してはいけない（DCS003）
