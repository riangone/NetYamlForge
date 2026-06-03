---
name: フック生成
icon: 🔗
description: IEntityHook 実装クラス（BeforeAsync / AfterAsync）を生成
needsInput: true
inputPlaceholder: 例: OrderValidation --project=myapp
order: 3
---

カスタムフック（`IEntityHook`）の雛形を生成してください。

## コマンド

```bash
# フッククラスのみ生成
dotnet run -- --scaffold-hook --name=<HookName> --project=<name>

# xUnit テストファイルも同時に生成
dotnet run -- --scaffold-hook --name=<HookName> --project=<name> --with-tests
```

## オプション

| オプション | デフォルト | 説明 |
|---|---|---|
| `--with-tests` | false | `NetYamlForge.Tests/Hooks/<HookName>HookTests.cs` も生成 |

## 生成されるファイル

- `projects/<name>/Hooks/<HookName>Hook.cs`
  - `BeforeAsync()` — DB操作前に実行（戻り値で中断可能）
  - `AfterAsync()` — DB操作後に実行（同一トランザクション内）
- （`--with-tests` 指定時）`NetYamlForge.Tests/Hooks/<HookName>HookTests.cs`
  - 正常系・異常系・フィールド欠落の3テンプレートを含む xUnit テスト

## 注意

- フック名は PascalCase で指定（例: `ValidateInventory` → `ValidateInventoryHook.cs`）
- 生成後は `project.yaml` の `hooks:` セクションにフック名を登録してください
- ビルド後に `dotnet build` でエラーがないか確認してください

フック名とプロジェクト名を指定してください（例: `ValidateOrder --project=todo-app`）:
