# Entity YAML 自動生成ワークフロー

## 目的
DBスキーマから `entities.generated/` を自動生成し、手作業ミスを減らします。  
業務ロジックとUI定義は `entities/` 側で上書きして管理します。

## 基本方針
- `projects/<project>/entities.generated/*.yml`:
  DB由来のベース定義。直接編集しない。
- `projects/<project>/entities/*.yml`:
  業務ロジック・UI・ラベル・フィルタ・リンクなどを定義。
- 読み込み順:
  `entities.generated` → `entities-{provider}` → `entities`（後勝ち上書き）。

## 生成コマンド
- 全プロジェクト:
  `dotnet run -- --scaffold-entities`
- 単一プロジェクト:
  `dotnet run -- --scaffold-entities --project=b2b-order-ops`
- 既存 generated を上書きしない:
  `dotnet run -- --scaffold-entities --project=b2b-order-ops --no-overwrite`

## 自動生成される主な項目
- `table` / `key` / `keys`
- `columns` の `type` / `required` / `identity`
- `forms` の `required` / `editable`
- FK列の `forms.*.foreignKey`（`entity` と `displayColumn`）

## 運用手順
1. DBスキーマ変更後に `--scaffold-entities` を実行。
2. 生成差分を確認。
3. `entities/` 側で必要な業務定義のみ追加・調整。
4. 起動時の整合チェック（YAML vs DB）エラーがないことを確認。
