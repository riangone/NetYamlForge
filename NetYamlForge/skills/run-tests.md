---
name: テスト実行
icon: 🧪
description: dotnet test を実行して結果を確認
needsInput: false
order: 7
---

`dotnet test` を実行して全テストの結果を確認してください。

失敗したテストがあれば：
1. エラーメッセージと失敗した箇所を特定する
2. 原因を分析して修正案を提示する

## テストの種類（参考）

| テストファイル | カバー範囲 |
|---|---|
| `DynamicEntityControllerTests.cs` | コントローラー・リクエストパイプライン |
| `EntityCrudExecutionServiceTests.cs` | フック実行・トランザクション |
| `YamlSchemaValidationTests.cs` | YAML 設定ファイルの構文検証 |
| `SqlGenerationSnapshotTests.cs` | SQL 生成のスナップショット回帰テスト |
| `YamlConfigStartupValidatorTests.cs` | 起動時型バリデーション |

## 特定テストのみ実行

```bash
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
```
