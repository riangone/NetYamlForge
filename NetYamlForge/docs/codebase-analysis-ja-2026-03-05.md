# NetYamlForge コードベース解析（2026-03-05）

## 1. 解析対象と結論
- 対象: `NetYamlForge/` 一式（Controllers/Services/Models/Middleware/Data/Localization/config/projects）
- 解析日時: 2026-03-05
- ビルド検証: `dotnet build` 成功（0 warning / 0 error）

結論として、このアプリは「YAML駆動の動的CRUD + マルチプロジェクト実行基盤」を中核とした ASP.NET Core MVC システムで、以下 3 層の設計が明確です。
- プロジェクト解決層: `ProjectManager` + `ProjectMiddleware` + `ProjectScope`
- メタデータ駆動層: Entity/Page/Dashboard の YAML 読み込み・検証・正規化
- 実行層: Controller + Repository + Hook/Permission/Audit + DB 初期化

## 2. 主要構成（静的マップ）
主要 C# ファイル数（解析時点）:
- `Controllers`: 7
- `Services`: 44
- `Models`: 9
- `Middleware`: 2
- `Data`: 1
- `Localization`: 3
- 合計: 66

プロジェクト定義（`projects/*/project.yaml`）:
- 16 プロジェクト
- プロジェクトごとの `Hooks/*.cs` は 4 プロジェクトで利用（計 6 ファイル）

## 3. 起動とDIの実装要点
`Program.cs` が以下を集約します。
- CLI モード:
  - `--scaffold-entities`
  - `--upgrade-entity-yaml`
  - `--init-project`
- Web モード:
  - Serilog
  - Cookie 認証 + `FallbackPolicy`（認証必須）
  - i18n（`en-US`, `zh-CN`, `ja-JP`, `ko-KR`）
  - マルチプロジェクト向け DI（`ProjectScope` 経由）
  - DB 方言/接続切替（sqlite/sqlserver/postgres/mysql）
  - Hook レジストリ + CRM バックグラウンドサービス

ルーティング:
- `/{project}` → `Home.Project`
- `/{project}/{controller=Dashboard}/{action=Index}/{id?}`
- `/{controller=Home}/{action=Index}/{id?}`

## 4. マルチプロジェクト実行モデル
### 4.1 `ProjectManager`（起動時ロード）
- `projects/*/project.yaml` を全スキャン
- `YamlSchemaValidator` で schema 検証
- `layout.yml` 読み込み（厳格デシリアライズ）
- プロジェクトごとに以下を構築:
  - `EntityMetadataProvider`
  - `DashboardConfigProvider`
  - `PageMetadataProvider`
  - `ProjectInfo`
- `EntityDbSchemaConsistencyValidator` で YAML と実 DB 不整合を起動時ブロック
- プロジェクト固有 Hook / Business Logic の動的ロードを実施

### 4.2 `ProjectMiddleware` + `ProjectScope`
- ルートの `{project}` からプロジェクトを特定
- ルートに無い場合は `ReturnUrl` 先頭セグメントから推定
- 最終フォールバックとして先頭プロジェクトを採用
- `ProjectScope.Current` を request-scope で供給

## 5. メタデータ駆動の中心機能
### 5.1 Entity メタデータ
- `EntityMetadataProvider` が `entities.generated` → `entities-{provider}` → `entities` の優先順でマージ
- `imports` 対応
- 単一ファイル `entities.yml` フォールバックあり
- 正規化対象:
  - `Columns / Forms / Filters / Links / Layout / Keys`
- 複合主鍵を `Keys` でサポート（`Key` と共存）

### 5.2 Page / Dashboard メタデータ
- `PageMetadataProvider`: `pages/*.yaml`
- `DashboardConfigProvider`: `dashboard.yml`
- どちらも schema 検証で失敗時に起動を止める設計

### 5.3 i18n
- `I18nText.Resolve`:
  - YAML キー翻訳 (`config/i18n.yml`, `projects/*/config/i18n.yml`)
  - `.resx` リソース
  - 言語フォールバック（exact → neutral → en-US → en）

## 6. リクエスト処理フロー
### 6.1 DynamicEntity（汎用CRUD）
`DynamicEntityController` + `DynamicCrudRepository`
- 一覧:
  - numbered/keyset ページング両対応
  - count 省略モードあり
  - YAML filters/search/sort を SQL 化
- 追加/更新/削除:
  - 複合主鍵（JSON / query）対応
  - Hook 前後処理
  - 監査ログ
  - トランザクション実行

### 6.2 Dynamic SQL の安全対策
`DynamicCrudRepository.ValidateMetadata` で以下を検証:
- 識別子ホワイトリスト（正規表現）
- 危険トークン拒否（`;`, `--`, `/*`, `*/`）
- join type 制約
- `fk.query` は `SELECT` 始まり + `Id` 列必須

### 6.3 Page（ページDSL）
`PageController`:
- `/{project}/Page/{pageName}`
- セクション単位でデータ読み込み
- 行更新/削除 API
- `AppUserSavedView` による保存ビュー
- `IPagePermissionService` による page/field 権限制御
- 管理系ページ/項目の追加制約あり

### 6.4 Dashboard
`DashboardController`:
- stat/card/charts を YAML から生成
- SQL 失敗時は該当カード/グラフをスキップして継続

## 7. 認証・監査・運用
- 認証: Cookie + `AppUser`（`PasswordHasher`）
- 認可: Admin ロール + RBAC テーブル（`AppUserRole`, `AppRolePermission`）
- 監査: `AuditLogService` で操作記録
- 追跡: `RequestTraceMiddleware` が `X-Trace-Id` を統一
- DB 初期化: `DbInitializer`
  - 各 project の DB を自動初期化
  - SQLite では auth/RBAC/CRM テーブル準備と seed
  - DB 未存在時、初回 Chinook ダウンロード経路あり

## 8. 動的フック基盤
`ProjectHookLoader`:
- `projects/*/Hooks/*.cs` を Roslyn で実行時コンパイル
- `IEntityHook`, `IProjectBusinessLogic`, `IProjectValidator`, `IProjectDataTransformer` を検出登録
- コンパイルエラーを診断ID付きで記録

設計上の利点:
- プロジェクト単位の拡張が本体改修なしで可能
- Hook の責務を Entity 操作フローに統合しやすい

## 9. 強みとリスク
### 強み
- YAML + schema 検証で設定駆動開発の再現性が高い
- マルチ DB 方言と project 分離が実装されている
- 複合主鍵、keyset ページング、保存ビュー、RBAC まで対応済み
- 起動時に DB/設定不整合を早期検知するガードが多い

### 技術的リスク
- Controller（特に `DynamicEntityController`, `PageController`）が肥大化
- SQL 文字列組み立てが広範で、仕様変更時の回帰面積が大きい
- Hook 動的コンパイルは強力だが、起動時失敗や依存解決失敗の運用負荷がある
- `DbInitializer` が巨大で、プロジェクト固有ロジック（CRM seed）が集中している

## 10. 優先改善提案（実装順）
1. Controller 分割
   - `DynamicEntityController`: Query/Mutation/Hook/Audit を Application Service へ分離
   - `PageController`: 読み込み・保存ビュー・行更新をサービス化
2. SQL ビルダ共通化
   - where/sort/paging/build-from の共通クラス化
   - controller 側 SQL 直書きを段階的に除去
3. Hook 運用強化
   - Hook の診断ページ（ロード済み/失敗詳細/依存）を追加
4. 初期化責務分割
   - `DbInitializer` を DB 種別・ドメイン別（auth/rbac/crm）に分割
5. 最低限の自動テスト追加
   - `ProjectManager` 読み込み失敗ケース
   - `DynamicCrudRepository.ValidateMetadata`
   - `PagePermissionService.EvaluatePermission`

## 11. 参照した代表ファイル
- `Program.cs`
- `Middleware/ProjectMiddleware.cs`
- `Services/ProjectManager.cs`
- `Services/EntityMetadataProvider.cs`
- `Services/DynamicCrudRepository.cs`
- `Services/PageMetadataProvider.cs`
- `Controllers/DynamicEntityController.cs`
- `Controllers/PageController.cs`
- `Controllers/DashboardController.cs`
- `Data/DbInitializer.cs`
- `Services/ProjectHookLoader.cs`
- `Services/YamlSchemaValidator.cs`

