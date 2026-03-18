# 更新サマリー（2026-03-05 17:23 JST）

## 概要
エラーモデル統一の第2段階として、エラーコード定義を集中化し、HTTP 応答判定を `CommandErrorHttpMapper` に分離しました。  
目的は、Controller に散在する文字列比較を削減し、エラーコード運用を一元化することです。

## 変更内容
- 追加: `NetYamlForge/Services/CommandErrorCodes.cs`
  - command error code の定数を集約
- 追加: `NetYamlForge/Services/CommandErrorHttpMapper.cs`
  - `CommandError` から HTTP 応答分類（Conflict 判定）を提供
- 変更: `NetYamlForge/Services/DynamicEntityCommandService.cs`
  - 直接文字列を `CommandErrorCodes` に置換
- 変更: `NetYamlForge/Controllers/DynamicEntityController.cs`
  - delete 競合判定を mapper 呼び出しに置換
- 変更: `NetYamlForge/Program.cs`
  - `CommandErrorHttpMapper` を DI 登録

## テスト
- 追加: `NetYamlForge.Tests/CommandErrorHttpMapperTests.cs`
  - concurrency code で conflict 判定
  - 非 conflict code で false 判定
- 変更: `NetYamlForge.Tests/DynamicEntityCommandServiceTests.cs`
  - code アサーションを定数化
- 変更: `NetYamlForge.Tests/DynamicEntityControllerTests.cs`
  - 新依存 mapper を注入

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 104, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
