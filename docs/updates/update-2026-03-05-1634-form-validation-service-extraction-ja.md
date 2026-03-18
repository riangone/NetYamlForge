# 更新サマリー（2026-03-05 16:34 JST）

## 概要
第2段階の継続として、`DynamicEntityController` に残っていたフォーム値変換・検証処理を `DynamicEntityFormValidationService` に分離しました。  
狙いは、入力変換ロジック（型変換、bool デフォルト、エラー収集）を Controller から切り離し、テスト容易性と再利用性を上げることです。

## 変更内容
- 追加: `NetYamlForge/Services/DynamicEntityFormValidationService.cs`
  - `ConvertAndValidate(EntityDefinition meta, Dictionary<string, string?> form)`
- 変更: `NetYamlForge/Controllers/DynamicEntityController.cs`
  - `Create` / `Edit` で検証処理をサービス呼び出しに置換
  - Controller 内の `ConvertAndValidate` private メソッドを削除
  - `IValueConverter` 直接依存を除去
- 変更: `NetYamlForge/Program.cs`
  - `DynamicEntityFormValidationService` を DI (`AddScoped`) 登録

## テスト
- 追加: `NetYamlForge.Tests/DynamicEntityFormValidationServiceTests.cs`
  - Identity 列スキップ
  - bool 欠落時の `false` 補完
  - 数値変換失敗時のエラー収集

## 検証
- `dotnet build NetYamlForge/NetYamlForge.csproj`
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj`
