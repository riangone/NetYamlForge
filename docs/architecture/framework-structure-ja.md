# NetYamlForge フレームワーク全体構造説明書

> 対象読者: 初めてこのフレームワークに触れる開発者・設計者
> 更新日: 2026-03-11

---

## 1. フレームワークの目的と特性

NetYamlForge は **「YAML を書くだけで業務 CRUD アプリが動く」** ASP.NET Core MVC フレームワークです。

### 解決する課題

| 従来の開発 | このフレームワーク |
|-----------|----------------|
| テーブル追加 → Controller・Service・Model・View を書く | YAML に定義するだけ（コード変更なし） |
| バリデーション変更 → コードを探して修正 | YAML の hooks セクションを変更 |
| 複数システムで共通コードが分散 | 1バイナリで複数プロジェクトを管理 |
| フォームとSQLが乖離しやすい | YAML が唯一の真実の源、自動整合 |

### 適用範囲

✅ 向いている用途:
- 社内管理ツール・マスタメンテナンス画面
- データエントリー・レポート閲覧
- プロトタイプ・PoC の高速開発

❌ 向いていない用途:
- 高度にカスタムされた UI（特殊なインタラクション）
- リアルタイム処理・WebSocket

---

## 2. ディレクトリ構造（全体）

```
NetYamlForge/
│
├── Program.cs                           # エントリポイント・DI設定・CLIコマンド
├── appsettings.json                     # 環境設定（DB接続・ログレベル等）
│
├── Controllers/                         # HTTP リクエスト処理層
│   ├── DynamicEntityController.cs       # ★ CRUD UI（一覧・フォーム・削除）
│   ├── DashboardController.cs           # ダッシュボード（集計カード・チャート）
│   ├── ApiEntityController.cs           # REST API
│   ├── PageController.cs                # カスタムページ
│   ├── AccountController.cs             # 認証（ログイン・ログアウト・登録）
│   ├── UsersController.cs               # ユーザー管理（Admin専用）
│   ├── LocalizationController.cs        # 言語切り替え
│   └── HomeController.cs                # ホームページ
│
├── Services/                            # ビジネスロジック・インフラ層
│   ├── DynamicCrudRepository.cs         # ★ SQL組立・実行エンジン
│   ├── EntityMetadataProvider.cs        # ★ YAML→EntityDefinition ローダー
│   ├── EntityCrudExecutionService.cs    # ★ フック実行・トランザクション管理
│   ├── DynamicEntityCommandService.cs   # Create/Update/Delete コマンドファサード
│   ├── DynamicEntityListQueryService.cs # 一覧データ取得
│   ├── DynamicEntityForeignKeyDataService.cs # FK選択肢データ取得
│   ├── DynamicEntityFormValidationService.cs # フォーム値変換・検証
│   ├── DynamicEntityKeyResolverService.cs    # URL→主キー値解決
│   ├── DynamicEntityFormViewModelFactory.cs  # フォームViewModelファクトリ
│   ├── DynamicEntityListResponseService.cs   # 変更後一覧再取得
│   ├── DynamicEntityListHttpResponseService.cs # HTMXヘッダー管理
│   ├── DynamicEntityNavigationService.cs     # パンくずリスト生成
│   ├── DynamicEntityConfigDiagnosticsService.cs # 設定診断
│   ├── CommandResult.cs                 # コマンド結果型定義
│   ├── CommandErrorCodes.cs             # エラーコード定数
│   ├── CommandErrorHttpMapper.cs        # エラーコード→HTTPステータス変換
│   ├── HookExecutionTelemetry.cs        # フック実行テレメトリ
│   ├── ProjectManager.cs                # ★ プロジェクト検出・管理（Singleton）
│   ├── ProjectScope.cs                  # リクエストスコープのプロジェクト情報
│   ├── BaseEntityMetadataProvider.cs    # グローバルエンティティ定義取得
│   ├── ProjectAwareEntityMetadataProvider.cs # プロジェクト別メタデータ
│   ├── DashboardConfigProvider.cs       # ダッシュボード設定ロード
│   ├── PageMetadataProvider.cs          # カスタムページ設定ロード
│   ├── HomePageConfigProvider.cs        # ホームページ設定ロード
│   ├── SqlSafetyGuard.cs               # SQL識別子の安全検証ユーティリティ
│   ├── ValueConverter.cs                # フォーム値の型変換
│   ├── FilterValueParser.cs             # URLクエリ→フィルター値変換
│   ├── ListQueryOptionResolver.cs       # ページングオプション解決
│   ├── ListStateUrlBuilder.cs           # 一覧状態URLの組み立て
│   ├── PagingResultBuilder.cs           # ページング結果構築
│   ├── YamlSchemaValidator.cs           # YAMLスキーマ検証
│   ├── EntityDbSchemaConsistencyValidator.cs # DB整合性チェック
│   ├── EntityYamlScaffolder.cs          # DB→YAML自動生成
│   ├── HookScaffolder.cs                # フック雛形生成CLI
│   ├── ProjectTemplateScaffolder.cs     # プロジェクト初期化CLI
│   ├── EntityYamlModernizer.cs          # YAML形式移行ユーティリティ
│   ├── CrmAutomationHostedService.cs    # バックグラウンドジョブ
│   │
│   ├── Hooks/                           # フックシステム
│   │   ├── IEntityHook.cs               # ★ フックインターフェース
│   │   ├── EntityHookContext.cs         # フック実行コンテキスト
│   │   ├── IEntityHookRegistry.cs       # フックレジストリインターフェース
│   │   ├── EntityHookRegistry.cs        # フレームワーク共通フックレジストリ
│   │   ├── IProjectHookRegistry.cs      # プロジェクトフックレジストリI/F
│   │   ├── ProjectHookRegistry.cs       # プロジェクトフックレジストリ実装
│   │   ├── ProjectHookLoader.cs         # .csファイルの動的コンパイルロード
│   │   ├── CommonHooks.cs               # ★ 組み込みフック20種以上
│   │   ├── SampleHooks.cs               # サンプルフック実装
│   │   ├── HookRejectReasonClassifier.cs # フック拒否理由の分類
│   │   ├── IProjectBusinessLogic.cs     # プロジェクト固有ロジックI/F
│   │   └── ProjectBusinessLogicRegistry.cs # プロジェクトロジックレジストリ
│   │
│   ├── Dialect/                         # DBダイアレクト抽象化
│   │   ├── ISqlDialect.cs               # SQLダイアレクトインターフェース
│   │   ├── SqliteDialect.cs             # SQLite実装
│   │   ├── SqlServerDialect.cs          # SQL Server実装
│   │   ├── PostgreSqlDialect.cs         # PostgreSQL実装
│   │   └── MySqlDialect.cs              # MySQL実装
│   │
│   └── Auth/                            # 認証・認可
│       ├── IUserAuthService.cs          # 認証サービスI/F
│       ├── UserAuthService.cs           # Cookie認証実装
│       ├── IAuditLogService.cs          # 監査ログI/F
│       ├── AuditLogService.cs           # 監査ログDB実装
│       ├── IPagePermissionService.cs    # ページ権限I/F
│       └── PagePermissionService.cs     # ロールベース権限実装
│
├── Models/                              # データ構造定義
│   ├── EntityMetadata.cs                # ★ YAML対応クラス群（全エンティティ定義）
│   ├── DashboardConfig.cs               # ダッシュボード設定モデル
│   ├── PageDefinition.cs                # カスタムページ設定モデル
│   ├── ProjectConfig.cs                 # project.yaml 対応モデル
│   ├── HomePageConfig.cs                # ホームページ設定モデル
│   ├── ErrorViewModel.cs                # エラー表示ViewModel
│   └── Auth/
│       ├── AppUser.cs                   # ユーザーエンティティ
│       ├── LoginViewModel.cs            # ログインフォームViewModel
│       └── UserEditViewModel.cs         # プロファイル編集ViewModel
│
├── Views/                               # Razor テンプレート
│   ├── DynamicEntity/
│   │   ├── Index.cshtml                 # ★ エンティティ一覧（フルページ）
│   │   ├── _List.cshtml                 # ★ 一覧部分ビュー（HTMX差し替え）
│   │   ├── _FilterControl.cshtml        # フィルターバー部分ビュー
│   │   ├── _Form.cshtml                 # ★ 作成/編集フォーム部分ビュー
│   │   ├── _FormField.cshtml            # フォームフィールドコンポーネント
│   │   ├── _Picker.cshtml               # FKピッカーモーダル
│   │   ├── FormPage.cshtml              # フルページフォーム
│   │   ├── Definition.cshtml            # デバッグ: 定義表示
│   │   ├── AllDefinitions.cshtml        # デバッグ: 全定義表示
│   │   └── ConfigDiagnostics.cshtml     # デバッグ: 設定診断
│   ├── Dashboard/
│   │   └── Index.cshtml                 # ダッシュボード
│   ├── Shared/
│   │   ├── _Layout.cshtml               # レイアウトテンプレート
│   │   └── Components/                  # Viewコンポーネント
│   └── ...
│
├── Middleware/
│   ├── ProjectMiddleware.cs             # ★ {project}ルート解析・ProjectScope設定
│   └── AuthMiddleware.cs                # Cookie認証検証
│
├── Analyzers/                           # Roslynアナライザー
│   └── NetYamlForge.Analyzers/
│       └── ForbiddenPatternAnalyzer.cs  # DCS001-DCS004 ビルド時チェック
│
├── Localization/
│   ├── LocalizationProjectContext.cs    # プロジェクトのi18nコンテキスト
│   └── YamlKeyLocalizer.cs             # YAMLキーの多言語解決
│
├── Resources/Localization/              # リソースファイル（.resx）
│   ├── SharedResource.ja-JP.resx
│   └── SharedResource.en-US.resx
│
├── wwwroot/lib/                         # ローカル化されたフロントエンドライブラリ
│   ├── htmx/                            # HTMX 1.9.10
│   ├── bootstrap/                       # Bootstrap 5
│   └── chart.js/                        # Chart.js
│
├── projects/                            # ★ マルチプロジェクト設定ルート
│   ├── chinook/                         # Chinookサンプル
│   ├── library/                         # 図書館サンプル（フルショーケース）
│   ├── todo/                            # TODOサンプル
│   └── <your-project>/                  # 追加プロジェクト
│       ├── project.yaml                 # DB設定・機能フラグ
│       ├── entities/                    # エンティティYAML（管理者編集）
│       ├── entities.generated/          # スキャフォールド自動生成
│       ├── entities-sqlserver/          # SQLServer方言上書き
│       ├── Hooks/                       # プロジェクト固有フック.cs
│       ├── database/                    # SQLiteファイル
│       ├── pages/                       # カスタムページYAML
│       ├── views/                       # プロジェクト固有Razorビュー
│       └── config/
│           ├── dashboard.yml            # ダッシュボード設定
│           ├── home-page.yml            # ホームページプロファイル
│           └── i18n.yml                 # 多言語設定
│
└── docs/                                # フレームワーク日本語ドキュメント
    ├── README-ja.md                     # ドキュメント案内（索引）
    ├── framework-structure-ja.md        # ← このファイル（全体構造）
    ├── annotated-architecture-ja.md     # コアコードの詳細解説
    ├── developer-tutorial-ja.md         # 開発者チュートリアル（完全版）
    ├── quickstart-ja.md                 # 5分クイックスタート
    ├── COMMON_HOOKS.md                  # 組み込みフック一覧
    ├── project-hooks-guide.md           # カスタムフックガイド
    ├── dashboard.md                     # ダッシュボード詳細
    └── examples/                        # コード変更例（正解例）
        ├── 01-add-simple-column.md
        ├── 02-add-validation-hook.md
        ├── 03-add-new-entity.md
        ├── 04-add-dashboard-stat.md
        └── 05-add-custom-hook.md
```

---

## 3. レイヤー構造と責務分担

```
┌─────────────────────────────────────────────────────────────┐
│  プレゼンテーション層（Views / Controllers）                  │
│  ─ HTTP リクエスト受付・レスポンス生成                        │
│  ─ HTMX による部分更新の制御                                  │
│  ─ ViewModel の組み立て（ビジネスロジックは持たない）          │
└──────────────────────────┬──────────────────────────────────┘
                           │ 呼び出す
┌──────────────────────────▼──────────────────────────────────┐
│  アプリケーション層（Services/）                              │
│  ─ DynamicEntityCommandService: CRUD コマンドのファサード    │
│  ─ EntityCrudExecutionService: フック実行・Tx管理            │
│  ─ DynamicEntityListQueryService: 一覧クエリ集約             │
│  ─ DynamicEntityFormValidationService: フォーム検証          │
└──────────────────────────┬──────────────────────────────────┘
                           │ 呼び出す
┌──────────────────────────▼──────────────────────────────────┐
│  インフラ層（Repository / Hooks / Auth）                      │
│  ─ DynamicCrudRepository: SQL組立・Dapper実行               │
│  ─ IEntityHook / CommonHooks: フック実装                    │
│  ─ EntityMetadataProvider: YAML→EntityDefinitionキャッシュ  │
│  ─ ISqlDialect: DB方言の差異を吸収                          │
│  ─ UserAuthService / AuditLogService: 認証・監査            │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│  データ層（Database / YAML）                                  │
│  ─ SQLite / SQL Server / PostgreSQL / MySQL                  │
│  ─ projects/<name>/entities/*.yml                           │
└─────────────────────────────────────────────────────────────┘
```

---

## 4. サービスクラス責務一覧

### コアサービス（変更頻度: 低）

| クラス | 責務 | 変更が必要なケース |
|--------|------|-----------------|
| `DynamicCrudRepository` | YAML→SQL組立・Dapper実行 | SQL方言固有機能の追加 |
| `EntityMetadataProvider` | YAML読み込み・キャッシュ | 新しいYAMLプロパティの追加 |
| `EntityCrudExecutionService` | フック実行順序・Tx境界 | フック優先順位変更 |
| `ProjectManager` | プロジェクト検出・初期化 | プロジェクト検出ロジック変更 |

### ファサード・集約サービス（変更頻度: 中）

| クラス | 責務 | 変更が必要なケース |
|--------|------|-----------------|
| `DynamicEntityCommandService` | Create/Update/Delete コマンド | 新CRUDコマンド（Upsert等）追加 |
| `DynamicEntityListQueryService` | 一覧クエリの集約 | ページング方式の追加 |
| `DynamicEntityForeignKeyDataService` | FK選択肢データ取得 | FK取得戦略の変更 |

### ユーティリティサービス（変更頻度: 低）

| クラス | 責務 |
|--------|------|
| `CommandErrorCodes` | エラーコード定数の管理 |
| `CommandErrorHttpMapper` | エラーコード→HTTPステータス変換 |
| `CommandResult<T>` | コマンド結果の統一型 |
| `DynamicEntityKeyResolverService` | URL→主キー値解決 |
| `DynamicEntityFormValidationService` | フォーム値の型変換・検証 |
| `DynamicEntityFormViewModelFactory` | フォームViewModelの組み立て |
| `DynamicEntityListResponseService` | 変更後一覧再取得 |
| `DynamicEntityListHttpResponseService` | HTMXヘッダー管理 |
| `HookExecutionTelemetry` | フック実行テレメトリ記録 |
| `HookRejectReasonClassifier` | フック拒否理由の分類 |
| `DynamicEntityConfigDiagnosticsService` | 設定診断情報の構築 |
| `BaseEntityMetadataProvider` | グローバル設定への参照（診断用） |

### 認証・認可サービス

| クラス | 責務 |
|--------|------|
| `UserAuthService` | ユーザー認証・Cookie発行 |
| `AuditLogService` | CRUD操作の監査ログDB記録 |
| `PagePermissionService` | ロールベースのページアクセス制御 |

---

## 5. リクエスト処理の全体フロー

```
HTTPリクエスト
    │
    ▼
[ProjectMiddleware]
    ─ URL から {project} を抽出
    ─ ProjectManager.TryGet(projectName) → ProjectInfo
    ─ ProjectScope.Set(projectInfo)（AsyncLocal で分離）
    ─ LocalizationProjectContext を設定
    │
    ▼
[AuthMiddleware]
    ─ Cookie "auth" を検証
    ─ 未認証 → /Account/Login にリダイレクト
    │
    ▼
[Controller.Action]
    ─ メタデータ取得: IEntityMetadataProvider.Get(entity)
    ─ アクセス制御: RejectIfNotVisible(meta)
    │
    ├─── GET（一覧表示）
    │    ─ DynamicEntityListQueryService.LoadAsync()
    │    ─ DynamicCrudRepository.GetAllAsync()（SQL組立・実行）
    │    ─ DynamicCrudRepository.CountAsync()
    │    ─ View / PartialView 返却
    │
    └─── POST（作成・更新・削除）
         ─ DynamicEntityFormValidationService.ConvertAndValidate()
         ─ DynamicEntityCommandService.CreateAsync() など
             ─ EntityCrudExecutionService.RunBeforeHookAsync()
             ─ EntityCrudExecutionService.ExecuteCrudTransactionAsync()
                 ─ DynamicCrudRepository.InsertAsync/UpdateAsync/DeleteAsync()
                 ─ EntityCrudExecutionService.RunAfterHookAsync()
                 ─ AuditLogService.WriteAsync()
             ─ tx.Commit() / tx.Rollback()
         ─ JSON { ok: true/false } 返却
         ─ [HTMX] DynamicEntityListResponseService でリスト再取得
```

---

## 6. YAML設定の読み込みフロー

```
アプリ起動時
    │
    ▼
[ProjectManager（Singleton）]
    ─ projects/ をスキャン
    ─ 各プロジェクトで EntityMetadataProvider を生成
        │
        ▼
    [EntityMetadataProvider（プロジェクトごと）]
        ─ entities.generated/*.yml を読み込み（最低優先）
        ─ entities-{provider}/*.yml を読み込み（方言上書き）
        ─ entities/*.yml を読み込み（最高優先・上書き）
        ─ 各YAMLを YamlSchemaValidator で検証
        ─ EntityDefinition にデシリアライズ
        ─ _entities 辞書にキャッシュ
    │
    ▼
リクエスト時
    ─ ProjectScope.Current → EntityMetadataProvider を解決
    ─ Get("customer") → キャッシュから EntityDefinition を返す
    ─ DynamicCrudRepository でSQL組立に使用
```

---

## 7. フックシステムの詳細

### フックの種類と使い分け

| 種類 | 場所 | 登録方法 | 適用範囲 |
|------|------|---------|---------|
| **フレームワーク共通フック** | `Services/Hooks/CommonHooks.cs` | `Program.cs` で `AddSingleton<IEntityHook, XxxHook>()` | 全プロジェクト共通 |
| **プロジェクト固有フック** | `projects/<name>/Hooks/*.cs` | 動的コンパイルロード（`ProjectHookLoader`） | プロジェクト限定 |

### フック実行の優先順位

```
YAML hooks.beforeCreate: ["validate_available", "trim"]
    │
    ▼
EntityCrudExecutionService.RunBeforeHookAsync()
    │
    ├─ 1. ProjectHookRegistry.Find(projectName, "validate_available")
    │      → プロジェクト固有フックが存在する場合: 実行して next へ
    │      → 存在しない場合: 2 へ
    │
    └─ 2. EntityHookRegistry.Find("validate_available")
           → フレームワーク共通フックが存在する場合: 実行して next へ
           → 存在しない場合: 警告ログ出力・スキップ
```

### フック設定構文早見表

```yaml
hooks:
  # 単一フック（文字列）
  beforeCreate: trim

  # 複数フック（リスト・順次実行）
  beforeCreate:
    - validate_required:Name,Email    # ":" 以降がフック設定パラメータ
    - validate_email:Email
    - trim:Name,Email
    - now:CreatedAt                   # 現在時刻を自動設定

  # プリセット定義（再利用可能なグループ）
  presets:
    commonRules:
      - validate_required:Name,Email
      - trim:Name,Email

  # プリセット参照（@ プレフィックス）
  beforeCreate:
    - "@commonRules"    # commonRules の全フックを展開して実行
    - now:CreatedAt
```

---

## 8. マルチプロジェクト機構

### プロジェクト分離の仕組み

```
リクエスト A: GET /chinook/DynamicEntity/Index?entity=customer
リクエスト B: GET /library/DynamicEntity/Index?entity=book

[ProjectMiddleware]
    ─ A: ProjectScope._current = chinook の ProjectInfo（AsyncLocal）
    ─ B: ProjectScope._current = library の ProjectInfo（AsyncLocal）

[DI解決時]
    ─ A: IEntityMetadataProvider → chinook の EntityMetadataProvider
    ─ B: IEntityMetadataProvider → library の EntityMetadataProvider

→ スレッドセーフ（AsyncLocal<T> による完全分離）
```

### project.yaml 設定例

```yaml
name: shop
displayName: "ショップ管理"

database:
  provider: sqlite                    # sqlite / sqlserver / postgresql / mysql
  connectionString: "Data Source=projects/shop/database/shop.db"

features:
  enableAuditLog: true               # CRUD操作の監査ログを有効化
  enableLocalization: true           # 多言語機能を有効化

aiHints:                             # AI生成コードへのヒント
  customHooks: [validate_stock]
  notes: "在庫管理システム"
  protectedEntities: [product]       # スキャフォールドで上書き禁止
```

---

## 9. データベース対応

### 対応DB一覧

| `provider` 設定値 | DB | 特記事項 |
|----------------|---|---------|
| `sqlite`（デフォルト） | SQLite | ローカル開発・小規模 |
| `sqlserver` | SQL Server | 本番環境推奨 |
| `postgresql` | PostgreSQL | `||` 演算子使用可 |
| `mysql` | MySQL | `CONCAT()` 関数使用 |

### SQLダイアレクト対応箇所

| 処理 | SQLite | SQL Server |
|------|--------|-----------|
| ページング | `LIMIT n OFFSET m` | `OFFSET m ROWS FETCH NEXT n ROWS ONLY` |
| 文字列結合 | `a \|\| b` | `a + b` |
| 現在時刻 | `datetime('now')` | `GETDATE()` |

---

## 10. セキュリティ設計

### 多層防御アーキテクチャ

```
Layer 1: YAML検証（YamlSchemaValidator）
  ─ ';' '--' '/*' '*/' を含む値を拒否
  ─ JSONスキーマで構造を検証

Layer 2: 識別子検証（DynamicCrudRepository.ValidateMetadata）
  ─ テーブル名・列名: ^[A-Za-z_][A-Za-z0-9_]*$ に一致しない場合はエラー
  ─ SQL式: ^[A-Za-z0-9_\.\s,()+-*/%<>=!'|]+$ に一致しない場合はエラー
  ─ JOIN型: left/inner/right のホワイトリスト

Layer 3: パラメータ化クエリ（Dapper）
  ─ 全てのユーザー入力値は @パラメータでバインド
  ─ SQL文字列への値直接埋め込みは DCS001 アナライザーがビルドエラー化

Layer 4: Roslynアナライザー（ビルド時静的解析）
  ─ DCS001: $"...{変数}..." SQL補間 → Error
  ─ DCS002: .Result/.Wait() ブロッキング → Error
  ─ DCS003: new SqliteConnection() 直接生成 → Error
  ─ DCS004: "Admin" ロール名ハードコード → Warning
```

---

## 11. 変更判断フロー（コードを書く前に確認）

```
変更したい内容
    │
    ├─ テーブル・フォーム・一覧の変更
    │   └─ → YAMLのみ変更。コード不要。
    │
    ├─ 既存エンティティへの列追加
    │   └─ → entities/<name>.yml の columns / forms に追加。コード不要。
    │
    ├─ バリデーション・変換・通知の追加
    │   ├─ 既存フックで対応可能（COMMON_HOOKS.md 参照）
    │   │   └─ → YAMLの hooks セクションに追加。コード不要。
    │   └─ 既存フックで対応不可
    │       └─ → IEntityHook を実装（projects/<name>/Hooks/）
    │
    ├─ ダッシュボード統計の追加
    │   └─ → config/dashboard.yml を変更。コード不要。
    │
    ├─ 独自UI・レポートページ
    │   └─ → pages/*.yml + PageController のカスタムアクション
    │
    └─ フレームワーク機能の追加（全プロジェクト共通）
        └─ → Services/ 配下の適切なサブディレクトリに追加 + テスト必須
```

---

## 12. テスト構成

```
NetYamlForge.Tests/
├── Controllers/
│   └── DynamicEntityControllerTests.cs      # コントローラー統合テスト
├── Services/
│   ├── EntityCrudExecutionServiceTests.cs   # フック実行・トランザクション
│   ├── PageRowMutationServiceTests.cs       # CRUD検証ロジック
│   └── SqlGenerationSnapshotTests.cs        # SQL生成の回帰テスト
├── Hooks/
│   └── <Name>HookTests.cs                   # フック単体テスト
├── YamlSchemaValidationTests.cs             # 全YAML形式の自動検証
└── CommandErrorHttpMapperTests.cs           # エラーコードHTTPマッピング
```

### テスト原則

```csharp
// インメモリ SQLite を使った完全分離テスト
await using var conn = new SqliteConnection("Data Source=:memory:");
await conn.OpenAsync();
// テーブル作成 → データ投入 → SUT 実行 → アサート
```

---

## 13. 関連ドキュメント一覧

| ドキュメント | 内容 |
|------------|------|
| `quickstart-ja.md` | 5分で動かす手順 |
| `developer-tutorial-ja.md` | ゼロから業務アプリ構築のステップバイステップ |
| `annotated-architecture-ja.md` | コアコードの詳細アノテーション解説 |
| `COMMON_HOOKS.md` | 組み込みフック20種以上の使い方 |
| `project-hooks-guide.md` | カスタムフック実装の詳細ガイド |
| `dashboard.md` | ダッシュボード・チャートの設定詳細 |
| `examples/01-add-simple-column.md` | 列追加の最小変更例 |
| `examples/02-add-validation-hook.md` | バリデーション追加例 |
| `examples/03-add-new-entity.md` | 新エンティティ追加の全手順 |
| `examples/04-add-dashboard-stat.md` | 統計カード追加例 |
| `examples/05-add-custom-hook.md` | カスタムフック実装テンプレート |
