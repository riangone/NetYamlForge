# 更新サマリー（2026-03-05 16:55 JST）

## 概要
`DynamicEntityController` の `Create/Edit` における modal 成功時の応答をテスト補強し、一覧 partial 返却と HTMX ヘッダー設定を固定しました。

## 変更内容
- 変更: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - `Create`（modal 成功）で `_List` 応答 + `HX-Retarget/HX-Trigger` を検証
  - `Edit`（modal 成功）で `_List` 応答 + `HX-Retarget/HX-Trigger` を検証
  - 成功時に count 再取得が呼ばれることも検証

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 95, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
