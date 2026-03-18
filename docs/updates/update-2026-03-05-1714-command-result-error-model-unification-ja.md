# 更新サマリー（2026-03-05 17:14 JST）

## 概要
第2段階の「エラーモデル統一」改善として、`DynamicEntityCommandService` の戻り値を tuple から統一 `CommandResult` へ置換しました。  
これにより、失敗時に `ErrorCode + Message` を一貫して扱えるようになり、Controller 側の分岐も明確になりました。

## 変更内容
- 追加: `NetYamlForge/Services/CommandResult.cs`
  - `CommandError`
  - `CommandResult`
  - `CommandResult<T>`
- 変更: `NetYamlForge/Services/DynamicEntityCommandService.cs`
  - `CreateAsync`: `CommandResult<int>` を返すよう変更
  - `UpdateAsync/DeleteAsync`: `CommandResult` を返すよう変更
  - hook reject 時にエラーコードを付与
    - `hook_rejected_before_create`
    - `hook_rejected_before_update`
    - `hook_rejected_before_delete`
- 変更: `NetYamlForge/Controllers/DynamicEntityController.cs`
  - `Ok` / `Error?.Message` 参照へ置換
- 変更: `NetYamlForge.Tests/DynamicEntityCommandServiceTests.cs`
  - 新戻り値モデルに合わせてアサーション更新

## 検証
- `dotnet test NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功
  - `Passed: 99, Failed: 0`
- `dotnet build NetYamlForge/NetYamlForge.csproj` 成功
