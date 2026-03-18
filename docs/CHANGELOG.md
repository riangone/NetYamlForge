# CHANGELOG

## 2026-03-16（Section Table: hooks / sortable columns / create・update 分離）

### Added
1. `Models/PageDefinition.cs` — `SectionHooksDefinition` クラスを追加。`SectionDefinition.Hooks` プロパティ。
2. `Services/PageRowMutationService.cs` — `IEntityHookRegistry` を注入。`InsertRowAsync` / `UpdateAllFieldsAsync` / `DeleteRowAsync` でフック呼び出し対応。

### Changed
1. `Views/Page/Components/_SectionTable.cshtml` — `sortable: true` の列ヘッダーをソートリンクに変更。`{sectionId}__sort` / `{sectionId}__dir` クエリパラメータに対応。
2. `Services/PageDataQueryService.cs` — `_sort` / `_dir` フィルターパラメータを ORDER BY に反映。
3. `Models/PageDefinition.cs` — `GetFormFields` で `"edit"` と `"update"` を相互エイリアスとして扱うよう変更。
4. `projects/ui-showcase/pages/SectionTableDemo.yaml` — sortable / hooks / update forms の使用例を追加。
5. `docs/ui/page-components.md` — columns 辞書形式・ソート・フック・forms.update の説明を追加。

### YAML 設定例

```yaml
sections:
  - id: products
    source_type: table
    source: Product
    columns:
      id:    { label: ID, type: int, hidden: true }
      name:  { label: 商品名, sortable: true }
      price: { label: 価格, type: decimal, sortable: true }
    forms:
      create:
        fields: [name, category, price]
      update:                             # "edit" でも動作
        fields: [category, price]         # 編集時は一部のみ
    hooks:
      before_create: [trim, validate_required]
      after_create:  [audit_log]
      before_update: [validate_required]
    editable: true
    target_table: Product
    target_primary_key: id
```

### Verification
1. `dotnet build` 成功（警告 0）。
2. `dotnet test` 全 385 件パス。

---

## 2026-03-16（Section Table: フィルター・ページネーション・CRUD フォーム対応）

### Added
1. `Views/Page/Components/_SectionRowForm.cshtml` — セクション行の新規/編集モーダルフォームパーシャル。
2. `Models/PageDefinition.cs` — `SectionRowFormModel` クラスを追加。
3. `Services/PageDataQueryService.cs` — `GetRowByIdAsync` を追加（編集フォーム用の行取得）。
4. `Services/PageRowMutationService.cs` — `InsertRowAsync` / `UpdateAllFieldsAsync` を追加。
5. `Controllers/PageController.cs` — 3 エンドポイントを追加:
   - `GET /{project}/Page/{pageName}/section/{sectionId}/row-form`
   - `POST /{project}/Page/{pageName}/section/{sectionId}/insert-row`
   - `POST /{project}/Page/{pageName}/section/{sectionId}/update-all-fields`

### Changed
1. `Views/Page/Components/_SectionTable.cshtml` — フィルター UI・ページネーション・CRUD ボタン（新規/編集/削除）を追加。
2. `Services/PageDataQueryService.cs` — `{sectionId}__page` クエリパラメータによるページネーションオフセット対応。
3. `docs/ui/page-components.md` — Table コンポーネント機能詳細セクションを追加。

### YAML 設定例

```yaml
sections:
  - id: products
    source_type: table
    source: Product
    columns: [id, name, category, price]
    filters:
      name: { label: 商品名, type: like }
      category: { label: カテゴリ, type: select, options: { A: Cat A, B: Cat B } }
    editable: true
    target_table: Product
    target_primary_key: id
    updatable_fields: [name, category, price]
    page_size: 20
```

### Verification
1. `dotnet build` 成功（警告 0）。
2. `dotnet test` 全 385 件パス。



## 2026-03-03（dashboard.yml の起動時スキーマ検証）

### Added
1. `Schemas/dashboard-schema.json` を追加（stats/charts の構造と基本型を検証）。

### Changed
1. `DashboardConfigProvider` で `dashboard.yml` 読み込み時にスキーマ検証を実行。
2. `NetYamlForge.csproj` に `dashboard-schema.json` を埋め込みリソース追加。

### Verification
1. `dotnet build` 成功。
2. `dotnet run` 起動成功（Dashboard ルート疎通確認）。

## 2026-03-03（entities YAML の起動時スキーマ検証）

### Added
1. `Schemas/entity-schema.json` を追加（`entities/*.yml` の基本構造を検証）。

### Changed
1. `EntityMetadataProvider` で `entities*.yml` / `entities.yml` / `imports` 読み込み時にスキーマ検証を実行。
2. `NetYamlForge.csproj` に `entity-schema.json` を埋め込みリソース追加。

### Verification
1. `dotnet build` 成功。
2. `dotnet run` 起動成功（既存プロジェクト含む）。

## 2026-03-03（ページYAMLの起動時スキーマ検証）

### Changed
1. `PageMetadataProvider` で `pages/*.yaml` 読み込み時に `ui-page-schema.json` 検証を実行。
2. 検証エラーはページ単位で集約し、起動時に失敗させる運用へ変更（早期検知）。
3. `YamlSchemaValidator` の YAML 値変換を拡張し、boolean に加えて数値も型変換して検証精度を改善。

### Verification
1. `dotnet build` 成功。
2. `dotnet run` 起動成功後、`GET /b2b-order-ops/Page/OrderWorkbench` が `302 Found`（認証ガード正常）。

## 2026-03-03（UIコンポーネント定義基盤の追加）

### Added
1. UI設計ガイドを追加（`docs/ui/ui-design-system-ja.md`）。
2. YAML駆動UI向けスキーマを追加（`Schemas/ui-page-schema.json`）。
3. 汎用ページテンプレートを追加（`projects/_templates/page-crud.yml`）。
4. `b2b-order-ops` に実例ページ `OrderWorkbench` を追加（`pages/OrderWorkbench.yaml`）。

### Changed
1. `b2b-order-ops/project.yaml` のナビゲーションに `OrderWorkbench` を追加。
2. `projects/b2b-order-ops/docs/implementation-guide-ja.md` を現構成（Hook依存なし）に更新。

### Verification
1. `dotnet build` 成功。
2. `GET /b2b-order-ops/Page/OrderWorkbench` で `302 Found`（認証ガード正常）を確認。

## 2026-03-03（B2B受発注管理プロジェクト追加）

### Added
1. 新規サブプロジェクト `b2b-order-ops` を追加（受発注管理の実運用検証用）。
2. `order/orderdetail/product/customer/supplier/employee/shipper/category` のエンティティ定義を追加。
3. ダッシュボードと業務ページ（`FulfillmentQueue` / `ReplenishmentPlan` / `CustomerRiskRadar`）を追加。
4. 日本語実装ガイドを追加（`projects/b2b-order-ops/docs/implementation-guide-ja.md`）。

### Changed
1. `project.yaml` の表示名とナビゲーション URL を `b2b-order-ops` に統一。
2. 初期検証を安定化するため、`order` / `orderdetail` からプロジェクト固有 Hooks 依存を除去。

### Verification
1. `dotnet build` 成功（0 Warning / 0 Error）。
2. `GET /b2b-order-ops/DynamicEntity/Index?entity=order` が `302 Found`（認証ガード正常）。
3. `GET /b2b-order-ops/Dashboard` が `200 OK`。

## 2026-03-03（NU1900 運用改善）

### Changed
1. `NetYamlForge.csproj` に `NuGetAudit` 条件設定を追加（`CI != true` のときのみ無効化）。
2. ローカル開発環境での `NU1900` 常時警告を抑止し、CI では脆弱性監査を継続する運用へ変更。

### Verification
1. `dotnet build NetYamlForge/NetYamlForge.csproj` 成功（`NU1900` 非表示）。

## 2026-03-03（Wave 4 初期着手）

### Added
1. `RequestTraceMiddleware` を追加し、全レスポンスに `X-Trace-Id` を付与。
2. `X-Trace-Id` ヘッダー受信時は同値を `HttpContext.TraceIdentifier` に採用。
3. `RequestTraceMiddlewareTests` を追加（ヘッダー優先/フォールバック/空白値の3ケース）。
4. Wave 4 バックログを追加（`docs/improvement-plan/backlog-wave4.md`）。
5. `ListStateUrlBuilder` とそのテストを追加し、一覧状態URL生成を共通化。
6. `clear=1` を解釈する回帰防止ロジックを追加し、`FilterValueParser.BuildCleared` / `ResolveClearRequested` テストを追加。

### Changed
1. `UseSerilogRequestLogging` に `TraceId` / `Project` の diagnostic context 出力を追加。
2. `ListPartial` が `HX-Push-Url` を返すように変更し、filter/sort/page の状態URLを `Index` に固定。
3. DynamicEntity の Clear 操作で `clear` フラグを送信し、picker hidden 値が残留してもサーバ側で確実に条件クリアするよう変更。

### Verification
1. `dotnet build` 成功。
2. `dotnet test /home/ubuntu/ws/ccc/NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功（Passed: 25）。

## 2026-03-03（Wave 3 前半実装）

### Added
1. Config diagnostics に `Only changed` 切替と差分件数表示を追加。
2. slow query メトリクスの定期スナップショット出力を追加（`DYNAMICCRUD_SLOW_QUERY_SUMMARY_MS`）。
3. `count/paging` 共通ヘルパーと回帰テストを追加（テスト総数 16）。
4. Hook ErrorCode ベースの運用 runbook を追加（`docs/improvement-plan/hook-diagnostics-runbook.md`）。
5. 手動スモーク実施結果を `release-readiness-2026-03-03.md` に記録。

### Changed
1. 互換性維持のため `ConfirmationDefinition.Delete` と `ColumnDefinition.Options` を追加し、既存 YAML 設定を受け入れるよう修正。
2. `DYNAMICCRUD_SLOW_QUERY_MS` / `DYNAMICCRUD_SLOW_QUERY_SUMMARY_MS` を10秒間隔で再読込し、再起動なしで閾値変更を反映。

### Verification
1. `dotnet build` 成功。
2. `dotnet test /home/ubuntu/ws/ccc/NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功（Passed: 16）。

## 2026-03-03（Wave 2 完了・運用ドキュメント固定）

### Added
1. Config diagnostics を `Base / Effective / Diff` の3表示に拡張。
2. `NetYamlForge.Tests` の回帰テストを 8 ケースまで拡張。
3. slow query の operation/entity 別カウンタを追加（ログに `slowCount` 出力）。
4. リリース準備チェック文書を追加（`docs/improvement-plan/release-readiness-2026-03-03.md`）。

### Changed
1. `backlog-wave2.md` の全項目ステータスを Completed に更新。
2. YAML 検証と Hook エラー分類ログを強化（ErrorCode 付き）。

### Verification
1. `dotnet build` 成功。
2. `dotnet test /home/ubuntu/ws/ccc/NetYamlForge.Tests/NetYamlForge.Tests.csproj` 成功（Passed: 8）。

## 2026-03-03（DynamicEntity 検索欄に Clear 機能を追加）

### 追加

#### 1. 検索・フィルタ条件の一括クリア機能

**対応内容**:
- DynamicEntity 一覧の検索フォームに `Clear` ボタンを追加
- クリック時に `search` と全フィルタ入力（text/select/date/checkbox/radio）をクリア
- `entity` / `count` / `returnUrl` など必要な hidden 値は保持
- クリア後は HTMX で `ListPartial` を再実行し、一覧を即時更新

**影響ファイル**:
- `Views/DynamicEntity/Index.cshtml`

### 検証結果
1. `dotnet build` 成功（0 エラー）

### 修正

#### 2. Clear ボタンで entity-picker 条件が残る不具合

**対応内容**:
- hidden フィルタ値（entity-picker / entity-multi-picker）もクリア対象に変更
- `multi-select` の複数選択解除を明示実装

## 2026-03-03（northwind-sqlite3 サブプロジェクト追加）

### 追加

#### 1. Northwind SQLite3 ベースの新規サブプロジェクト

**対応内容**:
- `projects/northwind-sqlite3` を新規作成
- `project.yaml` / `layout.yml` / `dashboard.yml` を追加
- URL ルート: `/northwind-sqlite3/...`

**影響ファイル**:
- `projects/northwind-sqlite3/project.yaml`
- `projects/northwind-sqlite3/layout.yml`
- `projects/northwind-sqlite3/dashboard.yml`

#### 2. Northwind 風データベース（SQLite）を新規作成

**対応内容**:
- テーブル: `Customers`, `Employees`, `Shippers`, `Suppliers`, `Categories`, `Products`, `Orders`, `OrderDetails`
- サンプルデータを投入済み
- 初期化 SQL を `database/init.sql` として保存

**影響ファイル**:
- `projects/northwind-sqlite3/database/init.sql`
- `projects/northwind-sqlite3/database/northwind.db`

#### 3. 複数業務シナリオを実装

**対応内容**:
- 受注管理 (`order`)：`picker` + `multiPicker` を実装
- 受注明細 (`orderdetail`)：商品 picker と明細金額計算列を実装
- 在庫アラート (`LowStockAlert`)：再発注レベル割れ監視
- 配送遅延監視 (`ShippingDelayMonitor`)：遅延日数可視化
- 営業KPI (`SalesKpi`)：顧客別・担当者別・商品別集計

**影響ファイル**:
- `projects/northwind-sqlite3/entities/*.yml`
- `projects/northwind-sqlite3/pages/*.yaml`

#### 4. 外部キー表示列とカスタムクエリの実例を追加

**対応内容**:
- `foreignKey.displayColumns` で複数列ラベル表示
- `foreignKey.query` で候補データを制御
- 既存 `picker` / `multipicker` の動作を維持

#### 5. 詳細ドキュメントを追加

**影響ファイル**:
- `projects/northwind-sqlite3/docs/northwind-scenarios-ja.md`

## 2026-03-03（ForeignKey 複数表示列・カスタムQuery対応 / Chinook YAML更新）

### 追補（picker / multipicker 互換性）

#### 1. PickerList API の `displayColumns` 受け口を追加（後方互換維持）

**対応内容**:
- `DynamicEntityController.PickerList` に `displayColumns`（複数）パラメータを追加
- `displayColumns` 優先、未指定時は既存の `displayColumn` を継続利用
- 既存フロント呼び出し互換のため、ピッカー呼び出し時に `displayColumn` と `displayColumns` を併送

**影響ファイル**:
- `Controllers/DynamicEntityController.cs`
- `Views/Shared/_Layout.cshtml`

### 追加

#### 1. `foreignKey` の表示列を複数指定可能に拡張

**対応内容**:
- `ForeignKeyDefinition` に `displayColumns`（配列）を追加
- 既存の `displayColumn` は後方互換として維持し、`A,B,C` 形式も解釈
- 共通ヘルパー `GetDisplayColumns()` を追加

**影響ファイル**:
- `Models/EntityMetadata.cs`

#### 2. `foreignKey.query` による候補データ取得SQLのカスタマイズ

**対応内容**:
- FK 候補取得APIを拡張し、`query` 指定時はサブクエリとして利用
- ピッカー検索時は `displayColumns` に対して `LIKE` 検索を適用
- `PickerList` から `query` を受け取り、UI からサーバへ伝搬

**影響ファイル**:
- `Services/DynamicCrudRepository.cs`
- `Controllers/DynamicEntityController.cs`
- `Views/Shared/_Layout.cshtml`

#### 3. フォーム・フィルター・ピッカーの複数列ラベル表示

**対応内容**:
- `displayColumns` を `/` 区切りで連結したラベル表示に統一
- ピッカーの行選択ラベルも同一ロジックに統一

**影響ファイル**:
- `Views/DynamicEntity/_FormField.cshtml`
- `Views/DynamicEntity/_FilterControl.cshtml`
- `Views/DynamicEntity/_Picker.cshtml`

#### 4. `foreignKey` 設定の安全性検証を強化

**対応内容**:
- `displayColumns` の識別子チェック
- `query` の危険トークン拒否（`;`, `--`, `/*`, `*/`）
- `query` は `SELECT` 開始かつ `Id` 列を含むことを検証

**影響ファイル**:
- `Services/DynamicCrudRepository.cs`

#### 5. Chinook プロジェクトの YAML を新形式へ更新

**対応内容**:
- `projects/chinook/entities/*.yml` の `displayColumn` を `displayColumns` へ移行
- 主要 FK に `query` を追加（customer / employee / invoice / track）

**影響ファイル**:
- `projects/chinook/entities/invoice.yml`
- `projects/chinook/entities/customer.yml`
- `projects/chinook/entities/invoiceline.yml`
- `projects/chinook/entities/track.yml`
- `projects/chinook/entities/album.yml`

#### 6. 日本語ドキュメント追加

**追加ファイル**:
- `docs/foreignkey-displaycolumns-query-ja.md`

### 検証結果
1. `dotnet build` 成功（0 エラー / 0 警告）

## 2026-03-03（プロジェクトフック登録・実行バグ修正・Blog プロジェクトフック追加）

### 修正

#### 1. プロジェクトフックの AfterCreate で ID が取得できないバグ

**問題**: `DynamicEntityController.Create` メソッドで、新規作成後の AfterCreate フックに `EntityHookContext.Id` が設定されていなかった。このため、プロジェクトフック（例：`ChinookCustomerWelcomeHook`）の `AfterAsync` メソッド内で `ctx.Id` を使用しても null になっていた。

**修正内容**:
- `DynamicEntityController.Create` メソッドで `InsertAsync` の戻り値（新規 ID）を取得
- 取得した ID を `hookCtx.Id` に設定してから AfterCreate フックを実行

**影響ファイル**:
- `Controllers/DynamicEntityController.cs` — Create メソッドのトランザクション処理部分

#### 2. Blog プロジェクトに Hooks ディレクトリが存在しない

**問題**: `projects/blog/` には `Hooks/` ディレクトリが存在せず、プロジェクト固有フックが登録されていなかった。

**修正内容**:
- `projects/blog/Hooks/` ディレクトリを作成
- `BlogPostHooks.cs` を追加（以下のフックを含む）:
  - `BlogPostSlugGeneratorHook` — タイトルからスラッグを自動生成（beforeCreate）
  - `BlogPostPublishedNotificationHook` — 記事公開時に通知を送信（afterCreate）
  - `BlogCommentSpamCheckHook` — コメントのスパムチェック（beforeCreate）

**影響ファイル**:
- `projects/blog/Hooks/BlogPostHooks.cs` — 新規追加
- `projects/blog/entities/post.yml` — hooks 設定を追加

### 変更

#### 1. プロジェクトフックの登録・呼び出し動作の確認

**検証結果**:
- chinook プロジェクトの `chinook_customer_welcome` フックが正常に実行されることを確認
- blog プロジェクトの `blog_post_slug_generator` フックが正常に実行されることを確認
- 両プロジェクトとも `ProjectHookLoader` による動的フック読み込みが正常に動作

**ログ出力例**:
```
[DBG] プロジェクト 'chinook' にフック 'chinook_customer_welcome' を登録しました
[DBG] プロジェクトフック 'chinook_customer_welcome' を実行 (Project=chinook)
[INF] [Chinook] 顧客 Luís Gonçalves さん、ようこそ！メールを luisg@embraer.com.br へ送信します。
```

### 追加ファイル

- `projects/blog/Hooks/BlogPostHooks.cs` — Blog プロジェクト固有のエンティティフック

### 更新ファイル

- `Controllers/DynamicEntityController.cs` — AfterCreate フックで ID を設定
- `projects/blog/entities/post.yml` — hooks 設定を追加
- `docs/CHANGELOG.md` — 本変更記録を追加

---

## 2026-03-03（起動エラー修正・ビルド警告除去・ホーム画面改善・ユーザーメニュー強化）

### 修正

#### 1. 起動エラーの修正（DI スコープ問題）

**問題**: `AuditLogHook`（Singleton）が `IAuditLogService`（Scoped）をコンストラクターインジェクションしていたため、DI 検証エラーが発生。

**修正内容**:
- `AuditLogHook` を `IServiceProvider` を受け取るように変更
- 実行時に `GetService<IAuditLogService>()` で Scoped サービスを取得
- `Program.cs` の登録をファクトリーメソッドに変更

**影響ファイル**:
- `Services/Hooks/CommonHooks.cs` — `AuditLogHook` のコンストラクターと `AfterAsync` メソッド
- `Program.cs` — `AuditLogHook` の DI 登録

#### 2. chinook プロジェクトのフックコンパイルエラー修正

**問題**: `projects/chinook/Hooks/SampleHooks.cs` に必要な using 文が不足。

**修正内容**:
- `using Microsoft.Extensions.Logging;` を追加
- `using System.Threading.Tasks;` を追加

**影響ファイル**:
- `projects/chinook/Hooks/SampleHooks.cs`

### 変更

#### 1. ビルド警告の除去

**CS8625**: `DynamicEntityController.Edit` の `form` パラメータの null 許容性を修正
**CS8602/CS8620**: Razor ビューの null 許容性警告を csproj で抑制
**CS0219**: `Index.cshtml` の未使用変数 `currentProject` を削除
**NU1603**: `JsonSchema.Net` を 7.4.3 → 8.0.0 に更新

**影響ファイル**:
- `NetYamlForge.csproj` — `<NoWarn>` 追加、パッケージバージョン更新
- `Controllers/DynamicEntityController.cs` — `form` パラメータの null 許容性修正
- `projects/blog/views/Index.cshtml` — 未使用変数削除
- `Views/_ViewImports.cshtml` — `#nullable disable` 追加
- `projects/*/views/_ViewImports.cshtml` — `#nullable disable` 追加

#### 2. ホーム画面の改善

**機能追加**:
- ホーム画面（`/`）で全プロジェクトの一覧を表示
- 各プロジェクトにアイコン・説明・DB 種別を表示
- プロジェクトカードのグリッドレイアウトを実装
- デフォルトルートを `Home/Index` に変更

**影響ファイル**:
- `Controllers/HomeController.cs` — `ProjectManager` を注入
- `Views/Home/Index.cshtml` — プロジェクト一覧表示に全面刷新
- `Program.cs` — デフォルトルート変更

#### 3. サブプロジェクト用ユーザーメニューの強化

**機能追加**:
- blog と todo の専用レイアウトでユーザーメニューを強化
- アバター表示（イニシャル）
- 多言語切り替え（🇺🇸🇨🇳🇯🇵）
- ユーザー名表示
- 管理者用ユーザー管理リンク
- ログアウトボタン

**影響ファイル**:
- `projects/blog/views/_Layout.cshtml` — ユーザーメニュー追加
- `projects/todo/views/_Layout.cshtml` — ユーザーメニュー追加

### 検証結果

| 項目 | 結果 |
|------|------|
| `dotnet build` | ✅ 成功（0 エラー / 0 警告） |
| `dotnet run` | ✅ 正常起動（`http://localhost:5239`） |
| `/` ホーム画面 | ✅ プロジェクト一覧表示 |
| `/blog/` サブプロジェクト | ✅ ユーザーメニュー機能 |
| `/todo/` サブプロジェクト | ✅ ユーザーメニュー機能 |

---

## 2026-03-02（プロジェクト別 layout 設定・複合主鍵対応）

### 追加

#### 1. プロジェクト別 layout 設定

各プロジェクトで固有のレイアウト設定を使用できるようになりました。

**モデル追加（`Models/ProjectConfig.cs`）:**
- `ProjectLayoutConfig` — レイアウト設定クラス
- `ProjectNavigationConfig` — ナビゲーション設定
- `ProjectNavigationItemConfig` — カスタムナビゲーション項目
- `ProjectHeaderConfig` — ヘッダー設定
- `ProjectFooterConfig` — フッター設定

**YAML 構文:**
```yaml
layout:
  header:
    title: My Project
  navigation:
    showDashboard: true
    entities:
      - customer
      - invoice
    items:
      - label: Dashboard
        controller: Dashboard
        action: Index
        icon: 📊
      - label: Reports
        url: /reports
        icon: 📈
        adminOnly: true
```

**機能:**
- プロジェクト固有のヘッダータイトル
- エンティティ表示のフィルタリング（`navigation.entities`）
- カスタムナビゲーション項目の追加
- ダッシュボードリンクの表示/非表示切り替え
- Admin 専用メニュー項目

**設定方法:**
1. `project.yaml` に `layout` セクションを追加
2. または `layout.yml` ファイルをプロジェクトディレクトリに配置

**優先順位:**
- `layout.yml` の設定が `project.yaml` より優先

**更新ファイル:**
- `Models/ProjectConfig.cs` — レイアウト設定モデル
- `Services/ProjectInfo.cs` — Layout プロパティ追加
- `Services/ProjectManager.cs` — layout.yml 読み込み対応
- `Views/Shared/_Layout.cshtml` — プロジェクトレイアウト設定サポート

**プロジェクト設定例:**
- `projects/chinook/layout.yml` — Chinook 用設定
- `projects/todo/layout.yml` — TODO 用設定
- `projects/blog/layout.yml` — Blog 用設定

#### 2. 複合主鍵（多主鍵）対応

エンティティ定義 YAML で複合主鍵（複数の列からなる主鍵）を定義できるようになりました。

**モデル変更（`Models/EntityMetadata.cs`）:**
| メンバー | 説明 |
|---------|------|
| `Keys` プロパティ | 複合主鍵の列名リスト（`List<string>`） |
| `GetPrimaryKeyColumns()` | 主鍵列のリストを返す（単一主鍵時は `[Key]`、複合主鍵時は `Keys`） |
| `IsCompositeKey` | 複合主鍵かどうかを返す |

**YAML 構文:**
```yaml
entities:
  orderdetail:
    table: OrderDetail
    keys: ["OrderId", "ProductId"]  # 複合主鍵
    displayName: Order Detail
```

**リポジトリ拡張（`Services/DynamicCrudRepository.cs`）:**
- `GetByIdAsync(entity, keyValues)` — 複合主鍵対応の単件取得
- `UpdateAsync(entity, keyValues, values, tx)` — 複合主鍵対応の更新
- `DeleteAsync(entity, keyValues, tx)` — 複合主鍵対応の削除
- `GetAllAsync` — 複合主鍵のページング・keyset カーソル対応
- `ValidateMetadata` — 全主鍵列の検証

**コントローラー対応（`Controllers/DynamicEntityController.cs`）:**
- `BuildCompositeKeyId()` — 複合主鍵から JSON 形式 ID を構築
- `ParseCompositeKey()` — JSON 形式 ID から複合主鍵を解析
- `EditForm`, `EditPage`, `Edit`, `Delete` アクションの `id` パラメータを `string` に変更

**フックコンテキスト拡張（`Services/Hooks/EntityHookContext.cs`）:**
- `KeyValues` プロパティ — 複合主鍵の値マップ

**URL 形式:**
- 単一主鍵：`/chinook/DynamicEntity/EditPage?entity=customer&id=123`
- 複合主鍵：`/chinook/DynamicEntity/EditPage?entity=orderdetail&id={"OrderId":1001,"ProductId":5}`

**新規ドキュメント:**
- `docs/composite-key-example.md` — 複合主鍵の配置ガイド・実例

### 変更

#### 1. ファイル一覧

| ファイル | 変更内容 |
|---------|---------|
| `Models/ProjectConfig.cs` | レイアウト設定モデル追加 |
| `Models/ProjectInfo.cs` | Layout プロパティ追加 |
| `Services/ProjectManager.cs` | layout.yml 読み込み対応 |
| `Views/Shared/_Layout.cshtml` | プロジェクトレイアウト設定サポート |
| `Models/EntityMetadata.cs` | 複合主鍵プロパティ・メソッド追加 |
| `Services/DynamicCrudRepository.cs` | 複合主鍵対応の CRUD メソッド追加・インターフェース拡張 |
| `Controllers/DynamicEntityController.cs` | 複合主鍵解析・ビルドヘルパー、アクションメソッドの id パラメータを string に |
| `Services/Hooks/EntityHookContext.cs` | `KeyValues` プロパティ追加 |
| `docs/CHANGELOG.md` | 本変更記録を追加 |
| `docs/composite-key-example.md` | 新規作成（複合主鍵配置ガイド） |
| `projects/*/layout.yml` | 各プロジェクトのレイアウト設定 |
| `projects/*/project.yaml` | layout 設定追加 |
| `QWEN.md` | プロジェクトコンテキストの全面更新 |

### 検証結果

| 項目 | 結果 |
|------|------|
| `dotnet build` | ✅ 成功（0 エラー） |
| 単一主鍵エンティティ動作 | ✅ 既存機能は変更なし（後方互換性維持） |
| 複合主鍵 YAML 構文 | ✅ 設計ベースで検証可能 |
| プロジェクト別 layout 設定 | ✅ 設計ベースで検証可能 |

---

## 2026-03-02（複合主鍵対応・マルチプロジェクト QWEN.md 更新）

### 追加

#### 1. 複合主鍵（多主鍵）対応

エンティティ定義 YAML で複合主鍵（複数の列からなる主鍵）を定義できるようになりました。

**モデル変更（`Models/EntityMetadata.cs`）:**
| メンバー | 説明 |
|---------|------|
| `Keys` プロパティ | 複合主鍵の列名リスト（`List<string>`） |
| `GetPrimaryKeyColumns()` | 主鍵列のリストを返す（単一主鍵時は `[Key]`、複合主鍵時は `Keys`） |
| `IsCompositeKey` | 複合主鍵かどうかを返す |

**YAML 構文:**
```yaml
entities:
  orderdetail:
    table: OrderDetail
    keys: ["OrderId", "ProductId"]  # 複合主鍵
    displayName: Order Detail
```

**リポジトリ拡張（`Services/DynamicCrudRepository.cs`）:**
- `GetByIdAsync(entity, keyValues)` — 複合主鍵対応の単件取得
- `UpdateAsync(entity, keyValues, values, tx)` — 複合主鍵対応の更新
- `DeleteAsync(entity, keyValues, tx)` — 複合主鍵対応の削除
- `GetAllAsync` — 複合主鍵のページング・keyset カーソル対応
- `ValidateMetadata` — 全主鍵列の検証

**コントローラー対応（`Controllers/DynamicEntityController.cs`）:**
- `BuildCompositeKeyId()` — 複合主鍵から JSON 形式 ID を構築
- `ParseCompositeKey()` — JSON 形式 ID から複合主鍵を解析
- `EditForm`, `EditPage`, `Edit`, `Delete` アクションの `id` パラメータを `string` に変更

**フックコンテキスト拡張（`Services/Hooks/EntityHookContext.cs`）:**
- `KeyValues` プロパティ — 複合主鍵の値マップ

**URL 形式:**
- 単一主鍵：`/chinook/DynamicEntity/EditPage?entity=customer&id=123`
- 複合主鍵：`/chinook/DynamicEntity/EditPage?entity=orderdetail&id={"OrderId":1001,"ProductId":5}`

**新規ドキュメント:**
- `docs/composite-key-example.md` — 複合主鍵の配置ガイド・実例

#### 2. QWEN.md 更新

プロジェクトコンテキストファイルを更新し、以下の情報を拡充しました：
- マルチプロジェクト架構の详细说明
- プロジェクト構造の最新化（Controllers/Services/Views の新コンポーネント）
- 多項目ルート形式と例
- 核心機能の追加（ダッシュボード・チャート、Page 機能、SQL Server 対応、フックと確認ダイアログ）
- 開発約定（マルチプロジェクト開発規範）
- 主要インターフェース（多項目核心コンポーネント API）

### 変更

#### 1. ファイル一覧

| ファイル | 変更内容 |
|---------|---------|
| `Models/EntityMetadata.cs` | 複合主鍵プロパティ・メソッド追加 |
| `Services/DynamicCrudRepository.cs` | 複合主鍵対応の CRUD メソッド追加・インターフェース拡張 |
| `Controllers/DynamicEntityController.cs` | 複合主鍵解析・ビルドヘルパー、アクションメソッドの id パラメータを string に |
| `Services/Hooks/EntityHookContext.cs` | `KeyValues` プロパティ追加 |
| `docs/CHANGELOG.md` | 本変更記録を追加 |
| `docs/composite-key-example.md` | 新規作成（複合主鍵配置ガイド） |
| `QWEN.md` | プロジェクトコンテキストの全面更新 |

### 検証結果

| 項目 | 結果 |
|------|------|
| `dotnet build` | ✅ 成功（0 エラー / 2 警告は既存 NuGet 警告） |
| 単一主鍵エンティティ動作 | ✅ 既存機能は変更なし（後方互換性維持） |
| 複合主鍵 YAML 構文 | ✅ 設計ベースで検証可能 |

---

## 2026-03-02（blog プロジェクト追加・Page 機能・SQL Server エンティティ拡充）

### 追加

#### 1. blog プロジェクト

Chinook とは別の独立プロジェクトとして blog プロジェクトを追加しました。

**ディレクトリ:** `projects/blog/`

| ファイル | 役割 |
|---------|------|
| `project.yaml` | プロジェクト設定（SQLite 使用） |
| `dashboard.yml` | 統計カード・グラフ定義 |
| `entities/post.yml` | Post エンティティ（ブログ記事） |
| `pages/*.yaml` | ページ定義（BlogList/BlogDetail/PostDashboard/CommentQueue） |
| `views/*.cshtml` | ページビュー |

**Post エンティティ特徴:**
- Title/Content/Author/PublishDate/Status/Views
- Status ドロップダウン（Draft/Published/Archived）
- PublishDate 範囲フィルター
- Views 数でソート可能

**ダッシュボード:**
- 統計カード：投稿数・公開数・下書き数・総閲覧数など
- グラフ：Status 別ドーナツ・月別投稿棒グラフなど

#### 2. Page 機能（ページ定義 YAML 駆動）

YAML 定義に基づくページ表示機能を実装しました。

**新規ファイル:**
| ファイル | 役割 |
|---------|------|
| `Controllers/PageController.cs` | YAML ページ定義に基づく動的表示 |
| `Models/PageDefinition.cs` | ページ定義モデル |
| `Services/PageMetadataProvider.cs` | pages/ ディレクトリから YAML 読み込み |
| `Services/ProjectViewLocationExpander.cs` | プロジェクト別ビュー場所展開 |
| `Views/Page/PageView.cshtml` | 共通ページビューテンプレート |

**ページ定義 YAML 構造:**
```yaml
page:
  title: Blog List
  layout: list    # list/form/dashboard
  entity: post
  columns: [Title, Author, PublishDate, Status]
  filters: [Status, DateRange]
```

#### 3. Chinook SQL Server 用エンティティ拡充

SQL Server 環境用のエンティティ定義を追加しました。

**ディレクトリ:** `projects/chinook/entities-sqlserver/`

| ファイル | エンティティ |
|---------|-------------|
| `customer.yml` | Customer（SQL Server 文字列連結対応） |
| `invoice.yml` | Invoice（SQL Server 文字列連結対応） |

**SQLite との差分:**
- SQL Server は文字列連結に `+` 演算子を使用
- SQLite は `||` 演算子

### 変更

#### 1. マルチプロジェクト対応強化

- `ProjectManager.cs`: blog プロジェクトスキャン対応
- `ProjectMiddleware.cs`: ページ機能との連携強化

#### 2. Program.cs

- Page 機能関連 DI 登録追加

### 検証結果

| 項目 | 結果 |
|------|------|
| `dotnet build` | ✅ 成功（0 エラー） |
| `blog/Dashboard/Index` | ✅ 200 |
| `blog/DynamicEntity/Index?entity=post` | ✅ 200 |
| `blog/Page/BlogList` | ✅ 200 |
| `chinook/DynamicEntity/Index?entity=customer` (SQL Server) | ✅ 200 |

---

## 2026-03-02（スキーマ定義表示機能）

### 追加

#### 1. エンティティ定義専用ページ（`Views/DynamicEntity/Definition.cshtml`）

Admin ユーザーが任意エンティティの YAML 定義を Web ブラウザ上で確認できる専用ページを追加しました。

**URL:** `/{project}/DynamicEntity/Definition?entity={name}`（Admin ロール必須）

**表示タブ構成:**

| タブ | 表示内容 |
|------|----------|
| Columns | 列名・型・ラベル・Identity/Searchable/Sortable/Editable・Expression・FK |
| Forms | フォームフィールド・型・Required・Hidden・Options/FK |
| Filters | フィルター種別・ラベル・Options/FK・Expression |
| Joins | JOIN 種別・テーブル・エイリアス・ON 条件 |
| Links | リンクキー・ラベル・遷移先エンティティ・Filter/Query マッピング |
| Settings | Paging（サイズ・モード・Count）・Layout（列数/順序）・Confirmation・Hooks |

基本情報カードにテーブル名・主キー・ページング設定・多言語表示名を一覧表示。

#### 2. 全エンティティ定義概要ページ（`Views/DynamicEntity/AllDefinitions.cshtml`）

プロジェクト内の全エンティティを一画面で俯瞰できるスキーマ概要ページを追加しました。

**URL:** `/{project}/DynamicEntity/AllDefinitions`（Admin ロール必須）

- 全エンティティをアルファベット順で一覧表示（Entity / DisplayName / Table / Key / Columns数 / Forms数 / Filters数 / Joins数 / Links数 / Paging / Flags）
- JOIN が存在する場合：**JOIN 一覧**サブカードを自動表示
- リンクが存在する場合：**エンティティ間リンク**サブカードを自動表示（From → To・Filter マッピング）
- 各行の「Detail →」ボタンで個別 Definition ページへ遷移

#### 3. コントローラー追加（`Controllers/DynamicEntityController.cs`）

| 追加要素 | 内容 |
|---------|------|
| `Definition` アクション | `[Authorize(Roles = "Admin")]`、`IEntityMetadataProvider.Get()` でメタデータ取得 |
| `AllDefinitions` アクション | `[Authorize(Roles = "Admin")]`、`IEntityMetadataProvider.GetAll()` で全件取得 |
| `EntityDefinitionViewModel` | `(string Entity, EntityDefinition Meta)` レコード |
| `AllDefinitionsViewModel` | `(IReadOnlyDictionary<string, EntityDefinition> Entities)` レコード |

#### 4. ナビゲーション改善

**`Views/DynamicEntity/Index.cshtml`**
- Admin ユーザー向けにタイトル横へ Definition アイコンボタンを追加

**`Views/Shared/_Layout.cshtml`**
- Admin ユーザーのサイドバーで各エンティティを `<details>/<summary>` 折りたたみサブメニューに変更
  - `List` → エンティティ一覧
  - `Definition` → 個別定義ページ（現在表示中のタブに応じてハイライト）
- Admin セクション（`Users` の上）に `Schema`（= AllDefinitions）リンクを追加

### 検証結果

| 項目 | 結果 |
|------|------|
| `dotnet build` | ✅ 成功（0 エラー） |
| `todo/DynamicEntity/Definition?entity=task` | ✅ 200 |
| `todo/DynamicEntity/Definition?entity=project` | ✅ 200 |
| `todo/DynamicEntity/AllDefinitions` | ✅ 200 |
| `chinook/DynamicEntity/AllDefinitions` | ✅ 200 |

---

## 2026-03-02（マルチプロジェクト対応・todo プロジェクト移植）

### 追加

#### 1. マルチプロジェクト基盤

複数の独立したプロジェクト（データベース・エンティティ定義・ダッシュボードを各自保持）を単一アプリで同時運用できる仕組みを実装しました。

**新規ファイル:**

| ファイル | 役割 |
|---------|------|
| `Services/ProjectManager.cs` | Singleton。`projects/` ディレクトリをスキャンし、各 `project.yaml` を検証・キャッシュ |
| `Services/ProjectInfo.cs` | 不変プロジェクト情報（Config / EntityMetadataProvider / DashboardConfigProvider） |
| `Services/ProjectScope.cs` | Scoped。現在リクエストのプロジェクトを保持。ミドルウェアが `Set()` で初期化 |
| `Services/ProjectAwareEntityMetadataProvider.cs` | `IEntityMetadataProvider` の Scoped プロキシ。`ProjectScope` から実際のプロバイダーに委譲 |
| `Services/ProjectAwareDashboardConfigProvider.cs` | `IDashboardConfigProvider` の Scoped プロキシ |
| `Services/YamlSchemaValidator.cs` | `project.yaml` を組み込みスキーマ（`project-schema.json`）で検証 |
| `Middleware/ProjectMiddleware.cs` | `{project}` ルートパラメータを解決し `ProjectScope` を初期化。未知プロジェクトは 404 |
| `Models/ProjectConfig.cs` | `project.yaml` のデシリアライズモデル |
| `Schemas/project-schema.json` | EmbeddedResource として組み込まれた JSON Schema（起動時検証に使用） |

**ルートパターン変更（`Program.cs`）:**

```
/{project}/{controller=Dashboard}/{action=Index}/{id?}
```

**ミドルウェア順序:**
```
UseRouting → UseMiddleware<ProjectMiddleware> → UseAuthentication → UseAuthorization
```

#### 2. `project.yaml` 仕様

```yaml
name: todo
displayName: TODO Project Manager
version: "1.0.0"
database:
  type: sqlite          # sqlite | sqlserver
  path: database/todo.db
features:
  multiLanguage: true
  userAuthentication: true
```

#### 3. todo プロジェクト（`projects/todo/`）

LCP からの移植。タスク管理・プロジェクト管理の 2 エンティティで構成。

| ファイル | 内容 |
|---------|------|
| `entities/task.yml` | Task エンティティ。JOIN Project、Status/Priority ドロップダウン、FK ProjectId、Paging 15 |
| `entities/project.yml` | Project エンティティ。Status ドロップダウン、links: tasks |
| `dashboard.yml` | 統計カード 7 個（Total Tasks / Pending / In Progress / Completed / Urgent / Total Projects / Active）、グラフ 4 個（Status ドーナツ・Priority 棒・Per Project 棒・Projects Status 円） |
| `database/todo.db` | Task / Project サンプルデータ入り SQLite DB |

#### 4. `EntityMetadata.cs` — imports 対応

`EntityConfigRoot` に `List<string> Imports` プロパティを追加。YAML の `imports: [...]` で共有フィールド定義をインポート可能になりました。

#### 5. 各サービスのマルチプロジェクト対応

- `UserAuthService` / `AuditLogService`：`ProjectScope` から接続文字列を取得するよう変更
- `EntityMetadataProvider`：パス引数コンストラクタを追加（ProjectManager が各プロジェクト用インスタンスを生成）
- `DashboardConfigProvider`：同上
- `DbInitializer`：全プロジェクトの DB を起動時に順次初期化

### 検証結果

| 項目 | 結果 |
|------|------|
| `dotnet build` | ✅ 成功（0 エラー） |
| `todo` プロジェクト認識 | ✅ 自動スキャン・DB 初期化 |
| `chinook` プロジェクト認識 | ✅ 既存動作維持 |
| ルート `/{project}/...` | ✅ 両プロジェクトで正常動作 |

---

## 2026-02-27（Dashboardカードリンク・グラフ・データ拡充）

### 追加

#### 1. 統計カードのジャンプリンク

各カードを `<a>` タグでラップし、クリックするとエンティティ一覧（`/DynamicEntity/Index?entity=xxx`）に遷移するようにしました。ホバー時にシャドウ＋スケールアップのアニメーションを追加。

**変更ファイル:** `Controllers/DashboardController.cs`（`EntityUrl` プロパティ追加）、`Views/Dashboard/Index.cshtml`

#### 2. Chart.js グラフ表示

CDN（Chart.js 4.4.3）を `@section Scripts` でロードし、`config/dashboard.yml` の `charts` セクション定義に基づいてグラフを動的に描画します。

| グラフ | 種別 | データ |
|--------|------|--------|
| Monthly Revenue | 折れ線 | Invoice SUM(Total) / 月別（strftime）過去24ヶ月 |
| Tracks by Genre | ドーナツ | Track COUNT JOIN Genre / ジャンル別 Top 10 |
| Top 10 Countries by Invoices | 棒グラフ | Invoice COUNT / 国別 Top 10 |
| Top 10 Artists by Albums | 棒グラフ | Album COUNT JOIN Artist / アーティスト別 Top 10 |

グラフ定義 YAML の主なフィールド:

| フィールド | 説明 |
|-----------|------|
| `type` | `bar` / `line` / `doughnut` / `pie` |
| `groupExpression` | GROUP BY に使う SQL 式（例: `strftime('%Y-%m', InvoiceDate)`） |
| `labelJoinEntity` | FK 先エンティティを JOIN してラベルを取得 |
| `colors` | `doughnut`/`pie` 用 RGBA カラーリスト |

#### 3. 統計カードの拡充

7種 → 12種に増加:

| 追加カード | 説明 |
|-----------|------|
| Genres | ジャンル数（COUNT） |
| Media Types | メディアタイプ数（COUNT） |
| Playlists | プレイリスト数（COUNT） |
| Invoice Lines | 請求明細数（COUNT） |
| Avg Invoice | 平均請求額（AVG Total） |

#### 4. `DashboardChartDefinition` モデル追加（`Models/DashboardConfig.cs`）

グラフ定義を保持するクラス。`DashboardConfig.Charts` リストに格納されます。

**主なプロパティ:**
- `Type`, `Entity`, `ValueAggregate`, `ValueColumn`
- `GroupExpression`（シンプル GROUP BY）
- `LabelJoinEntity` / `LabelJoinKey` / `LabelJoinDisplay`（FK JOIN）
- `OrderBy`, `OrderDir`, `Limit`
- `ColorBg`, `ColorBorder`（単色）/ `Colors`（複数色リスト）

#### 5. `DashboardChartViewModel` / `DashboardViewModel` 追加（`Controllers/DashboardController.cs`）

- `DashboardChartViewModel`: タイトル・種別・ラベル JSON・値 JSON・色情報
- `DashboardViewModel`: `Stats[]` + `Charts[]` をまとめた View モデル

---

## 2026-02-27（Dashboard・URLリセット修正・パンくず修正）

### 追加

#### 1. Dashboard 画面（`Controllers/DashboardController.cs`、`Views/Dashboard/Index.cshtml`）

アプリのトップページを **Dashboard** に変更しました。
YAML 設定（`config/dashboard.yml`）で定義した統計情報を DB から集計してカード形式で表示します。

**統計定義 YAML（`config/dashboard.yml`）**

```yaml
stats:
  - label: Artists
    labelI18n: { en-US: Artists, zh-CN: 艺术家, ja-JP: アーティスト }
    entity: artist
    aggregate: count       # count / sum / avg
    icon: "🎵"
    color: badge-primary

  - label: Total Revenue
    entity: invoice
    aggregate: sum
    column: Total          # sum / avg の場合に必須
    icon: "💰"
    color: badge-success
```

| フィールド | 説明 |
|-----------|------|
| `entity` | entities.yml で定義したエンティティキー |
| `aggregate` | `count` / `sum` / `avg` |
| `column` | `sum` / `avg` の対象カラム |
| `filter` | WHERE 句（任意）|
| `icon` | アイコン絵文字 |
| `color` | DaisyUI バッジカラークラス |

**新規ファイル:**
- `Models/DashboardConfig.cs`（`DashboardConfig` / `DashboardStatDefinition` モデル）
- `Services/DashboardConfigProvider.cs`（`IDashboardConfigProvider` + 実装）
- `Controllers/DashboardController.cs`（集計クエリ実行）
- `Views/Dashboard/Index.cshtml`（統計カード表示）
- `config/dashboard.yml`（デフォルト統計定義）

#### 2. サイドバーに Dashboard リンク追加（`Views/Shared/_Layout.cshtml`）

サイドメニューの最上部に Dashboard リンクを追加しました。
現在 Dashboard 画面のとき `active` スタイルが適用されます。

### 修正

#### 1. URL リセットバグ（"New Page" ボタン）

**原因**: `Index.cshtml` の "New Page" ボタンの `returnUrl` は `entity` だけのシンプルな URL で、HTMX によるフィルター更新後も更新されなかった。

**修正**: "New Page" ボタンを `Index.cshtml` から `_List.cshtml` に移動しました。
`_List.cshtml` は HTMX によって毎回再描画されるため、常に最新の `currentReturnUrl`（検索・ソート・フィルター状態を含む）を使用します。

#### 2. 保存後 URL リセットバグ（Create / Edit POST）

**原因**: ページモードで保存成功後に `return RedirectToAction(nameof(Index), new { entity })` と状態なしの基本 URL にリダイレクトしていた。

**修正**: `returnUrl` が存在する場合はそこにリダイレクト、なければ基本 Index にフォールバック。

```csharp
// before
return RedirectToAction(nameof(Index), new { entity });

// after
return Redirect(returnUrl ?? Url.Action(nameof(Index), new { entity })!);
```

影響: `DynamicEntityController.Create` (POST) / `DynamicEntityController.Edit` (POST)

#### 3. Cancel ボタン URL リセット（`_Form.cshtml`）

ページモードの Cancel ボタンが `returnUrl` を無視して基本 Index に遷移していた問題を修正しました。
`Context.Request.Query["returnUrl"]` が存在する場合はそこに遷移します。

#### 4. パンくずリスト重複バグ（`FormPage.cshtml`）

**原因**: `BuildBreadcrumbChain(returnUrl)` が既にエンティティのパンくずを生成しているにもかかわらず、`FormPage.cshtml` でエンティティリンクをハードコードしていたため「Customer / Customer / Edit」のような重複が発生していた。

**修正**: パンくずチェーンが存在する場合（returnUrl あり）はハードコードのリンクを省略し、パンくずチェーンが空の場合（直接ナビゲーション時）のみ表示するよう変更。

```razor
@if (breadcrumbs.Count == 0)
{
    <li><a href="@Url.Action("Index", ...)">@Model.Meta.GetDisplayName()</a></li>
}
```

#### 5. パンくず "Home" → "Dashboard" への変更

`Index.cshtml` / `FormPage.cshtml` のパンくず最上位を `Home` から `Dashboard` に変更しました。

### 変更

#### デフォルトルートを Dashboard に変更（`Program.cs`）

```csharp
// before
app.MapControllerRoute(..., pattern: "{controller=DynamicEntity}/{action=Index}/{id?}");

// after
app.MapControllerRoute(..., pattern: "{controller=Dashboard}/{action=Index}/{id?}");
```

---

## 2026-02-27（SQL Server対応・全Chinook YAML・UXバグ修正・フック＆確認ダイアログ）

### 追加

#### 1. SQL Server 方言サポート（`Services/Dialect/`）

データベースプロバイダーごとにページング SQL を切り替える `ISqlDialect` 抽象を導入しました。

| ファイル | 説明 |
|----------|------|
| `Services/Dialect/ISqlDialect.cs` | `AppendNumberedPagination` / `AppendKeysetPagination` / `ConcatOperator` インターフェース |
| `Services/Dialect/SqliteDialect.cs` | `LIMIT @PageSize OFFSET @Offset` による実装 |
| `Services/Dialect/SqlServerDialect.cs` | `ORDER BY ... OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY`。ORDER BY が未指定の場合は主キーで自動補完 |

`Program.cs` の `DatabaseProvider` 設定（`"sqlite"` / `"sqlserver"`）に応じて DI に登録されます。

```json
// appsettings.json
{
  "DatabaseProvider": "sqlite",           // "sqlserver" に変えるだけで切り替え
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chinook.db",
    "SqlServer": "Server=localhost;Database=Chinook;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

#### 2. SQL Server 用 DB 初期化（`Data/DbInitializer.cs`）

`DatabaseProvider` が `sqlserver` の場合、SQLite の Chinook ダウンロードをスキップし、SQL Server 向け DDL（`IF NOT EXISTS` / `INT IDENTITY(1,1)` 構文）で `AppUser` / `AuditLog` テーブルを作成します。

#### 3. EntityMetadataProvider — プロバイダー別 YAML ディレクトリ（`Services/EntityMetadataProvider.cs`）

- `sqlserver` 時: `config/entities-sqlserver/` を**先に**読み込み、不足エンティティを `config/entities/` で補完
- `sqlite` 時: `config/entities/` のみ読み込み（従来通り）

差分だけを `entities-sqlserver/` に置けばよいため、重複が最小化されます。

#### 4. Chinook 全テーブル YAML（`config/entities/`）

| ファイル | テーブル | 追加内容 |
|----------|----------|----------|
| `mediatype.yml` | MediaType | 一覧・新規・編集・Track へのリンク |
| `playlist.yml` | Playlist | 一覧・新規・編集 |
| `invoiceline.yml` | InvoiceLine | Invoice / Track FK ピッカー対応、Unit Price 範囲フィルター |

#### 5. SQL Server 向け差分 YAML（`config/entities-sqlserver/`）

SQLite と異なる点（文字列連結 `||` → `+`）のみを上書きします。

| ファイル | 変更箇所 |
|----------|----------|
| `customer.yml` | `SupportRepName.expression` を `e.LastName + ', ' + e.FirstName` に変更 |
| `invoice.yml` | `CustomerName.expression` を `c.LastName + ', ' + c.FirstName` に変更 |

#### 6. 確認ダイアログ・前処理・後処理フック（`Services/Hooks/`、`Models/EntityMetadata.cs`）

YAML の `confirmation` / `hooks` セクションで、作成・更新操作に確認ダイアログと前後処理を追加できます（詳細は `docs/confirmation-and-hooks.md` 参照）。

**新規ファイル:**
- `Services/Hooks/EntityHookContext.cs`（`CrudOperation` / `HookResult` / `EntityHookContext`）
- `Services/Hooks/IEntityHook.cs`（フックインターフェース）
- `Services/Hooks/IEntityHookRegistry.cs`（レジストリインターフェース）
- `Services/Hooks/EntityHookRegistry.cs`（DI 経由の名前→実装マップ）
- `Services/Hooks/SampleHooks.cs`（4 種のサンプル実装）

#### 7. リンクラベルの多言語対応（`Models/EntityMetadata.cs`）

`EntityLinkDefinition` に `LabelI18n` プロパティと `GetLabel()` メソッドを追加。
`_List.cshtml` のリンク表示を `link.Label` から `link.GetLabel()` に変更しました。

```yaml
links:
  invoices:
    label: Invoices
    labelI18n: { en-US: Invoices, zh-CN: 发票, ja-JP: 請求書 }
    targetEntity: invoice
    filter: { CustomerId: CustomerId }
```

#### 8. フォームフィールドパーシャル抽出（`Views/DynamicEntity/_FormField.cshtml`）

ページモードとモーダルモードで重複していたフィールド描画 HTML を `_FormField.cshtml` パーシャルビューに切り出し、両モードから `Html.PartialAsync` で参照するよう変更しました。

### 修正

#### 1. フォームフィールド消去バグ（バリデーション・フックエラー時）

`DynamicFormViewModel` に `SubmittedValues` （`Dictionary<string, string?>`）パラメータを追加。バリデーションエラーやフックキャンセル時にも送信値でフォームを再描画するようにしました。

**修正前**: エラー時に `item = null` で VM を組み立てていたためフィールドが空になる。
**修正後**: 送信フォーム値 `form` を `SubmittedValues` として渡し、フィールド値を復元。

影響ファイル: `Controllers/DynamicEntityController.cs`、`Views/DynamicEntity/_Form.cshtml`、`Views/DynamicEntity/_FormField.cshtml`

#### 2. HTMX 確認ダイアログと前処理フックの競合

**根本原因**: HTMX フォームの `submit` イベントで `evt.preventDefault()` を呼んでも HTMX は XHR を送信してしまう。そのため確認ダイアログ表示中にサーバーへリクエストが送られ、フックエラーと確認ダイアログが同時に発生していた。

**修正方法**:
- モーダルモードの `<form>` に `hx-confirm="@(confirmMsg ?? "")"` 属性を付与
- `_Layout.cshtml` の `htmx:confirm` イベントハンドラでカスタムダイアログを表示し、OK 後に `evt.detail.issueRequest(true)` を呼ぶ
- `hx-confirm=""` の場合（確認なし）はダイアログをスキップして即座にリクエスト発行

```javascript
document.body.addEventListener('htmx:confirm', function (evt) {
    var msg = evt.detail.question;
    if (!msg) {
        evt.preventDefault();
        evt.detail.issueRequest(true);  // 確認なし→即リクエスト
        return;
    }
    evt.preventDefault();
    showConfirmDialog(msg, function () { evt.detail.issueRequest(true); });
});
```

#### 3. Razor ビルドエラー修正（`_Form.cshtml`）

`else {}` ブロック内に誤って `@{}` を入れ子にした問題（`RZ1010`）と、`<form>` タグの属性エリアで `@Html.Raw()` を使った問題（`RZ1031`）を修正しました。

### 検証結果

1. `dotnet build` 成功（0 エラー）
2. SQLite モードでの動作変更なし（LIMIT / OFFSET は従来通り）
3. `entities-sqlserver/` の YAML が SQLite フォールバックと正しくマージされることを確認（設計ベース）
4. フォームフィールドがバリデーションエラー後も送信値を保持することを確認（設計ベース）

---

## 2026-02-27（UI改善：パンくず多段化・Newボタン位置変更・エンティティ選択ピッカー）

### 追加

#### 1. パンくずの多段チェーン対応（`Controllers/DynamicEntityController.cs`）
- `BuildBreadcrumbChain(returnUrl)` ヘルパーメソッドを追加
- `returnUrl` クエリパラメータが入れ子になっていることを利用し、再帰的に遡って全遷移履歴を抽出
- 例: Customer → Invoice → Track と遷移すると `Home / Customer / Invoice / Track（現在）` が自動生成される
- `DynamicListViewModel` に `BreadcrumbChain` プロパティを追加
- `DynamicFormViewModel` に `BreadcrumbChain` プロパティを追加
- `CreatePage`・`EditPage` アクションに `returnUrl` パラメータを追加し、フォームページでもパンくずを表示

#### 2. パンくずをタイトルの上方に配置（`Views/DynamicEntity/Index.cshtml`、`Views/DynamicEntity/FormPage.cshtml`）
- パンくず `<nav>` を h1 タイトルより前に移動
- `Model.BreadcrumbChain` を使って全遷移履歴をパンくずリンクとして表示
- `Index.cshtml` の「New Page」ボタンに `returnUrl` を追加（現在のエンティティ一覧URLを引き渡し）
- `FormPage.cshtml` も同様の多段パンくず表示に刷新

#### 3. Newボタンをタイトルの左側に配置（`Views/DynamicEntity/Index.cshtml`）
- 従来: タイトル左寄せ・ボタン右寄せ（`justify-between`）
- 変更後: `[New] [New Page] [h1 タイトル]` の水平並び（`flex items-center gap-3`）

#### 4. エンティティ選択ピッカー（新機能）

**モデル（`Models/EntityMetadata.cs`）**
- `ForeignKeyDefinition` に `Picker: bool`（単一選択）・`MultiPicker: bool`（複数選択）プロパティを追加

**コントローラー（`Controllers/DynamicEntityController.cs`）**
- `PickerList` アクションを追加：HTMX からピッカーテーブル用パーシャルを返す
- `PickerViewModel` レコードを追加（Entity, Meta, Items, TargetField, DisplayColumn, Multi, Search, Page, PageSize, HasMore）
- `BreadcrumbItem` レコードを追加

**ビュー（`Views/DynamicEntity/_Form.cshtml`）**
- `foreignKey.picker: true` の場合、ドロップダウンの代わりに「テキスト表示入力 + Browse ボタン + hidden input」を描画
- `foreignKey.multiPicker: true` の場合、チップ（バッジ）形式で複数選択された値を表示。「+ Browse」で追加、各チップの✕で削除

**ビュー（`Views/DynamicEntity/_FilterControl.cshtml`）**
- `type: entity-picker`：単一選択ピッカーフィルター（Browse ボタン + hidden input + 選択済み表示 + クリアボタン）
- `type: entity-multi-picker`：複数選択ピッカーフィルター（チップ表示 + Browse ボタン + Clear ボタン）

**新規ファイル（`Views/DynamicEntity/_Picker.cshtml`）**
- ピッカーモーダルのコンテンツ（テーブル + ページング）を描画するパーシャルビュー
- テーブル行クリックで選択（行 `data-picker-id` / `data-picker-label` 属性から JS が値を取得）

**レイアウト（`Views/Shared/_Layout.cshtml`）**
- 全ページ共通のエンティティ選択ピッカーモーダル `#entity-picker-modal` を追加
- ピッカー操作 JS 関数群を追加:
  - `openEntityPicker(btn)` — Browse ボタンから設定を読み取ってモーダルを開く
  - `loadPickerContent(search, page)` — HTMX で `PickerList` を呼んでテーブルを更新
  - `entityPickerSearch(value)` — デバウンス300ms 付きインクリメンタル検索
  - `loadPickerPage(page)` — ページング
  - `pickerSelectFromRow(row)` — 行クリック時の単一/複数選択処理
  - `removePickerChip(fieldName, id, el)` — チップ削除
  - `clearPickerValue(fieldName)` — フォームピッカーのクリア
  - `clearPickerFilterValue(fieldName)` — フィルターピッカーのクリア

**ビューインポート（`Views/_ViewImports.cshtml`）**
- `@using NetYamlForge.Controllers` を追加（`BreadcrumbItem` 型をビューで直接参照できるように）

#### 5. `_List.cshtml` の「Edit Page」リンクに `returnUrl` を追加
- `EditPage` リンクに `returnUrl = currentReturnUrl` を追加し、フォームページ側でもパンくずが正しく構築されるように対応

#### 6. デモ用 YAML 設定を更新（`config/entities.yml`）
- `album.forms.ArtistId` — `foreignKey.picker: true` を追加（ピッカー単一選択デモ）
- `invoice.forms.CustomerId` — `foreignKey.picker: true` を追加（多数レコードからのピッカー選択デモ）
- `album.filters.ArtistId` — `type: dropdown` → `type: entity-picker` に変更（フィルターピッカーデモ）

### 検証結果
1. `dotnet build` 成功（0 エラー / 8 警告はすべて既存の nullable 注釈警告）
2. Customer → Invoice → Track の3段遷移でパンくずが `Home / Customer / Invoice / Track` と正しく表示されることを確認（設計ベース）
3. Album フォームの ArtistId フィールドでピッカーモーダルが開き、選択値が hidden input へセットされる動作を設計確認
4. `_ViewImports.cshtml` への `using` 追加でビュー内の `BreadcrumbItem` 型参照エラーが解消

---

## 2026-02-27（エンティティ間ナビゲーション・状態復元・面パン強化）

### 追加

#### 1. `EntityLinkDefinition` に `filter` フィールドを追加（`Models/EntityMetadata.cs`）
- YAML の `links` セクションで `filter: { targetParam: sourceColumn }` を定義できるようにした
- `targetParam`：遷移先エンティティに渡すクエリパラメータ名
- `sourceColumn`：現在の行から取得するカラム名
- `filter` なし → エンティティレベルリンク（一覧上部に表示）
- `filter` あり → 行単位リンク（アクション列にボタン表示、行のIDを付与して遷移）

#### 2. 行単位ナビゲーションリンク（`Views/DynamicEntity/_List.cshtml`）
- アクション列に `filter` が設定されたリンクを行ごとにボタン表示（`btn-secondary btn-outline`）
- ボタン押下時、`returnUrl` に現在ページの全状態（検索・ソート・フィルタ・ページ・ページサイズ）を含めて遷移
- `currentStateUrl` を `Context.Request.QueryString` から構築することで HTMX 経由のフィルタ値も正確に保持
- エンティティレベルリンク（`filter` なし）は上部セクションに残す構成に変更

#### 3. 遷移元状態の復元とパンくずナビ（`Views/DynamicEntity/Index.cshtml`）
- `returnUrl` を `filter-form` の hidden input に追加し、HTMX 部分更新後も引き継ぎ
- `list-container` の `hx-get` に `hx-include="#filter-form"` を追加して初期ロード時もフィルタ状態を伝播
- 面パン（breadcrumb）に遷移元エンティティ名のリンクを追加（`ReturnDisplayName` → `ReturnUrl`）
- `returnUrl` が存在する場合「← 前の〇〇に戻る」ボタンを表示。状態を維持したまま一覧に戻れる

#### 4. `CreateForm` に `returnUrl` 引き継ぎ（`Views/DynamicEntity/Index.cshtml`、`Views/DynamicEntity/_Form.cshtml`）
- `New` ボタンの `hx-get` に `returnUrl` を追加し、モーダル経由の新規作成後も遷移元を保持
- ページモードフォームの hidden input に `returnUrl` を追加

#### 5. コントローラ側の `returnUrl` 対応（`Controllers/DynamicEntityController.cs`）
- `Index`・`ListPartial`・`Create`・`Edit`・`Delete` の各アクションに `returnUrl` パラメータを追加
- `DynamicListViewModel` に `ReturnUrl`・`ReturnEntity`・`ReturnDisplayName` フィールドを追加
- `CreateListViewModel` ヘルパーメソッドを新設し、`returnUrl` から遷移元エンティティを解析して表示名を自動解決
- `ExtractEntityFromReturnUrl` メソッドを新設。`returnUrl` のクエリ文字列から `entity` パラメータを抽出

#### 6. `customer.yml` — `invoices` リンクに行単位フィルタ追加
- `links.invoices.filter: { CustomerId: CustomerId }` を追加
- 顧客行の「Invoices →」ボタンで、該当顧客の請求書一覧に絞り込み遷移できるようになった

#### 7. `invoice.yml` — `CustomerId` フィルター追加
- `filters.CustomerId`（type: dropdown、expression: `Invoice.CustomerId`、foreignKey: customer）を追加
- `layout.filters.order` に `CustomerId` を先頭追加
- 顧客一覧から遷移した際、顧客名ドロップダウンが自動選択された状態でフィルタが適用される

### 修正

#### 1. `_List.cshtml` の Razor 構文エラー修正
- `@foreach` コードブロック内に誤って `@{ }` ネストが存在した問題を修正（`RZ1010` エラー）
- `@{` と閉じ `}` を除去し、C# 文を直接ブロック内に配置

### 検証結果
1. `dotnet build` 成功（0 エラー / 8 警告はすべて既存のnullable注釈警告）
2. 顧客一覧 → 「Invoices →」行リンク → 請求書一覧（該当顧客で絞り込み済み）の動作を確認
3. 面パン「Customer」リンク → 元のページ・フィルタ状態が完全復元されることを確認

---

## 2026-02-27

### Added
1. Added detailed Japanese file-level comments across custom `.cs` and `.cshtml` files.
2. Added detailed Japanese method-level comments in core backend files (`DynamicCrudRepository`, `DynamicEntityController`, `UserAuthService`).

### Fixed
1. Fixed Razor build issue caused by BOM before `@model` in `Views/Shared/Error.cshtml`.

### Added
1. Optional `count=false` query mode for large datasets to skip `COUNT(*)`.
2. Keyset pagination support via cursor for high-volume list views.

### Added
1. Enforced push workflow note: update modification records before every push.

### Added
1. Added an `isPublic` flag on entity metadata so each YAML definition can opt into appearing in the authenticated sidebar menu.

### Changed
1. Refactored dynamic form item access in [`Views/DynamicEntity/_Form.cshtml`](/Users/tt/Desktop/ws/ccc/NetYamlForge/Views/DynamicEntity/_Form.cshtml).
2. Replaced repeated runtime cast/try-catch field reads with safe dictionary access (`TryGetValue`).
3. Removed nullable-related build warnings from form rendering path.
4. Sidebar navigation now waits for login, iterates every YAML definition, and shows a `Public`/`Private` badge plus active-state styling based on the new `isPublic` metadata.
5. `Views/DynamicEntity/Index.cshtml` now renders breadcrumb navigation to keep the current entity context explicit.

### Verification
1. `dotnet build` succeeded with `0 warning / 0 error`.
