# 更新サマリー（2026-03-05 17:12 JST）

## 概要
第2段階の「可観測性」改善として、Entity Hook 実行時の統一テレメトリを追加しました。  
Hook の `before/after` 実行ごとに、`phase/source/entity/operation/hook/result/durationMs` を記録できるようにしています。

## 変更内容
- 追加: `NetYamlForge/Services/HookExecutionTelemetry.cs`
  - `IHookExecutionTelemetry`
  - `HookExecutionTelemetryEvent`
  - 既定実装 `HookExecutionTelemetryLogger`
- 変更: `NetYamlForge/Services/EntityCrudExecutionService.cs`
  - before/after の project/framework hook 実行時に実行時間を計測
  - `continue/cancel/error/skipped_not_found` をテレメトリ記録
  - 例外発生時もイベント記録後に再送出
- 変更: `NetYamlForge/Program.cs`
  - `IHookExecutionTelemetry -> HookExecutionTelemetryLogger` を DI 登録

## テスト
- 変更: `NetYamlForge.Tests/EntityCrudExecutionServiceTests.cs`
  - telemetry イベント記録の確認テストを追加
- 変更: `NetYamlForge.Tests/DynamicEntityCommandServiceTests.cs`
  - 新依存 (`IHookExecutionTelemetry`) のテストスタブ追加
- 変更: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - 新依存 (`IHookExecutionTelemetry`) のテストスタブ追加

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 99, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
