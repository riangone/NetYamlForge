# NetYamlForge 実装変更詳細（日本語）

## 1. 概要
このドキュメントは、現時点までに本プロジェクトへ適用した実装変更を整理したものです。
対象プロジェクト:
`/Users/tt/Desktop/ws/ccc/NetYamlForge`

## 2. プロジェクト基盤
1. .NET 10 MVC プロジェクトを生成し、動作可能な構成へ再編成。
2. 動的CRUD用のコントローラ、サービス、モデル、ビューを実体化。
3. HTMXによる部分更新フローを実装。
4. UIにDaisyUIを適用。

主なファイル:
- `Program.cs`
- `Controllers/DynamicEntityController.cs`
- `Services/DynamicCrudRepository.cs`
- `Models/EntityMetadata.cs`

## 3. Filter/Form機能拡張
### 3.1 Filter対応
対応タイプ:
1. `dropdown`
2. `checkbox`（複数値をIN検索）
3. `multi-select`（複数値をIN検索）
4. `range`（`*_min` / `*_max`）
5. `date-range`（`*_from` / `*_to`）

### 3.2 Form対応
対応タイプ:
1. `text`
2. `email`
3. `textarea`
4. `int`
5. `decimal`
6. `double`
7. `bool`
8. `date`
9. `datetime`
10. `select`（`options`）
11. `radio`（`options`）

主なファイル:
- `Views/DynamicEntity/_FilterControl.cshtml`
- `Views/DynamicEntity/_Form.cshtml`
- `Views/DynamicEntity/_List.cshtml`
- `Services/ValueConverter.cs`
- `Services/DynamicCrudRepository.cs`

## 4. Form表示モードの二系統化
1. Create/Edit のモーダル表示対応。
2. Create/Edit のページ遷移モード対応。
3. モーダル保存成功時は一覧を更新し、モーダルを閉じる。
4. バリデーションエラー時はモーダル内に再表示。

追加アクション:
1. `CreateForm` / `EditForm`（モーダル）
2. `CreatePage` / `EditPage`（ページ遷移）

主なファイル:
- `Controllers/DynamicEntityController.cs`
- `Views/DynamicEntity/Index.cshtml`
- `Views/DynamicEntity/FormPage.cshtml`
- `Views/DynamicEntity/_Form.cshtml`

## 5. Chinook DB導入
1. デフォルト接続先を `chinook.db` に変更。
2. 起動時、DBが存在しない場合はChinook SQLiteを自動ダウンロード。
3. Chinookテーブル中心で動的CRUDを検証。

主なファイル:
- `Data/DbInitializer.cs`
- `appsettings.json`

## 6. 認証とユーザー管理
1. Cookie認証を追加。
2. `AppUser` テーブルを追加。
3. `AdminOnly` ポリシーを追加。
4. ログイン、ログアウト、アクセス拒否ページを追加。
5. ユーザー管理画面（一覧、新規、編集）を追加。
6. `DynamicEntityController` を認証必須化。

初期管理者:
1. UserName: `admin`
2. Password: `Admin@123`

主なファイル:
- `Controllers/AccountController.cs`
- `Controllers/UsersController.cs`
- `Models/Auth/AppUser.cs`
- `Services/Auth/UserAuthService.cs`
- `Views/Account/Login.cshtml`
- `Views/Users/Index.cshtml`
- `Views/Users/Edit.cshtml`

## 7. 多言語対応
対応言語:
1. `en-US`
2. `zh-CN`
3. `ja-JP`

実装内容:
1. RequestLocalization有効化。
2. 言語切替コントローラ追加。
3. Layoutに言語切替UI追加。
4. 共通文言をRESX管理。

主なファイル:
- `Program.cs`
- `Controllers/LocalizationController.cs`
- `Views/Shared/_Layout.cshtml`
- `Localization/SharedResource.cs`
- `Resources/Localization.SharedResource.en-US.resx`
- `Resources/Localization.SharedResource.zh-CN.resx`
- `Resources/Localization.SharedResource.ja-JP.resx`

## 8. ログ強化
1. Serilog導入。
2. コンソールログと日次ローテーションファイルログを有効化。
3. HTTPリクエストログを有効化。
4. リポジトリ内にSQL実行ログを追加。
5. DB監査ログ `AuditLog` を追加。
6. 認証、ユーザー管理、CRUD操作を監査ログへ記録。

主なファイル:
- `NetYamlForge.csproj`
- `Program.cs`
- `Services/DynamicCrudRepository.cs`
- `Services/Auth/AuditLogService.cs`

## 9. ページ単位YAML化とYAML主導UI
### 9.1 ページごとYAML
`config/entities/*.yml` の分割構成へ変更。

作成済みファイル:
1. `config/entities/customer.yml`
2. `config/entities/employee.yml`
3. `config/entities/artist.yml`
4. `config/entities/album.yml`
5. `config/entities/genre.yml`
6. `config/entities/track.yml`
7. `config/entities/invoice.yml`

### 9.2 YAML内多言語
新規対応:
1. `displayNameI18n`
2. `labelI18n`（columns/forms/filters）

### 9.3 YAML内レイアウト
新規対応:
1. `layout.forms.columns`
2. `layout.forms.order`
3. `layout.filters.columns`
4. `layout.filters.order`

モデルと読み込み拡張:
- `Models/EntityMetadata.cs`
- `Services/EntityMetadataProvider.cs`

ビュー反映:
- `Views/DynamicEntity/Index.cshtml`
- `Views/DynamicEntity/_Form.cshtml`
- `Views/DynamicEntity/_FilterControl.cshtml`
- `Views/DynamicEntity/_List.cshtml`
- `Views/DynamicEntity/FormPage.cshtml`

## 10. 不具合修正履歴
### 10.1 ログイン後 NullReferenceException
事象:
- `_FilterControl` 内で外部キー候補を `dict["Id"]` 前提で参照。
- Chinookの参照先では主キー名が `Id` 以外のテーブルがあるため例外発生。

対応:
1. `GetAllForEntityAsync` で主キーを `AS Id` として返却。
2. `_FilterControl` 側にnullガードを追加。

対象:
- `Services/DynamicCrudRepository.cs`
- `Views/DynamicEntity/_FilterControl.cshtml`

### 10.2 分割YAMLパースエラー
事象:
- `expression` 内のシングルクォートを含む値がYAML解釈で失敗。

対応:
1. 対象 `expression` をダブルクォートで囲って修正。

対象:
- `config/entities/customer.yml`
- `config/entities/invoice.yml`

## 11. 現在の確認状況
1. 最新ビルドは成功。
2. ログイン後の主要ページ確認:
   - `customer`: 200
   - `album`: 200
   - `track`: 200
   - `invoice`: 200
3. 言語切替後の表示確認:
   - `ja-JP` で 200
4. ログ出力確認:
   - `logs/app-YYYYMMDD.log`
   - `AuditLog` テーブル記録

## 13. 追加改善履歴（最新）
### 13.1 ページネーションリンク数の最適化
1. ページ番号リンクを最大5件表示へ変更。
2. 現在ページ中心のウィンドウ表示に変更。

対象:
- `Views/DynamicEntity/_List.cshtml`

### 13.2 レイアウト再設計（左オーバーレイメニュー）
1. ヘッダーを簡素化。
2. ページ一覧を左サイドの開閉メニューへ移動。
3. メニューはオーバーレイ方式で、本文幅に影響しない設計へ変更。
4. 開閉操作:
   - ヘッダーのハンバーガーで開く
   - メニュー内の閉じるボタンで閉じる
   - オーバーレイクリックで閉じる

対象:
- `Views/Shared/_Layout.cshtml`

### 13.3 右側ナビUI調整（DaisyUI参考）
1. 右側を「ドロップダウン中心」構成へ再設計。
2. 検索入力は削除。
3. 言語切替をテキストではなくアイコン（国旗）ボタンへ変更。

対象:
- `Views/Shared/_Layout.cshtml`

### 13.4 CRUDコア改善（安全性・共通化・整合性）
1. SQL安全化:
   - メタデータ検証（table/key/column/join/expression/filter）を追加。
   - 危険トークンを拒否。
   - 識別子・式を許可制に制限。
2. クエリ構築共通化:
   - `GetAllAsync` / `CountAsync` の重複ロジックを統合。
   - `BuildFromClause` / `BuildWhere` / `AppendWhere` を追加。
3. 取引整合性:
   - CRUD + Audit を同一トランザクションで実行。
   - `Insert/Update/Delete` に `IDbTransaction` 対応追加。

対象:
- `Services/DynamicCrudRepository.cs`
- `Controllers/DynamicEntityController.cs`
- `Services/Auth/IAuditLogService.cs`
- `Services/Auth/AuditLogService.cs`

### 13.5 Users管理のトランザクション化
1. `UsersController` の Create/Edit で User更新とAudit記録を同一トランザクション化。
2. `IUserAuthService` と `UserAuthService` を接続/トランザクション受け取り対応に拡張。

対象:
- `Controllers/UsersController.cs`
- `Services/Auth/IUserAuthService.cs`
- `Services/Auth/UserAuthService.cs`

### 13.6 認証可用性改善
1. `AccountController` で監査ログ書き込み失敗がログイン/ログアウト成功を阻害しないよう修正。
2. 監査失敗時は警告ログのみ記録して処理継続。

対象:
- `Controllers/AccountController.cs`

## 15. UI改善（パンくず多段化・ボタン位置・エンティティ選択ピッカー）

### 15.1 パンくず多段チェーン

ページ遷移のたびに `returnUrl` クエリパラメータが入れ子になる構造を活用し、コントローラー側で `BuildBreadcrumbChain()` が再帰的に遡ることで全遷移履歴を `BreadcrumbItem` リストとして生成します。

```
Customer 一覧 → Invoice 一覧（returnUrl=Customer） → Track 一覧（returnUrl=Invoice の URL）
↓ パンくず表示
Home / Customer / Invoice / Track（現在）
```

対象ファイル:
- `Controllers/DynamicEntityController.cs`（`BuildBreadcrumbChain`、`BreadcrumbItem`、`BreadcrumbChain` プロパティ追加）
- `Views/DynamicEntity/Index.cshtml`（パンくずをタイトル上方に移動）
- `Views/DynamicEntity/FormPage.cshtml`（多段パンくず対応）
- `Views/DynamicEntity/_List.cshtml`（EditPage リンクへ `returnUrl` 引き渡し）
- `Views/_ViewImports.cshtml`（`using NetYamlForge.Controllers` 追加）

### 15.2 Newボタンをタイトル左側へ配置

```
変更前: [                タイトル ] [ New ] [ New Page ]
変更後: [ New ] [ New Page ] [ タイトル              ]
```

対象ファイル:
- `Views/DynamicEntity/Index.cshtml`

### 15.3 エンティティ選択ピッカー

フォームやフィルターの外部キー項目で、ドロップダウンの代わりに別エンティティの一覧モーダルを開いて行を選択できます。

**YAMLによる設定方法:**

```yaml
# フォームフィールド — 単一選択
ArtistId:
  type: int
  foreignKey:
    entity: artist
    displayColumn: Name
    picker: true        # ドロップダウン→ピッカーモーダルへ

# フォームフィールド — 複数選択（カンマ区切りで保存）
Tags:
  type: string
  foreignKey:
    entity: tag
    displayColumn: Name
    multiPicker: true

# フィルター — 単一ピッカー
ArtistId:
  type: entity-picker
  foreignKey:
    entity: artist
    displayColumn: Name

# フィルター — 複数ピッカー
GenreId:
  type: entity-multi-picker
  foreignKey:
    entity: genre
    displayColumn: Name
```

**動作フロー:**
1. フォーム/フィルターの「Browse...」ボタンをクリック
2. 全ページ共通の `#entity-picker-modal` が開く（DaisyUI dialog）
3. HTMX が `GET /DynamicEntity/PickerList?entity=...&search=...&page=...` を呼び出し、テーブルを表示
4. 検索ボックスへの入力でインクリメンタル検索（デバウンス 300ms）
5. テーブル行クリックで選択
   - 単一選択: 値をセットしてモーダルを閉じる
   - 複数選択: チップとして追加。モーダルは「Done」ボタンで閉じる
6. 選択済みチップの「✕」で個別削除可能

**追加ファイル:**
- `Views/DynamicEntity/_Picker.cshtml`（ピッカー用テーブル + ページングパーシャル）

**変更ファイル:**
- `Models/EntityMetadata.cs`（`ForeignKeyDefinition.Picker` / `MultiPicker` 追加）
- `Controllers/DynamicEntityController.cs`（`PickerList` アクション、`PickerViewModel` 追加）
- `Views/DynamicEntity/_Form.cshtml`（`picker`/`multiPicker` 分岐追加）
- `Views/DynamicEntity/_FilterControl.cshtml`（`entity-picker`/`entity-multi-picker` タイプ追加）
- `Views/Shared/_Layout.cshtml`（ピッカーモーダル HTML + JS 関数群追加）

## 12. YAML定義テンプレート
```yaml
entities:
  entity_key:
    table: TableName
    key: PrimaryKey
    displayName: Entity Name
    displayNameI18n:
      en-US: Entity Name
      zh-CN: 实体名
      ja-JP: エンティティ名
    softDelete: false
    paging:
      pageSize: 10
      mode: numbered
    layout:
      forms:
        columns: 2
        order: [Field1, Field2]
      filters:
        columns: 3
        order: [Filter1, Filter2]
    joins: []
    columns:
      Field1:
        type: string
        label: Field 1
        labelI18n:
          en-US: Field 1
          zh-CN: 字段1
          ja-JP: 項目1
        searchable: true
        sortable: true
    forms:
      Field1:
        type: string
        label: Field 1
        labelI18n:
          en-US: Field 1
          zh-CN: 字段1
          ja-JP: 項目1
        editable: true
    filters:
      Field1:
        type: dropdown
        label: Field 1
        labelI18n:
          en-US: Field 1
          zh-CN: 字段1
          ja-JP: 項目1
        options: [A, B, C]
```

## 14. 運用ルール（更新）
1. 今後、**push を行う前に必ず修改记录（`docs/CHANGELOG.md`）を更新する**。
2. 変更内容、影響範囲、検証結果（最低1つ）を記録してから push する。

---

## 16. 確認ダイアログ・前処理・後処理フック

詳細は `docs/confirmation-and-hooks.md` を参照してください。

### 16.1 確認ダイアログ（`ConfirmationDefinition`）

```yaml
customer:
  confirmation:
    create: "新しい顧客を登録してよろしいですか？"
    update: "顧客情報を更新してよろしいですか？"
```

- **ページモード**: `submit` イベント（キャプチャフェーズ）で `data-confirm-msg` 属性を検出し、DaisyUI モーダルを表示
- **モーダルモード（HTMX）**: `hx-confirm` 属性 + `htmx:confirm` イベントハンドラ経由。空文字の場合は確認なしで即リクエスト

### 16.2 フックシステム（`Services/Hooks/`）

```
Services/Hooks/
├── EntityHookContext.cs   # コンテキスト（Entity / Operation / Values / UserName / Data）
├── IEntityHook.cs         # インターフェース（BeforeAsync / AfterAsync）
├── IEntityHookRegistry.cs # 名前→実装ルックアップ
├── EntityHookRegistry.cs  # DI 経由の IEnumerable<IEntityHook> から辞書を構築
└── SampleHooks.cs         # 4 種のサンプル実装
```

**フック登録（`Program.cs`）:**
```csharp
builder.Services.AddSingleton<IEntityHook, CustomerEmailDomainHook>();
builder.Services.AddSingleton<IEntityHook, CustomerNameNormalizeHook>();
builder.Services.AddSingleton<IEntityHook, InvoiceMinimumTotalHook>();
builder.Services.AddSingleton<IEntityHook, ConsoleLogAfterHook>();
builder.Services.AddSingleton<IEntityHookRegistry, EntityHookRegistry>();
```

**処理フロー:** バリデーション → BeforeAsync（中断可） → DB 書き込み + AuditLog → AfterAsync → コミット

---

## 17. SQL Server サポート

### 17.1 データベースプロバイダー設定

`appsettings.json` の `DatabaseProvider` を変更するだけで切り替えられます。

```json
{
  "DatabaseProvider": "sqlite",   // または "sqlserver"
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chinook.db",
    "SqlServer": "Server=...;Database=Chinook;TrustServerCertificate=True;"
  }
}
```

### 17.2 SQL 方言抽象（`Services/Dialect/`）

| クラス | ページング構文 | ConcatOperator |
|--------|---------------|----------------|
| `SqliteDialect` | `LIMIT @PageSize OFFSET @Offset` | `\|\|` |
| `SqlServerDialect` | `ORDER BY ... OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY` | `+` |

SQL Server では ORDER BY が必須のため、ソート未指定時は主キーでフォールバックします。

### 17.3 YAML ディレクトリのマージ戦略（`Services/EntityMetadataProvider.cs`）

```
config/entities-sqlserver/   ← 先に読み込み（差分ファイルのみ配置）
config/entities/             ← 不足エンティティを補完
```

SQL Server で変更が必要なのは文字列連結演算子を使う YAML のみです：
- `entities-sqlserver/customer.yml`（`e.LastName + ', ' + e.FirstName`）
- `entities-sqlserver/invoice.yml`（`c.LastName + ', ' + c.FirstName`）

### 17.4 DB 初期化（`Data/DbInitializer.cs`）

| プロバイダー | 動作 |
|-------------|------|
| `sqlite` | Chinook DB を自動ダウンロード（存在しない場合）+ SQLite 構文で AppUser/AuditLog 作成 |
| `sqlserver` | Chinook ダウンロードをスキップ + SQL Server 構文（`IF NOT EXISTS` / `INT IDENTITY`）でテーブル作成 |

---

## 18. Chinook 全テーブル YAML（SQLite 版）

| ファイル | エンティティ | 主な設定 |
|----------|-------------|---------|
| `artist.yml` | Artist | 名前検索・ソート |
| `album.yml` | Album | Artist FK ピッカー、Artist フィルター |
| `track.yml` | Track | Album/Genre FK、価格範囲フィルター、keyset ページング |
| `genre.yml` | Genre | 名前検索 |
| `employee.yml` | Employee | 生年月日範囲フィルター |
| `customer.yml` | Customer | SupportRep FK、国フィルター、Invoice リンク |
| `invoice.yml` | Invoice | Customer FK ピッカー、日付・金額範囲フィルター |
| `mediatype.yml` | MediaType | Track へのリンク付き（新規追加） |
| `playlist.yml` | Playlist | 名前検索（新規追加） |
| `invoiceline.yml` | InvoiceLine | Invoice/Track FK、Unit Price 範囲フィルター（新規追加） |

---

## 19. UX バグ修正

### 19.1 フォームフィールド消去バグ

バリデーションエラーやフックキャンセル時に `item = null` で VM を組み立てていたため、全フォームフィールドが空になっていた問題を修正しました。

- `DynamicFormViewModel` に `SubmittedValues: Dictionary<string, string?>?` を追加
- `Create`・`Edit` のすべてのエラーリターン経路で `SubmittedValues: form` を渡す
- `_Form.cshtml` でフィールド値表示時に `SubmittedValues` を優先

### 19.2 HTMX 確認ダイアログ競合

HTMX フォームの `submit` イベントで `evt.preventDefault()` を呼んでも XHR が送られてしまう問題を修正。

- HTMX 組み込みの `hx-confirm` + `htmx:confirm` イベントに切り替え
- `evt.detail.issueRequest(true)` で確認後にリクエストを発行
- `hx-confirm=""` 空文字は確認なし扱いで即リクエスト

### 19.3 リンクラベルの多言語対応

`EntityLinkDefinition` に `LabelI18n` / `GetLabel()` を追加し、`_List.cshtml` を更新。

### 19.4 _FormField.cshtml の抽出

ページモード・モーダルモードで重複していたフィールド描画 HTML を `_FormField.cshtml` に抽出。両モードとも `Html.PartialAsync("_FormField", ...)` を呼ぶよう変更し、メンテナビリティを向上。

---

## 20. Dashboard 機能（YAML 定義統計カード・グラフ）

### 20.1 概要

アプリのトップページを **Dashboard** に変更しました。`config/dashboard.yml` に統計・グラフ定義を書くだけで、DB から集計した数値をカード＋グラフで表示できます。コードの変更は不要です。

| 機能 | 説明 |
|------|------|
| 統計カード | クリックするとエンティティ一覧へ遷移（12種類） |
| グラフ | Chart.js 4.4.3（棒・折れ線・ドーナツ、4種類） |

詳細は `docs/dashboard.md` を参照してください。

### 20.2 新規ファイル一覧

| ファイル | 役割 |
|---------|------|
| `Models/DashboardConfig.cs` | `DashboardConfig` / `DashboardStatDefinition` / `DashboardChartDefinition` |
| `Services/DashboardConfigProvider.cs` | `IDashboardConfigProvider` インターフェースと実装（YAML ローダー・Singleton） |
| `Controllers/DashboardController.cs` | 統計・グラフ集計クエリ実行、`DashboardViewModel` 組み立て |
| `Views/Dashboard/Index.cshtml` | カードグリッド（リンク付き）＋ Chart.js グラフ |
| `config/dashboard.yml` | 統計（12種）＋グラフ（4種）定義 |

### 20.3 アーキテクチャ

```
config/dashboard.yml
        │  起動時に読み込み（Singleton）
        ▼
DashboardConfigProvider
        │  GetConfig() → DashboardConfig { Stats[], Charts[] }
        ▼
DashboardController.Index()
        ├─ BuildStatsAsync()   → DashboardStatViewModel[] (EntityUrl付き)
        └─ BuildChartsAsync()  → DashboardChartViewModel[] (LabelsJson/ValuesJson)
        ▼
DashboardViewModel { Stats[], Charts[] }
        ▼
Views/Dashboard/Index.cshtml
  ├── stat カード × N（<a>リンク → エンティティ一覧）
  └── <canvas> × M（Chart.js 4.4.3 初期化）
```

### 20.4 モデル定義（`Models/DashboardConfig.cs`）

```csharp
public class DashboardConfig
{
    public List<DashboardStatDefinition>  Stats  { get; set; } = new();
    public List<DashboardChartDefinition> Charts { get; set; } = new();
}

public class DashboardStatDefinition
{
    public string Label { get; set; } = "";
    public Dictionary<string, string> LabelI18n { get; set; } = new();
    public string Entity    { get; set; } = "";    // entities.yml のキーと一致
    public string Aggregate { get; set; } = "count"; // count / sum / avg
    public string? Column   { get; set; }          // sum / avg の対象カラム
    public string? Filter   { get; set; }          // WHERE 句（任意）
    public string? Icon     { get; set; }          // 絵文字アイコン
    public string? Color    { get; set; }          // DaisyUI バッジクラス
    public string GetLabel() { ... }               // 現在ロケールのラベルを返す
}

public class DashboardChartDefinition
{
    public string Title      { get; set; } = "";
    public Dictionary<string, string> TitleI18n { get; set; } = new();
    public string Type       { get; set; } = "bar";  // bar/line/doughnut/pie
    public string Entity     { get; set; } = "";
    public string ValueAggregate  { get; set; } = "count";
    public string? ValueColumn    { get; set; }
    public string? GroupExpression { get; set; }     // GROUP BY 式
    // FK JOIN でラベル取得
    public string? LabelJoinEntity  { get; set; }
    public string? LabelJoinKey     { get; set; }
    public string? LabelJoinDisplay { get; set; }
    public string? OrderBy  { get; set; }   // label / value
    public string? OrderDir { get; set; }   // asc / desc
    public int     Limit    { get; set; } = 10;
    public string? Filter   { get; set; }
    // 色設定
    public string? ColorBg     { get; set; }
    public string? ColorBorder { get; set; }
    public List<string>? Colors { get; set; }  // doughnut/pie 用
    public string GetTitle() { ... }
}
```

### 20.5 ViewModel 定義（`Controllers/DashboardController.cs`）

コントローラーファイル内で定義する View モデルです。

```csharp
// 統計カード 1 枚分
public class DashboardStatViewModel
{
    public string Label      { get; set; } = "";   // 表示ラベル
    public string Value      { get; set; } = "";   // 集計値（フォーマット済み文字列）
    public string? Icon      { get; set; }         // 絵文字アイコン
    public string? Color     { get; set; }         // DaisyUI バッジクラス
    public string? EntityUrl { get; set; }         // クリック時の遷移先 URL
}

// グラフ 1 つ分
public class DashboardChartViewModel
{
    public string Title        { get; set; } = ""; // グラフタイトル
    public string Type         { get; set; } = "bar"; // bar/line/doughnut/pie
    public string LabelsJson   { get; set; } = "[]";  // JSON 文字列配列
    public string ValuesJson   { get; set; } = "[]";  // JSON 数値配列
    public string? ColorBg     { get; set; }       // 単色背景（棒・折れ線用）
    public string? ColorBorder { get; set; }       // 単色枠線（棒・折れ線用）
    public string? ColorsJson  { get; set; }       // 複数色 JSON 配列（ドーナツ/円用）
}

// Index アクションが View に渡すルートモデル
public class DashboardViewModel
{
    public List<DashboardStatViewModel>  Stats  { get; set; } = new();
    public List<DashboardChartViewModel> Charts { get; set; } = new();
}
```

### 20.6 YAML 設定リファレンス（`config/dashboard.yml`）

#### 20.6.1 stats セクション（統計カード）

```yaml
stats:
  # カウント集計（最もシンプル）
  - label: Artists
    labelI18n:
      en-US: Artists
      zh-CN: 艺术家
      ja-JP: アーティスト
    entity: artist        # entities.yml のエンティティキー
    aggregate: count
    icon: "🎵"
    color: badge-primary

  # SUM 集計（column 必須）
  - label: Total Revenue
    entity: invoice
    aggregate: sum
    column: Total         # 集計するカラム名
    icon: "💰"
    color: badge-success

  # AVG 集計
  - label: Avg Invoice
    entity: invoice
    aggregate: avg
    column: Total
    icon: "📊"
    color: badge-info

  # WHERE 句付き集計
  - label: Active Tracks
    entity: track
    aggregate: count
    filter: "Milliseconds > 0"   # WHERE 句を直接記述
    icon: "🎸"
    color: badge-accent
```

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|:----:|------|
| `label` | string | ✅ | デフォルト言語ラベル |
| `labelI18n` | map | — | ロケール別ラベル（`en-US` / `zh-CN` / `ja-JP`） |
| `entity` | string | ✅ | `entities.yml` で定義したエンティティキー |
| `aggregate` | string | ✅ | `count` / `sum` / `avg` |
| `column` | string | ※ | `sum` / `avg` 時必須 |
| `filter` | string | — | WHERE 句（省略可） |
| `icon` | string | — | アイコン絵文字 |
| `color` | string | — | DaisyUI バッジカラークラス（例: `badge-primary`） |

#### 20.6.2 charts セクション（グラフ）

```yaml
charts:
  # ── 折れ線グラフ：月別売上 ─────────────────────────────────────
  - title: Monthly Revenue
    titleI18n:
      en-US: Monthly Revenue
      ja-JP: 月別売上推移
    type: line                                       # グラフ種別
    entity: invoice
    valueAggregate: sum
    valueColumn: Total
    groupExpression: "strftime('%Y-%m', InvoiceDate)" # GROUP BY 式
    orderBy: label                                   # label / value
    orderDir: asc                                    # asc / desc
    limit: 24
    colorBg: "rgba(99, 102, 241, 0.15)"              # 塗りつぶし色
    colorBorder: "rgba(99, 102, 241, 1)"             # 線色

  # ── ドーナツグラフ：ジャンル別（FK JOIN） ────────────────────────
  - title: Tracks by Genre
    type: doughnut
    entity: track
    valueAggregate: count
    labelJoinEntity: genre        # JOIN 先エンティティ
    labelJoinKey: GenreId         # 現テーブルの FK カラム
    labelJoinDisplay: Name        # JOIN 先の表示カラム
    orderBy: value
    orderDir: desc
    limit: 10
    colors:                       # doughnut / pie 用複数色リスト
      - "rgba(99, 102, 241, 0.85)"
      - "rgba(16, 185, 129, 0.85)"
      # ...

  # ── 棒グラフ：カラム直接 GROUP BY ─────────────────────────────
  - title: Top 10 Countries by Invoices
    type: bar
    entity: invoice
    valueAggregate: count
    groupExpression: BillingCountry   # カラム名をそのまま指定
    orderBy: value
    orderDir: desc
    limit: 10
    colorBg: "rgba(16, 185, 129, 0.7)"
    colorBorder: "rgba(16, 185, 129, 1)"
```

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|:----:|------|
| `title` | string | ✅ | グラフタイトル |
| `titleI18n` | map | — | ロケール別タイトル |
| `type` | string | ✅ | `bar` / `line` / `doughnut` / `pie` |
| `entity` | string | ✅ | エンティティキー |
| `valueAggregate` | string | ✅ | `count` / `sum` / `avg` |
| `valueColumn` | string | ※ | `sum`/`avg` 時必須 |
| `groupExpression` | string | ※ | GROUP BY 式（JOIN なし時必須） |
| `labelJoinEntity` | string | — | FK JOIN で取得するラベルの元エンティティ |
| `labelJoinKey` | string | — | 現テーブルの FK カラム名 |
| `labelJoinDisplay` | string | — | JOIN 先の表示カラム名 |
| `orderBy` | string | — | `label` / `value`（既定: `value`） |
| `orderDir` | string | — | `asc` / `desc`（既定: `desc`） |
| `limit` | int | — | 取得件数（既定: 10） |
| `filter` | string | — | WHERE 句 |
| `colorBg` | string | — | 塗りつぶし色（単色用 RGBA） |
| `colorBorder` | string | — | 枠線色（単色用 RGBA） |
| `colors` | list | — | `doughnut`/`pie` 用カラーリスト（RGBA） |

#### 20.6.3 デフォルト設定一覧

**統計カード 12 種**

| カード | エンティティ | 集計 | アイコン | バッジ |
|-------|------------|------|--------|--------|
| Artists | artist | COUNT | 🎵 | badge-primary |
| Albums | album | COUNT | 💿 | badge-secondary |
| Tracks | track | COUNT | 🎸 | badge-accent |
| Genres | genre | COUNT | 🎼 | badge-primary |
| Media Types | mediatype | COUNT | 📀 | badge-secondary |
| Playlists | playlist | COUNT | 📋 | badge-accent |
| Customers | customer | COUNT | 👥 | badge-info |
| Employees | employee | COUNT | 🧑‍💼 | badge-neutral |
| Invoices | invoice | COUNT | 📄 | badge-warning |
| Invoice Lines | invoiceline | COUNT | 🧾 | badge-ghost |
| Total Revenue | invoice | SUM(Total) | 💰 | badge-success |
| Avg Invoice | invoice | AVG(Total) | 📊 | badge-info |

**グラフ 4 種**

| グラフ | 種別 | エンティティ | 集計 | 備考 |
|-------|------|------------|------|------|
| Monthly Revenue | `line` | invoice | SUM(Total) | strftime 月別・24ヶ月 |
| Tracks by Genre | `doughnut` | track | COUNT | Genre JOIN・Top 10 |
| Top 10 Countries by Invoices | `bar` | invoice | COUNT | BillingCountry 別 |
| Top 10 Artists by Albums | `bar` | album | COUNT | Artist JOIN |

### 20.7 コントローラーの集計ロジック（`Controllers/DashboardController.cs`）

#### 統計カード（`BuildStatsAsync`）

```csharp
// count
"SELECT COUNT(*) FROM {meta.Table} [WHERE {filter}]"

// sum
"SELECT COALESCE(SUM({col}), 0) FROM {meta.Table} [WHERE {filter}]"

// avg
"SELECT COALESCE(AVG({col}), 0) FROM {meta.Table} [WHERE {filter}]"
```

- `EntityUrl = Url.Action("Index", "DynamicEntity", new { entity })` をセット（カードのリンク先）
- SQL 失敗 / entity 未定義の場合はスキップ

#### グラフ（`BuildChartsAsync`）

**シンプル GROUP BY**（`groupExpression` 使用）:
```sql
SELECT {groupExpression} AS label, {aggregate} AS value
FROM {Table} [WHERE {filter}]
GROUP BY {groupExpression}
ORDER BY {orderBy} {orderDir} LIMIT {limit}
```

**FK JOIN**（`labelJoinEntity` 使用）:
```sql
SELECT j.{LabelJoinDisplay} AS label, {aggregate} AS value
FROM {Table}
JOIN {JoinTable} j ON {Table}.{LabelJoinKey} = j.{JoinPK}
[WHERE {filter}]
GROUP BY j.{LabelJoinDisplay}
ORDER BY {orderBy} {orderDir} LIMIT {limit}
```

クエリ結果は `System.Text.Json.JsonSerializer.Serialize` でラベル・値をそれぞれ JSON 配列化し、`LabelsJson` / `ValuesJson` として View に渡します。

### 20.8 グラフ描画（`Views/Dashboard/Index.cshtml`）

Chart.js 4.4.3 を CDN からロード（`@section Scripts` 内）。各グラフ定義について `<canvas id="chart-@i">` を生成し、インライン `<script>` で初期化します。

```javascript
new Chart(ctx, {
    type: 'bar' | 'line' | 'doughnut' | 'pie',
    data: {
        labels: @Html.Raw(chart.LabelsJson),
        datasets: [{ data: @Html.Raw(chart.ValuesJson), ... }]
    },
    options: { responsive: true, maintainAspectRatio: false, ... }
});
```

- 単色グラフ（棒・折れ線）: `colorBg` / `colorBorder` を使用
- 複数色グラフ（ドーナツ・円）: `colors` リストを `colorsJson` として渡す
- Y 軸は 1000 以上を `k` 単位表示（例: `2.3k`）

### 20.9 DI 登録（`Program.cs`）

```csharp
builder.Services.AddSingleton<IDashboardConfigProvider, DashboardConfigProvider>();
```

`DashboardConfigProvider` は起動時に `config/dashboard.yml` を一度だけ読み込む Singleton です。

### 20.10 デフォルトルート変更

```csharp
// Program.cs（変更後）
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");
```

アプリの起動直後（`/`）にアクセスすると Dashboard が表示されます。

### 20.11 動作検証結果

| 検証項目 | 結果 |
|---------|------|
| `dotnet build` | ✅ エラー 0 件 |
| 統計カード 12 種の表示 | ✅ COUNT / SUM / AVG 各集計が正しく表示される |
| カードのジャンプリンク | ✅ クリックで対応エンティティ一覧へ遷移 |
| 棒グラフ（Top Countries / Top Artists） | ✅ 正常表示 |
| 折れ線グラフ（Monthly Revenue） | ✅ 月別 24 ヶ月分が時系列で表示される |
| ドーナツグラフ（Tracks by Genre） | ✅ Genre JOIN・上位 10 件が円形表示される |
| `entity` 未定義時のスキップ | ✅ エラーにならずカード・グラフをスキップ |
| SQL エラー時のスキップ | ✅ 集計失敗分だけスキップし他は正常表示 |

---

## 21. UX バグ修正（URLリセット・パンくず重複・HOME→Dashboard）

### 21.1 "New Page" ボタンの URLリセットバグ

#### 原因

`Index.cshtml` のヘッダー部分に "New Page" ボタンを配置していた。HTMX が `#list-container` を部分更新する際、`Index.cshtml` 本体（ヘッダー含む）は再描画されない。そのため検索・ソート・フィルター状態が変化しても、ボタンの `returnUrl` は初期状態のシンプルな URL（`?entity=customer`）のままになっていた。

```
[HTMX 更新の範囲]
Index.cshtml（再描画されない）
  └── #list-container（_List.cshtml）← ここだけ更新される
```

#### 修正

"New Page" ボタンを `Index.cshtml` から削除し、`_List.cshtml` の先頭（カード内）に移動しました。`_List.cshtml` は HTMX によって毎回再描画されるため、`currentReturnUrl`（検索・ソート・フィルター・ページ状態を含む完全な URL）が常に最新の値を持ちます。

```razor
{{!-- _List.cshtml（変更後） --}}
<div class="card bg-base-100 shadow">
    <div class="card-body space-y-4">
        <div class="flex items-center gap-2 flex-wrap">
            <a class="btn btn-outline btn-sm"
               href="@Url.Action("CreatePage", ..., new { returnUrl = currentReturnUrl })">
                New Page
            </a>
            ...（ページサイズセレクタ）
        </div>
```

`currentReturnUrl` は `_List.cshtml` 内で以下のように構築されています：

```razor
var currentListUrl = Url.Action("Index", "DynamicEntity", new
{
    entity = Model.Entity,
    search = Model.Search,
    sort   = Model.Sort,
    dir    = Model.Dir,
    pageSize = Model.PageSize,
    count  = Model.CountEnabled ? "true" : "false",
    cursor = Model.Cursor,
    returnUrl = Model.ReturnUrl,
    // ...
});
var currentReturnUrl = string.IsNullOrEmpty(currentListUrl) ? null : currentListUrl;
```

### 21.2 保存後 URLリセットバグ（Create / Edit POST）

#### 原因

ページモードで新規作成・更新が成功した後、コントローラーが以下のようにリダイレクトしていた。

```csharp
// 修正前
if (isPageMode)
{
    return RedirectToAction(nameof(Index), new { entity });
    // → /DynamicEntity/Index?entity=customer  ← 検索・ソート状態が消える
}
```

#### 修正

`returnUrl`（フォームの hidden フィールドから `[FromForm]` で受け取る）が存在する場合はそちらへリダイレクトするよう変更しました。

```csharp
// 修正後（Create POST / Edit POST 共通）
if (isPageMode)
{
    return Redirect(returnUrl ?? Url.Action(nameof(Index), new { entity })!);
    // returnUrl があれば検索・ソート状態を維持して戻る
}
```

`returnUrl` は `_Form.cshtml` の hidden フィールドとして送信されます：

```razor
<input type="hidden" name="returnUrl" value="@Context.Request.Query["returnUrl"]" />
```

#### 影響ファイル

- `Controllers/DynamicEntityController.cs`（`Create` POST・`Edit` POST の isPageMode 分岐）

### 21.3 Cancel ボタンの URLリセットバグ

ページモードのフォームに「Cancel」リンクがあるが、これも `returnUrl` を無視して基本 Index に遷移していた。

```razor
{{!-- 修正前 --}}
<a href="@Url.Action("Index", "DynamicEntity", new { entity = Model.Entity })">Cancel</a>

{{!-- 修正後 --}}
@{
    var cancelUrl = Context.Request.Query["returnUrl"].ToString() is { Length: > 0 } cancelReturnUrl
        ? cancelReturnUrl
        : Url.Action("Index", "DynamicEntity", new { entity = Model.Entity });
}
<a href="@cancelUrl">Cancel</a>
```

#### 影響ファイル

- `Views/DynamicEntity/_Form.cshtml`

### 21.4 パンくずリスト重複バグ（FormPage.cshtml）

#### 原因

`BuildBreadcrumbChain(returnUrl)` は `returnUrl` クエリパラメータを再帰的に解析し、エンティティ名を抽出してリストを構築します（セクション 15.1 参照）。

一方、`FormPage.cshtml` にはエンティティリンクがハードコードされていました。

```razor
{{!-- 修正前：常に表示（BuildBreadcrumbChain の出力と重複する） --}}
@foreach (var crumb in breadcrumbs) { /* Customer */ }
<li><a href="...">Customer</a></li>   {{!-- ← ハードコード（重複）}}
<li>Edit</li>
```

結果として `Dashboard / Customer / Customer / Edit` のように同じエンティティ名が2回表示されていた。

#### 修正

パンくずチェーンが存在する場合（`returnUrl` が提供された通常ナビゲーション時）はハードコードのリンクを省略し、チェーンが空の場合（直接 URL アクセスなど）のみ表示するよう変更しました。

```razor
{{!-- 修正後 --}}
@foreach (var crumb in breadcrumbs) { /* チェーン由来のリンク */ }
@if (breadcrumbs.Count == 0)
{
    {{!-- returnUrl がない直接ナビゲーション時のみ表示 --}}
    <li><a href="@Url.Action("Index", ...)">@Model.Meta.GetDisplayName()</a></li>
}
<li>@formLabel</li>
```

| シナリオ | 修正前 | 修正後 |
|---------|--------|--------|
| 一覧 → Edit Page（returnUrl あり） | Dashboard / Customer / **Customer** / Edit | Dashboard / Customer / Edit ✅ |
| 直接 URL アクセス（returnUrl なし） | Dashboard / Customer / Edit | Dashboard / Customer / Edit ✅ |

#### 影響ファイル

- `Views/DynamicEntity/FormPage.cshtml`

### 21.5 HOME → Dashboard への変更

パンくずナビゲーションの最上位ラベル「Home」を「Dashboard」に変更し、リンク先も `DashboardController.Index` に変更しました。

```razor
{{!-- 修正前（Index.cshtml / FormPage.cshtml 共通） --}}
<a asp-controller="Home" asp-action="Index">Home</a>

{{!-- 修正後 --}}
<a asp-controller="Dashboard" asp-action="Index">Dashboard</a>
```

また `_Layout.cshtml` のサイドバーにも Dashboard リンクをエンティティ一覧の上部に追加しました。

```razor
{{!-- _Layout.cshtml サイドバー --}}
<li>
    <a class="@(isDashboard ? "active" : "")"
       asp-controller="Dashboard" asp-action="Index">Dashboard</a>
</li>
<li class="menu-title mt-1"><span>Entities</span></li>
{{!-- エンティティ一覧 ... --}}
```

#### 影響ファイル

- `Views/DynamicEntity/Index.cshtml`
- `Views/DynamicEntity/FormPage.cshtml`
- `Views/Shared/_Layout.cshtml`
- `Program.cs`（デフォルトルート変更）

### 21.6 検証結果

| 項目 | 結果 |
|------|------|
| `dotnet build` | ✅ 成功（0 エラー） |
| URL リセットバグ（New Page） | ✅ `_List.cshtml` 移動により検索・フィルター状態を保持 |
| URL リセットバグ（保存後） | ✅ `returnUrl` へのリダイレクトで状態を保持 |
| Cancel ボタン | ✅ returnUrl 対応済み |
| パンくず重複 | ✅ 重複なし（条件付き表示） |
| Dashboard 表示 | ✅ 統計カード正常描画 |
| HOME → Dashboard | ✅ パンくず・サイドバー更新済み |

---

## 22. マルチプロジェクト対応・スキーマ定義表示機能（2026-03-02）

### 22.1 概要

単一アプリで複数プロジェクトを同時運用できるマルチプロジェクト基盤を実装し、LCP から todo プロジェクトを移植。加えて、Admin ユーザーが YAML 定義を Web ブラウザ上で確認できる**スキーマ定義表示機能**を追加した。

### 22.2 マルチプロジェクト基盤

#### アーキテクチャ

```
ProjectManager (Singleton)
  └─ projects/ をスキャン → ProjectInfo × N（キャッシュ）
       └─ ProjectInfo = Config + EntityMetadataProvider + DashboardConfigProvider

ProjectMiddleware (Scoped/リクエスト毎)
  └─ {project} ルートパラメータ → ProjectScope.Set(projectInfo)

ProjectScope (Scoped)
  └─ ProjectAwareEntityMetadataProvider / ProjectAwareDashboardConfigProvider が委譲先として参照
```

#### 新規ファイル

| ファイル | 種別 | 役割 |
|---------|------|------|
| `Services/ProjectManager.cs` | Singleton | `projects/` スキャン・ProjectInfo キャッシュ |
| `Services/ProjectInfo.cs` | 不変クラス | プロジェクト情報ホルダー |
| `Services/ProjectScope.cs` | Scoped | 現在プロジェクト保持 |
| `Services/ProjectAwareEntityMetadataProvider.cs` | Scoped | `IEntityMetadataProvider` プロキシ |
| `Services/ProjectAwareDashboardConfigProvider.cs` | Scoped | `IDashboardConfigProvider` プロキシ |
| `Services/YamlSchemaValidator.cs` | Singleton | `project.yaml` → JSON Schema 検証 |
| `Middleware/ProjectMiddleware.cs` | ミドルウェア | `{project}` 解決・ProjectScope 初期化 |
| `Models/ProjectConfig.cs` | モデル | `project.yaml` デシリアライズ |
| `Schemas/project-schema.json` | EmbeddedResource | プロジェクト設定 JSON Schema |

#### ルート・ミドルウェア変更

```csharp
// Program.cs
app.UseRouting();
app.UseMiddleware<ProjectMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("project",
    "{project}/{controller=Dashboard}/{action=Index}/{id?}");
```

### 22.3 todo プロジェクト（LCP 移植）

**ディレクトリ:** `projects/todo/`

| エンティティ | テーブル | 主な特徴 |
|-------------|----------|---------|
| `task` | Task | JOIN Project、Status/Priority ドロップダウン、FK ProjectId（ピッカー）、DueDate、links: project |
| `project` | Project | Status ドロップダウン、links: tasks |

**ダッシュボード:** 統計カード 7 個・グラフ 4 個（Status ドーナツ・Priority 棒・Per Project 棒・Projects Status 円）

### 22.4 スキーマ定義表示機能

#### Definition ページ

**URL:** `/{project}/DynamicEntity/Definition?entity={name}`（Admin 限定）

6 タブ構成（Columns / Forms / Filters / Joins / Links / Settings）でエンティティ定義を一覧表示。

#### AllDefinitions ページ

**URL:** `/{project}/DynamicEntity/AllDefinitions`（Admin 限定）

全エンティティを 1 テーブルで比較表示。JOIN 一覧・エンティティ間リンクのクロス参照サマリを自動生成。

#### 追加コード（`DynamicEntityController.cs`）

```csharp
[Authorize(Roles = "Admin")]
public IActionResult Definition(string entity = "customer")
{
    var meta = _meta.Get(entity);
    return View("Definition", new EntityDefinitionViewModel(entity, meta));
}

[Authorize(Roles = "Admin")]
public IActionResult AllDefinitions()
{
    var all = _meta.GetAll();
    return View("AllDefinitions", new AllDefinitionsViewModel(all));
}

public record EntityDefinitionViewModel(string Entity, EntityDefinition Meta);
public record AllDefinitionsViewModel(IReadOnlyDictionary<string, EntityDefinition> Entities);
```

#### ナビゲーション変更

- **`Index.cshtml`**: タイトル横に Definition アイコンボタン（Admin のみ）
- **`_Layout.cshtml`**:
  - 各エンティティリンクを `<details>/<summary>` サブメニューへ（Admin のみ）: List / Definition
  - Admin セクションに Schema（AllDefinitions）リンクを追加

### 22.5 影響ファイル

**新規作成:**
- `Services/ProjectManager.cs`、`ProjectInfo.cs`、`ProjectScope.cs`
- `Services/ProjectAwareEntityMetadataProvider.cs`、`ProjectAwareDashboardConfigProvider.cs`
- `Services/YamlSchemaValidator.cs`
- `Middleware/ProjectMiddleware.cs`
- `Models/ProjectConfig.cs`
- `Schemas/project-schema.json`
- `projects/todo/`（project.yaml / entities/ / dashboard.yml / database/）
- `Views/DynamicEntity/Definition.cshtml`
- `Views/DynamicEntity/AllDefinitions.cshtml`

**変更:**
- `Controllers/DynamicEntityController.cs`（Definition / AllDefinitions アクション・ViewModel 追加）
- `Controllers/AccountController.cs`、`DashboardController.cs`、`UsersController.cs`（ProjectScope 対応）
- `Services/Auth/UserAuthService.cs`、`AuditLogService.cs`（ProjectScope 接続文字列）
- `Services/EntityMetadataProvider.cs`、`DashboardConfigProvider.cs`（path 引数コンストラクタ）
- `Models/EntityMetadata.cs`（EntityConfigRoot.Imports 追加）
- `Data/DbInitializer.cs`（全プロジェクト DB 初期化）
- `Program.cs`（ProjectMiddleware・マルチプロジェクト DI 登録）
- `Views/DynamicEntity/Index.cshtml`（Definition ボタン）
- `Views/Shared/_Layout.cshtml`（サイドバーサブメニュー・Schema リンク）

### 22.6 検証結果

| 項目 | 結果 |
|------|------|
| `dotnet build` | ✅ 成功（0 エラー） |
| `todo/DynamicEntity/Definition?entity=task` | ✅ 200 |
| `todo/DynamicEntity/Definition?entity=project` | ✅ 200 |
| `todo/DynamicEntity/AllDefinitions` | ✅ 200 |
| `chinook/DynamicEntity/AllDefinitions` | ✅ 200 |
| todo / chinook 両プロジェクト同時動作 | ✅ 確認済み |
