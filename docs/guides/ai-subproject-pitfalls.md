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

## 今後の注意事項追記欄

（新たな問題が発生した場合、このセクションに追記すること）
