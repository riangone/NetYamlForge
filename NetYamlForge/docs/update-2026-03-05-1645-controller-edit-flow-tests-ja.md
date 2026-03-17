# 更新サマリー（2026-03-05 16:45 JST）

## 概要
`DynamicEntityController` のテスト補強を継続し、`Edit` アクションの page/modal 分岐を追加で固定しました。  
これにより Create に加えて Edit でも、入力エラー時の応答種別と page 成功時の遷移挙動を回帰防止できます。

## 変更内容
- 変更: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - `Edit`（validation fail, `modal`）で `_Form` を返すことを検証
  - `Edit`（`page` 成功）で `returnUrl` へ redirect することを検証
  - Fake repository に update 呼び出しカウンタを追加

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 87, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
