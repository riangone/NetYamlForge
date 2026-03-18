# リリース準備チェック（2026-03-03）

## 1. 対象

- 期間: Wave 1 / Wave 2 の改善実装
- 主な対象: DynamicEntity 一覧/フィルタ、Hook ロード、YAML 検証、診断ページ、テスト基盤

## 2. 実施結果

1. Build
- コマンド: `dotnet build`
- 結果: 成功（警告: `NU1900` のみ）

2. Test
- コマンド: `dotnet test /home/ubuntu/ws/ccc/NetYamlForge.Tests/NetYamlForge.Tests.csproj`
- 結果: 成功（Passed: 8）

3. 文書整備
- 改善計画/品質ゲート/回帰手順/文書更新ガイドを更新済み

## 3. 手動スモーク（実施: 2026-03-03）

実行コマンド（抜粋）:
- `dotnet run --no-build --urls http://127.0.0.1:5270`
- `curl -i http://127.0.0.1:5270/<project>/DynamicEntity/...`

結果:
1. `GET /chinook/DynamicEntity/Index?entity=customer`
- Status: `302 Found`（Login redirect）
- 判定: Pass（ルート有効・認証ガード正常）

2. `GET /blog/DynamicEntity/Index?entity=post`
- Status: `302 Found`（Login redirect）
- 判定: Pass（ルート有効・認証ガード正常）

3. `GET /northwind-sqlite3-ops/DynamicEntity/Index?entity=order`
- Status: `302 Found`（Login redirect）
- 判定: Pass（ルート有効・認証ガード正常）

4. `GET /northwind-sqlite3-ops/DynamicEntity/ConfigDiagnostics?entity=order`
- Status: `302 Found`（Login redirect）
- 判定: Pass（Admin 認証ガード正常）

## 4. リスクメモ

1. ローカル開発では `NuGetAudit` を無効化して `NU1900` を抑止済み。CI/本番では監査継続のため、ネットワーク到達性を定期確認する。
2. `NetYamlForge/projects/chinook/database/chinook.db` にローカル差分が残っているため、リリース対象に混入しないことを確認する。

## 5. 判定

- コード品質ゲート（build/test/docs）: Pass
- 手動スモーク（ルート到達/認証ガード）: Pass
- リリース判定: Pass（認証後の業務操作テストは別途継続）
