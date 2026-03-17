# NetYamlForge 5分クイックスタート（日本語）

## 0. 目的

最短で「プロジェクト起動 -> ログイン -> CRUD 画面確認 -> YAML 変更反映」まで進めるための手順です。

---

## 1. 起動

```bash
cd NetYamlForge
dotnet restore
dotnet build
dotnet run
```

既定URL:
- `http://localhost:5239`

---

## 2. ログイン

1. ブラウザで `http://localhost:5239/chinook/Dashboard` を開く  
2. ログイン画面で以下を入力
- UserName: `admin`
- Password: `Admin@123`

---

## 3. CRUD 画面を確認

以下を順に確認:

1. 一覧表示  
`/chinook/DynamicEntity/Index?entity=customer`

2. 作成フォーム  
`/chinook/DynamicEntity/CreatePage?entity=customer`

3. 編集フォーム（一覧から任意行を選択）

4. ダッシュボード  
`/chinook/Dashboard`

---

## 4. YAML を1つ変更して反映確認

対象ファイル:
- `projects/chinook/entities/customer.yml`

例: 表示名を変更

```yaml
displayNameI18n:
  ja-JP: 顧客マスタ
```

反映:
1. アプリ再起動（`dotnet run`）
2. `customer` 一覧を再表示
3. タイトル変更を確認

---

## 5. 新規プロジェクトを最短作成

1. 最小テンプレートを生成

```bash
dotnet run -- --init-project --project=demo-ops --display-name="Demo Ops" --db-type=sqlite --db-path=database/demo-ops.db
```

生成内容（自動）:
- `entities/`（`labelKey` あり）
- `entities.generated/`（`labelKey` なし）
- `dashboard.yml`
- `config/i18n.yml`（英語デフォルトの翻訳キー）
- `pages/StarterOverview.yaml`
- `views/StarterOverview.cshtml`

2. エンティティ YAML を自動生成（DB スキーマから）
  - 既定: `entities.generated/` へ出力（`labelKey` なし）
  - `entities/` へ `labelKey` 付きで出力する場合:

```bash
dotnet run -- --scaffold-entities --project=demo-ops --output-dir=entities --with-label-keys
```

3. 起動後に確認
- `/demo-ops/Dashboard`
- `/demo-ops`（`config/home-page.yml` の内容）
- `/demo-ops/DynamicEntity/Index?entity=<entity名>`

---

## 6. つまずきやすい点

1. `project.yaml` がない  
- `projects/<name>/project.yaml` が必須

2. エンティティが見えない  
- `entities.generated` だけでなく `entities` の上書き有無を確認

3. 起動時に YAML エラー  
- 行番号付きメッセージを確認し、キー名・型・インデントを修正

4. SQL Server で動かない  
- `database.connectionString` の設定漏れを確認

---

## 7. 次に読むべきドキュメント

1. 全体像: `docs/framework-overview-tutorial-ja.md`
2. フック詳細: `docs/project-hooks-guide.md`
3. Dashboard 詳細: `docs/dashboard.md`
4. 生成運用: `docs/entity-scaffold-workflow-ja.md`
