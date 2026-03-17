# 更新サマリー（2026-03-05 16:54 JST）

## 概要
`DynamicEntityController` の一覧系挙動テストを拡張し、`ListPartial` の `HX-Push-Url` ヘッダー設定と `count/clear` オプション分岐を固定しました。

## 変更内容
- 変更: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - `ListPartial` 実行時の `HX-Push-Url` ヘッダー設定を検証
  - `Index` で `count=0` 指定時に件数取得をスキップすることを検証
  - `ListPartial` で `clear=1` 指定時に検索語がクリアされることを検証
  - テスト補助:
    - `IUrlHelper` のスタブ追加（`Url.Action` 依存を固定）
    - Fake repository に `GetAll/Count` 呼び出し記録を追加

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 93, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
