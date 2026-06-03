---
name: ビルド確認
icon: 🔨
description: dotnet build を実行してエラーを確認・修正
needsInput: false
order: 6
---

`dotnet build` を実行してビルドエラーがないか確認してください。

エラーがあれば原因を特定して修正し、再度ビルドが成功するまで繰り返してください。

## よくあるエラーパターン

- **CS0234 / CS0246** — サブプロジェクト削除後に `Hooks/` やテストファイルが残っている
- **DCS001** — SQL 文字列補間（パラメーター化クエリに修正が必要）
- **DCS002** — `.Result` / `.Wait()` などブロッキング呼び出し（`await` に修正）
- **DCS003** — `IDbConnection` を直接 `new` している（DI 経由に修正）
- **Startup validation** — `NOT NULL` 列に `columns.required: true` が不足
