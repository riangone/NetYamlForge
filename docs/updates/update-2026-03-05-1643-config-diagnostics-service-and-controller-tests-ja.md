# 更新サマリー（2026-03-05 16:43 JST）

## 概要
第2段階の継続として、`DynamicEntityController` の `ConfigDiagnostics` 残責務を専用サービスへ分離し、あわせて `Create` アクションの controller レイヤー挙動テストを追加しました。  
これにより Controller の編成責務をさらに限定し、画面応答の回帰をテストで固定しました。

## 変更内容
- 追加: `NetYamlForge/Services/BaseEntityMetadataProvider.cs`
  - `IBaseEntityMetadataProvider` 抽象を定義
  - 既定実装 `BaseEntityMetadataProvider` で base metadata 読み込みを提供
- 追加: `NetYamlForge/Services/DynamicEntityConfigDiagnosticsService.cs`
  - entity 選択、base/effective JSON 生成、diff 計算を集約
- 変更: `NetYamlForge/Controllers/DynamicEntityController.cs`
  - `ConfigDiagnostics` を `DynamicEntityConfigDiagnosticsService` 呼び出しに置換
  - base metadata 読み込み/JSON 生成ロジックを Controller から削除
- 変更: `NetYamlForge/Program.cs`
  - `IBaseEntityMetadataProvider` / `DynamicEntityConfigDiagnosticsService` を DI 登録

## テスト
- 追加: `NetYamlForge.Tests/DynamicEntityConfigDiagnosticsServiceTests.cs`
  - 未存在 entity 指定時のフォールバック選択
  - metadata 欠落時 JSON (`{}`) の確認
  - base/effective 差分検出
- 追加: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - `Create`（validation fail, `modal`）で `_Form` を返すこと
  - `Create`（validation fail, `page`）で `FormPage` を返すこと
  - `Create`（`page` 成功）で `returnUrl` へ redirect すること

## 検証
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 85, Failed: 0`
