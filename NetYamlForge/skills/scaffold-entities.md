---
name: エンティティ生成
icon: 📋
description: DB スキーマからエンティティ YAML を生成
needsInput: true
inputPlaceholder: プロジェクト名を入力...
order: 2
---

DB スキーマを解析してエンティティ YAML を生成してください。

## コマンド

```bash
# 基本（既存ファイルを上書き）
dotnet run -- --scaffold-entities --project=<name>

# 既存ファイルを保持して新規のみ追加
dotnet run -- --scaffold-entities --project=<name> --no-overwrite

# 出力ディレクトリを指定
dotnet run -- --scaffold-entities --project=<name> --output-dir=my-entities

# i18n labelKey を YAML に含める
dotnet run -- --scaffold-entities --project=<name> --with-label-keys
```

## オプション

| オプション | デフォルト | 説明 |
|---|---|---|
| `--no-overwrite` | false | 既存ファイルをスキップ（新規テーブルのみ生成） |
| `--output-dir` | entities.generated | 出力先ディレクトリ名 |
| `--with-label-keys` | false | i18n の `labelKey` フィールドを追加 |

## 生成されるファイル

- `projects/<name>/entities.generated/<TableName>.yml` — テーブルごとに1ファイル
  - 列定義（型・必須・デフォルト値）
  - フォーム設定
  - 外部キー・リレーション
  - ページング・フィルター設定

生成後に `entities/` へコピーしてカスタマイズしてください。
`NOT NULL` かつデフォルト値なしの列には必ず `required: true` を設定してください。

プロジェクト名:
