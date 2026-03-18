# 更新サマリー（2026-03-05 17:20 JST）

## 概要
第2段階の「並行更新ガード」改善として、`Update/Delete` で受影響行数が 0 の場合を競合として扱う保護を追加しました。  
これにより、他ユーザー更新や既削除データへの操作時に成功扱いされる問題を防止します。

## 変更内容
- 変更: `NetYamlForge/Services/DynamicEntityCommandService.cs`
  - `Update/Delete` 実行後の affected rows を検証
  - 0 件の場合は `concurrency_conflict_or_not_found` を返却
  - 競合時メッセージ:
    - `対象データが更新済みか、既に削除されています。`
- 変更: `NetYamlForge/Controllers/DynamicEntityController.cs`
  - `Delete` で上記エラーコード時は `409 Conflict` を返却
- 変更: `NetYamlForge.Tests/DynamicEntityCommandServiceTests.cs`
  - `Update/Delete` 0件更新時の競合結果を検証
- 変更: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - `Delete` 0件更新時の `ConflictObjectResult` を検証

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 102, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
