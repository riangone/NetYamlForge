# 更新サマリー（2026-03-05 16:51 JST）

## 概要
Controller テスト補強を継続し、`Delete` 成功分岐と `ConfigDiagnostics` アクションの戻りモデル検証を追加しました。

## 変更内容
- 変更: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - `Delete` 成功時に `_List` partial が返ることを検証
  - `ConfigDiagnostics` で未存在 entity 指定時に利用可能 entity へフォールバックすることを検証
  - `ConfigDiagnosticsViewModel` の `ProjectName/Entity/Entities` を検証

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 90, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
