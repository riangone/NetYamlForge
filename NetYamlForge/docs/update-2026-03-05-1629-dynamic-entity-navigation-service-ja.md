# 更新サマリー（2026-03-05 16:29 JST）

## 概要
第2段階の継続として、`DynamicEntityController` に残っていた遷移ナビゲーション責務（`returnUrl` 解析、パンくず生成）を `DynamicEntityNavigationService` に分離しました。  
目的は、Controller の肥大化を抑え、遷移ロジックの回帰をユニットテストで固定できるようにすることです。

## 変更内容
- 追加: `NetYamlForge/Services/DynamicEntityNavigationService.cs`
  - `BuildBreadcrumbChain(string? returnUrl, int maxDepth = 8)`
  - `ExtractEntityFromReturnUrl(string? returnUrl)`
- 変更: `NetYamlForge/Controllers/DynamicEntityController.cs`
  - `CreatePage` / `EditPage` でサービス呼び出しに置換
  - 一覧 ViewModel 組み立て時の return entity 解決をサービス呼び出しに置換
  - Controller 内の重複 private メソッド（パンくず/returnUrl解析）を削除
- 変更: `NetYamlForge/Program.cs`
  - `DynamicEntityNavigationService` を DI (`AddScoped`) 登録

## テスト
- 追加: `NetYamlForge.Tests/DynamicEntityNavigationServiceTests.cs`
  - entity 抽出（正常/欠落）
  - パンくず順序（古い遷移 -> 新しい遷移）
  - 中間ノード異常時の打ち切り
  - `maxDepth` 制限

## 検証結果
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 72, Failed: 0`

## 次ステップ（第2段階 継続）
- `DynamicEntityController` の JSON diff/診断ロジック（`BuildJsonDiffLines` / `CollectJsonDiff`）を専用サービスへ分離し、表示層と差分計算ロジックの責務を分離する。
