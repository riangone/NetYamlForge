# AIサブプロジェクト作成時の注意事項

このドキュメントは、AIがサブプロジェクトを自動生成・削除する際に過去に発生したエラーの原因と対策をまとめたものです。
同じ問題を繰り返さないために、必ず参照すること。

---

## [2026-03-23] プロジェクト削除時にテストファイルが残存してビルドエラー

### 現象

`dotnet build` が以下のようなエラーで失敗する。

```
error CS0234: The type or namespace name 'Redmine' does not exist in the namespace 'NetYamlForge.Projects'
error CS0234: The type or namespace name 'Inventory' does not exist in the namespace 'NetYamlForge.Projects'
error CS0246: The type or namespace name 'SalesforceCrmPageMutationValidator' could not be found
```

### 原因

`projects/<name>/` 配下のサブプロジェクトを削除した際に、対応するテストファイルを削除し忘れた。

削除されたプロジェクト: `Redmine`, `Inventory`, `SalesforceCrm`

残存していたテストファイル:
- `NetYamlForge.Tests/Hooks/SetIssueClosedOnHookTests.cs` → `Redmine` プロジェクトのフック参照
- `NetYamlForge.Tests/Hooks/ValidateIssueStatusTransitionHookTests.cs` → `Redmine` プロジェクトのフック参照
- `NetYamlForge.Tests/Hooks/UpdateProductStockHookTests.cs` → `Inventory` プロジェクトのフック参照
- `NetYamlForge.Tests/Hooks/ValidateStockOutHookTests.cs` → `Inventory` プロジェクトのフック参照
- `NetYamlForge.Tests/PageRowMutationServiceTests.cs` → `SalesforceCrm` プロジェクトのバリデータ参照

### 対策

**サブプロジェクトを削除するときは、必ず以下をセットで削除する：**

1. `projects/<name>/` ディレクトリ全体
2. `NetYamlForge/projects/<name>/Hooks/` 配下の C# フッククラス
3. `NetYamlForge.Tests/Hooks/` 配下の当該プロジェクト専用テストファイル
4. `NetYamlForge.Tests/` 直下でそのプロジェクトに依存するテストファイル

削除後は必ず `dotnet build` を実行してビルドが通ることを確認すること。

### チェックリスト（削除時）

```bash
# 削除したプロジェクト名に関連するテストファイルを検索
grep -rl "NetYamlForge.Projects.<ProjectName>" NetYamlForge.Tests/

# ビルド確認
dotnet build
```

---

## [2026-03-23] DBスキーマとYAML定義の不整合による起動失敗

### 現象

`init_seed.sql` でDBを初期化後、アプリを起動すると以下のエラーで起動に失敗する。

```
[ERR] プロジェクト読み込みエラー：.../projects/todo-app
System.InvalidOperationException: プロジェクト 'todo-app' の Entity YAML とDBスキーマが不整合です。
- entities.comment.columns.EntityType.required を true にしてください。DB列 Comment.EntityType は NOT NULL かつ既定値なしです。
- entities.comment.columns.EntityId.required を true にしてください。DB列 Comment.EntityId は NOT NULL かつ既定値なしです。
- entities.comment.columns.Author.required を true にしてください。DB列 Comment.Author は NOT NULL かつ既定値なしです。
- entities.comment.columns.Body.required を true にしてください。DB列 Comment.Body は NOT NULL かつ既定値なしです。
```

### 原因

エンティティ YAML の `columns` セクションで、DB 上 `NOT NULL` かつデフォルト値なしの列に `required: true` が設定されていなかった。

`forms` セクションには `required: true` を書いていたが、**`columns` セクションにも同じ指定が必要**なことを見落としていた。

`EntityDbSchemaConsistencyValidator` が起動時にこの不整合を検出し、例外をスローしてアプリが起動できなくなる。

### 対策

**DB列が `NOT NULL` かつデフォルト値なしの場合、YAMLの両セクションに `required: true` を設定する：**

```yaml
columns:
  ColumnName:
    type: string
    required: true   # ← NOT NULL・デフォルト値なし列は必須
    label: ...

forms:
  ColumnName:
    type: string
    required: true   # ← editable: true の場合も必須
    editable: true
```

#### 該当するDBパターン

| DBカラム定義 | `columns.required` 必要？ | `forms.required` 必要？ |
|---|---|---|
| `NOT NULL` かつデフォルト値なし | **必須** | editable: true なら**必須** |
| `NOT NULL` かつ `DEFAULT '...'` あり | 不要 | 不要 |
| `NULL` 許容 | 不要 | 不要 |
| `PRIMARY KEY` / `AUTOINCREMENT` | 不要 | 不要 |

### チェックリスト（エンティティ作成時）

```bash
# 各テーブルのNOT NULL列を確認（SQLite）
sqlite3 projects/<name>/database/<name>.db "PRAGMA table_info(<TableName>);"
# notnull=1 かつ dflt_value=NULL かつ pk=0 の列をすべて YAML に required: true で定義すること
```

新しいエンティティ YAML を作成したら、必ずアプリを起動してエラーがないことを確認する。

---

## 今後の注意事項追記欄

（新たな問題が発生した場合、このセクションに追記すること）
