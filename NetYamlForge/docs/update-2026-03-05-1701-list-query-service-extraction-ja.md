# 更新サマリー（2026-03-05 17:01 JST）

## 概要
`DynamicEntityController` の `Index/ListPartial` に重複していた一覧取得ロジックを `DynamicEntityListQueryService` へ抽出しました。  
あわせてサービス単体テストを追加し、`count/clear` 分岐と FK 読み込み経路を固定しました。

## 変更内容
- 追加: `NetYamlForge/Services/DynamicEntityListQueryService.cs`
  - 一覧取得、`count/clear` 反映、Paging 判定、FK データ取得を集約
  - 戻り値 `DynamicEntityListQueryResult` を追加
- 変更: `NetYamlForge/Controllers/DynamicEntityController.cs`
  - `Index/ListPartial` で新サービス呼び出しへ置換
  - Controller 内の重複クエリ組み立て処理を削減
- 変更: `NetYamlForge/Program.cs`
  - `DynamicEntityListQueryService` を DI 登録

## テスト
- 追加: `NetYamlForge.Tests/DynamicEntityListQueryServiceTests.cs`
  - `count=0` で件数取得スキップ
  - `clear=1` で検索語クリア
  - `foreignKeysForForm` フラグで form/filter FK 読み込み先を切替
- 変更: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - コンストラクタ依存更新（`DynamicEntityListQueryService` 注入）

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 98, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
