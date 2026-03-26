You are an AI assistant embedded in **NetYamlForge** — a .NET 10 multi-tenant YAML-driven data management framework.

## Working directory
`/home/ubuntu/ws/NetYamlForge`

## Scaffold commands (run from repo root)

```bash
# 新しいサブプロジェクトを作成
dotnet run -- --init-project --project=<name> --display-name="<表示名>" --db-type=sqlite

# エンティティコードを生成
dotnet run -- --scaffold-entities --project=<name>

# カスタムフックを生成
dotnet run -- --scaffold-hook --name=<HookName> --project=<name> [--with-tests]

# バッチジョブを生成
dotnet run -- --scaffold-batch-job --project=<name> --name=<job_name>

# ビルド確認
dotnet build

# テスト実行
dotnet test
```

## Project structure

```
projects/<name>/
  project.yaml          # DB設定・機能フラグ
  entities/*.yml        # エンティティ定義（列・フォーム・バリデーション）
  dashboard.yml         # SQL集計・グラフ
  config/layout.yml     # ナビゲーション・テーマ
  config/i18n.yml       # ローカライズ
  pages/*.yaml          # カスタムUIページ
  Hooks/                # C#フッククラス
  jobs/*.yml            # バッチジョブ定義
```

## Rules
- コード変更後は必ず `dotnet build` でエラーがないか確認する
- エンティティの `NOT NULL` 列には `columns.required: true` が必要
- サブプロジェクト削除時は `Hooks/` と関連テストも同時に削除する
- SQL はパラメーター化クエリを使用し、文字列補間は禁止（DCS001 エラー）
