# NetYamlForge アーキテクチャマップ（日本語）

## 目的
本ドキュメントは、実装責務を「入口（Controller）」「業務（Service）」「設定（YAML/Provider）」で俯瞰するための入口です。

## レイヤ構成
1. HTTP/画面レイヤ
- `Controllers/`
- `Views/`

2. 業務ロジックレイヤ
- `Services/`
- 代表: `DynamicEntity*Service`, `Page*Service`, `EntityCrudExecutionService`

3. 設定・メタデータレイヤ
- `config/`
- `projects/<project>/`
- `IEntityMetadataProvider`, `IDashboardConfigProvider`, `IPageMetadataProvider`

4. 永続化・方言レイヤ
- `IDynamicCrudRepository` / `DynamicCrudRepository`
- `Services/Dialect/`

## 主要フロー
1. 一覧取得
- `DynamicEntityController.Index/ListPartial`
- `DynamicEntityListQueryService`
- `IDynamicCrudRepository.GetAllAsync/CountAsync`

2. 作成・更新・削除
- `DynamicEntityController.Create/Edit/Delete`
- `DynamicEntityCommandService`
- `EntityCrudExecutionService`（hook + tx + audit）

3. 設定診断
- `DynamicEntityController.ConfigDiagnostics`
- `DynamicEntityConfigDiagnosticsService`
- `DynamicEntityConfigDiffService`

## 関連ドキュメント
1. 全体チュートリアル
- `framework-overview-tutorial-ja.md`

2. 実装サマリー（詳細）
- `implementation-summary-ja.md`

3. フック詳細
- `project-hooks-guide.md`
- `COMMON_HOOKS.md`
