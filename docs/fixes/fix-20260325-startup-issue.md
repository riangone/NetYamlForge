# 起動障害修復レポート (2026-03-25)

## 概要

NetYamlForge の起動時に todo-app プロジェクトのフックコンパイルエラーが発生する問題を修正しました。

## 問題

```
[ERR] [HOOK_COMPILE_DIAGNOSTICS] プロジェクト 'todo-app' のフックコンパイルエラー:
TaskActionHandlers.cs(162,27) CS0103: The name 'File' does not exist in the current context
TaskActionHandlers.cs(176,26) CS0246: The type or namespace name 'List<>' could not be found
TaskActionHandlers.cs(211,13) CS0019: Operator '>' cannot be applied to operands of type 'method group' and 'int'
TaskActionHandlers.cs(225,26) CS0246: The type or namespace name 'List<>' could not be found
[ERR] [HOOK_COMPILE_FAILED] プロジェクト 'todo-app' のフックコンパイルに失敗しました
```

## 原因

`NetYamlForge/projects/todo-app/Hooks/TaskActionHandlers.cs` ファイルに必要な `using` 宣言が不足していました:

- `using System.Collections.Generic;` - `List<>` 型に必要
- `using System.IO;` - `File` クラスに必要

## 修正

ファイルの先頭に以下の using 宣言を追加:

```csharp
using System;
using System.Collections.Generic;  // ← 追加
using System.Data;
using System.IO;                    // ← 追加
using System.Threading.Tasks;
using Dapper;
using NetYamlForge.Services.Hooks;
```

## 影響範囲

- ファイル: `NetYamlForge/projects/todo-app/Hooks/TaskActionHandlers.cs`
- クラス: `ImportTasksCsvHandler`
- メソッド: `ExecuteAsync`, `ParseCsvLine`

## 検証

修正後、以下のコマンドでビルドと起動を確認:

```bash
dotnet build NetYamlForge/NetYamlForge.csproj
# Build succeeded. 0 Warning(s) 0 Error(s)

dotnet run --project NetYamlForge
# [INF] Now listening on: http://localhost:5000
# [INF] Application started. Press Ctrl+C to shut down.
```

## YAML 検証警告

起動時に 9 件の YAML 設定警告が確認されています:

### unknown_filter_type (9 件)

`toggle` 型が `KnownFilterTypes` に登録されていません。

| プロジェクト | エンティティ | フィルター名 | 型 |
|-------------|-------------|-------------|-----|
| framework-showcase | batch_job_demo | IsEnabled | toggle |
| framework-showcase | export_demo | IsActive | toggle |
| framework-showcase | export_demo | Price | gte |
| framework-showcase | filter_demo | ViewCount | gte |
| framework-showcase | filter_demo | Rating | gte |
| framework-showcase | form_component | BoolToggle | toggle |
| framework-showcase | hook_demo | IsArchived | toggle |
| framework-showcase | layout_demo | IsPublic | toggle |
| biz-docs | pdf_template_category | IsEnabled | toggle |

**対応方針**: 現時点では警告のまま運用。必要に応じて以下を実施:
1. `KnownFilterTypes` に `toggle` を追加
2. または YAML 側を `bool-toggle` に変更

## コミット情報

- コミットハッシュ: `250ca84`
- メッセージ: `fix: add missing using statements in TaskActionHandlers.cs`
- 日時: 2026-03-25 09:20:55 +0900

## 参考

- 関連ファイル: `NetYamlForge/Services/Validation/YamlConfigStartupValidator.cs`
- 既知のフィルター型: `KnownFilterTypes` (YamlConfigStartupValidator.cs:45)
