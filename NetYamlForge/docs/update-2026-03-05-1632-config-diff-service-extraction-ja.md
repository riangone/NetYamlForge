# 更新サマリー（2026-03-05 16:32 JST）

## 概要
第2段階の継続として、`DynamicEntityController` に残っていた JSON 差分計算責務を `DynamicEntityConfigDiffService` へ分離しました。  
これにより、Controller は診断ページのオーケストレーションに集中し、差分アルゴリズムを単体テスト可能なサービスに集約しました。

## 変更内容
- 追加: `NetYamlForge/Services/DynamicEntityConfigDiffService.cs`
  - `BuildJsonDiffLines(EntityDefinition? baseMeta, EntityDefinition? effectiveMeta, bool includeUnchanged)`
  - オブジェクト/配列/値の差分を再帰収集
- 変更: `NetYamlForge/Controllers/DynamicEntityController.cs`
  - `ConfigDiagnostics` から差分計算をサービス呼び出しへ置換
  - `BuildJsonDiffLines` / `CollectJsonDiff` を Controller から削除
- 変更: `NetYamlForge/Program.cs`
  - `DynamicEntityConfigDiffService` を DI (`AddScoped`) 登録

## テスト
- 追加: `NetYamlForge.Tests/DynamicEntityConfigDiffServiceTests.cs`
  - 両者 `null` のときのメッセージ
  - base 欠落時の追加判定
  - `DisplayName` 変更時の差分検出
  - `includeUnchanged=true` での同一値出力

## 検証
- `dotnet build NetYamlForge/NetYamlForge.csproj`
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj`
