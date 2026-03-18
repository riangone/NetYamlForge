# NetYamlForge コアアーキテクチャ 詳解（コード注解版）

> 対象読者: フレームワークの内部実装を理解したい開発者・保守担当者
> 更新日: 2026-03-11

---

## 目次

1. [フレームワーク全体像](#1-フレームワーク全体像)
2. [リクエスト処理フロー](#2-リクエスト処理フロー)
3. [エンティティメタデータシステム](#3-エンティティメタデータシステム)
4. [SQL生成エンジン（DynamicCrudRepository）](#4-sql生成エンジン)
5. [フックシステム](#5-フックシステム)
6. [トランザクション管理](#6-トランザクション管理)
7. [マルチプロジェクト機構](#7-マルチプロジェクト機構)
8. [セキュリティアーキテクチャ](#8-セキュリティアーキテクチャ)
9. [設計判断の背景](#9-設計判断の背景)

---

## 1. フレームワーク全体像

```
NetYamlForge/
├── Program.cs                         # エントリポイント・DI設定・CLIコマンド
├── Controllers/
│   ├── DynamicEntityController.cs     # CRUD UI: 30以上のアクション
│   ├── DashboardController.cs         # 集計ダッシュボード
│   ├── ApiEntityController.cs         # REST APIエンドポイント
│   └── PageController.cs              # カスタムページ
├── Services/
│   ├── DynamicCrudRepository.cs       # SQL組立・実行エンジン（核心）
│   ├── EntityMetadataProvider.cs      # YAMLロード・キャッシュ
│   ├── EntityCrudExecutionService.cs  # フック実行・トランザクション管理
│   ├── Hooks/                         # フックシステム
│   │   ├── IEntityHook.cs            # フックインターフェース
│   │   ├── EntityHookRegistry.cs     # フック名→実装のDIルックアップ
│   │   ├── EntityHookContext.cs      # フック実行コンテキスト
│   │   └── CommonHooks.cs            # 組み込みフック20種以上
│   └── Dialect/                       # DBダイアレクト抽象化
│       ├── ISqlDialect.cs            # 方言インターフェース
│       ├── SqliteDialect.cs          # SQLite実装
│       └── SqlServerDialect.cs       # SQL Server実装
├── Models/
│   └── EntityMetadata.cs             # YAMLデータモデル（全クラス）
└── projects/<name>/                   # プロジェクトごとの設定
    ├── project.yaml                   # DB設定・機能フラグ
    ├── entities/                      # エンティティYAML
    └── Hooks/                         # プロジェクト固有フック
```

### 中心的設計原則

| 原則 | 説明 |
|------|------|
| **YAML優先** | UIとSQLの唯一の真実の源はYAMLファイル。コード不要でスキーマ変更可能 |
| **セキュリティ多層防御** | 識別子検証 → 式検証 → パラメータ化クエリ → Roslynアナライザー |
| **拡張点はフック** | ビジネスロジックはHookで挟む。コントローラー・リポジトリは変更しない |
| **マルチプロジェクト分離** | プロジェクトごとにDB・エンティティ・フックを完全分離 |

---

## 2. リクエスト処理フロー

### 2.1 一覧表示（GET /chinook/DynamicEntity/Index?entity=customer）

```
HTTP GET /{project}/DynamicEntity/Index?entity=customer
  │
  ▼
[ProjectMiddleware]
  ─ {project} = "chinook" をルートパラメータから抽出
  ─ ProjectManager.TryGet("chinook") → ProjectInfo（DB接続文字列等を保持）
  ─ ProjectScope.Set(projectInfo) → スコープ付きDIコンテナに設定
  ─ LocalizationProjectContext.CurrentProjectName = "chinook" に設定
  │
  ▼
[AuthMiddleware]
  ─ Cookie "auth" を検証
  ─ 未認証の場合 → /Account/Login にリダイレクト
  │
  ▼
[DynamicEntityController.Index("customer")]
  ─ IEntityMetadataProvider.TryGet("customer") でメタデータ取得
  ─ 非公開エンティティの場合 → RejectIfNotVisible() が 403 を返す
  │
  ▼
[DynamicEntityListQueryService.LoadAsync]
  ─ DynamicCrudRepository.GetAllAsync(entity, search, sort, dir, filters, page)
    │  ├─ EntityMetadataProvider.Get("customer")
    │  │    └─ YAMLロード済みキャッシュから EntityDefinition を返す
    │  ├─ ValidateMetadata(meta)
    │  │    └─ テーブル名・列名・式を IdentifierRegex で検証（SQL注入防止）
    │  ├─ BuildFromClause(meta)
    │  │    └─ "FROM customer LEFT JOIN country c ON customer.country_id = c.id"
    │  ├─ BuildWhere(meta, search, filters, param)
    │  │    ├─ 検索条件: "customer.FirstName LIKE @s_FirstName"
    │  │    ├─ フィルタ条件: "customer.Status = @status"
    │  │    └─ ソフトデリート条件: "(customer.IsDeleted=0 OR customer.IsDeleted IS NULL)"
    │  └─ _db.QueryAsync(sql, param) → Dapper実行
  ─ DynamicCrudRepository.CountAsync() → 総件数
  ─ DynamicEntityForeignKeyDataService.LoadForListAsync() → FK表示名解決
  │
  ▼
View("Index", viewModel) → HTML出力
```

### 2.2 作成（POST /chinook/DynamicEntity/Create）

```
HTTP POST /{project}/DynamicEntity/Create
  │
  ▼
[DynamicEntityController.Create] POST
  ─ IFormCollection からフォーム値を取得
  ─ DynamicEntityFormValidationService.ConvertAndValidate()
    ├─ 型変換: "123" → int 123, "2024-01-01" → DateTime
    ├─ required チェック: 空値は 400 Bad Request
    └─ 変換済み values: Dictionary<string, object?> を作成
  │
  ▼
[EntityCrudExecutionService.ExecuteCrudTransactionAsync]
  ─ _db.BeginTransaction()
  │
  ├─ RunBeforeHookAsync(["validate_email", "trim"], ctx)
  │   ├─ ProjectHookRegistry.Find("chinook", "validate_email") → null（未登録）
  │   ├─ EntityHookRegistry.Find("validate_email") → ValidateEmailHook
  │   ├─ hook.BeforeAsync(ctx, _db, null) → HookResult.Continue()
  │   ├─ ProjectHookRegistry.Find("chinook", "trim") → null
  │   ├─ EntityHookRegistry.Find("trim") → TrimHook
  │   └─ hook.BeforeAsync(ctx, _db, null) → HookResult.Continue()
  │
  ├─ DynamicCrudRepository.InsertAsync("customer", values, tx)
  │   └─ "INSERT INTO customer (FirstName, Email) VALUES (@FirstName, @Email)"
  │
  ├─ RunAfterHookAsync(["audit_log"], ctx, tx)
  │   └─ AuditLogHook.AfterAsync() → audit_log テーブルに記録
  │
  └─ tx.Commit()
  │
  ▼
JSON { ok: true } → HTMXクライアントが一覧を再取得
```

---

## 3. エンティティメタデータシステム

### 3.1 EntityMetadataProvider（`Services/EntityMetadataProvider.cs`）

YAMLファイルを読み込んでメモリにキャッシュするプロバイダー。

```csharp
// ■ 読み込み優先順位（低 → 高）
// 1. entities.generated/  ← スキャフォールド自動生成（ベース）
// 2. entities-{provider}/ ← DB方言別上書き（例: entities-sqlserver/）
// 3. entities/             ← 管理者が手動編集した最優先定義

public EntityMetadataProvider(string projectDir, string databaseProvider)
{
    var generatedDir = Path.Combine(projectDir, "entities.generated");
    LoadDirectory(deserializer, generatedDir, skipExisting: false);  // 1

    if (provider != "sqlite")
    {
        var providerDir = Path.Combine(projectDir, $"entities-{provider}");
        LoadDirectory(deserializer, providerDir, skipExisting: false);  // 2
    }

    LoadDirectory(deserializer, Path.Combine(projectDir, "entities"), skipExisting: false);  // 3
}
```

### 3.2 EntityDefinition クラスの重要フィールド

```csharp
// ■ entities/customer.yml が対応するC#クラス
public class EntityDefinition
{
    // --- DB設定（変更禁止: 既存DBとの整合性）---
    public string Table { get; set; }       // "customer" ← 実DBテーブル名
    public string Key { get; set; }         // "id" ← 単一主キー列名
    public List<string> Keys { get; set; }  // ["OrderId","ProductId"] ← 複合主キー

    // --- UI表示名（多言語対応）---
    public string DisplayName { get; set; }                       // "顧客"
    public string? DisplayNameKey { get; set; }                   // "entity.customer.label"
    public Dictionary<string, string>? DisplayNameI18n { get; set; }  // {"ja-JP":"顧客","en-US":"Customer"}

    // --- 読み取り制御（一覧表示）---
    public Dictionary<string, ColumnDefinition> Columns { get; set; }  // 一覧列

    // --- 書き込み制御（フォーム）---
    public Dictionary<string, FormDefinition> Forms { get; set; }   // フォームフィールド

    // --- フィルター ---
    public Dictionary<string, FilterDefinition> Filters { get; set; }

    // --- JOIN定義 ---
    public List<JoinDefinition> Joins { get; set; }

    // --- 機能フラグ ---
    public bool SoftDelete { get; set; }   // true → DELETE の代わりに IsDeleted=1 UPDATE
    public bool IsPublic { get; set; }     // false → Admin のみアクセス可能

    // --- フック設定 ---
    public EntityHooksDefinition? Hooks { get; set; }
}
```

### 3.3 フォーム型（FormDefinition.Type）対応表

| Type値 | UIコントロール | データ型 | 備考 |
|--------|--------------|--------|------|
| `string` | テキスト入力 | string | デフォルト |
| `int` / `decimal` | 数値入力 | number | HTMLの `type=number` |
| `date` | 日付ピッカー | string(yyyy-MM-dd) | SQLite は文字列保存 |
| `datetime` | 日時ピッカー | string | |
| `email` | メール入力 | string | ブラウザ検証付き |
| `textarea` | テキストエリア | string | 複数行入力 |
| `select` | ドロップダウン | string | `options:` で値一覧指定 |
| `checkbox` / `bool-toggle` | チェックボックス | int(0/1) | SQLite の boolean |
| `toggle-group` | トグルボタン群 | string | `options:` で選択肢 |
| `radio` | ラジオボタン | string | `options:` で選択肢 |
| `hidden` | 非表示 | any | フォームに送信するが非表示 |

---

## 4. SQL生成エンジン

### 4.1 DynamicCrudRepository（`Services/DynamicCrudRepository.cs`）

このクラスがフレームワークの中核。YAMLメタデータからSQLを動的に組み立てる。

```
【クラス責務の境界】
✅ DynamicCrudRepository がやること:
  ─ YAMLメタデータ → SQL文字列の組み立て
  ─ WHERE句の安全な構築（識別子検証 + パラメータ化）
  ─ ページング（番号付き / キーセット方式）
  ─ ソフトデリート対応
  ─ スロークエリ検出・ログ

❌ DynamicCrudRepository がやらないこと:
  ─ ビジネスロジック（フックで行う）
  ─ 認証・認可（ミドルウェアで行う）
  ─ トランザクション開始（EntityCrudExecutionServiceで行う）
```

### 4.2 GetAllAsync のSQL組み立て詳解

```csharp
public async Task<IEnumerable<dynamic>> GetAllAsync(
    string entity,      // "customer"
    string? search,     // "田中"（全文検索キーワード）
    string? sort,       // "lastName"（ソート列名）
    string? dir,        // "asc" or "desc"
    Dictionary<string, string?>? filters = null,  // {"status": "active"}
    int page = 1,
    int? pageSize = null,
    string? cursor = null,  // キーセットページング用カーソル
    bool keyset = false,    // true = キーセットモード
    bool fetchOneExtra = false)  // 次ページ有無確認用+1件取得
{
    // ① メタデータ取得 + 安全性検証
    var meta = _meta.Get(entity);
    ValidateMetadata(meta, entity);  // 全識別子をRegexで検証

    // ② SELECT句の組み立て
    // Expressionがある列は式を使う（計算列・JOIN列）
    var selectList = string.Join(", ",
        meta.Columns.Select(c =>
            c.Value.Expression != null
                ? $"{c.Value.Expression} AS {c.Key}"   // "c.country_name AS countryName"
                : $"{meta.Table}.{c.Key}"));            // "customer.firstName"

    // ③ FROM句の組み立て（JOIN含む）
    // BuildFromClause: "FROM customer LEFT JOIN country c ON customer.country_id = c.id"
    var sql = new List<string> { $"SELECT {selectList} {BuildFromClause(meta)}" };

    // ④ WHERE句の組み立て
    var param = new DynamicParameters();
    var where = BuildWhere(meta, search, filters, param);
    //   ├─ 検索: "(customer.firstName LIKE @s_firstName OR customer.email LIKE @s_email)"
    //   ├─ フィルタ: "customer.status = @status"
    //   └─ ソフトデリート: "(customer.IsDeleted = 0 OR customer.IsDeleted IS NULL)"

    // ⑤ ソートの追加
    if (sort != null && meta.Columns.TryGetValue(sort, out var colDef) && colDef.Sortable)
    {
        var direction = (dir?.ToLowerInvariant() == "desc") ? "DESC" : "ASC";
        sql.Add($" ORDER BY {colDef.Expression ?? $"{meta.Table}.{sort}"} {direction}");
    }

    // ⑥ ページングSQL追加（方言別）
    // SQLite: "LIMIT @_Size OFFSET @_Offset"
    // SQL Server: "OFFSET @_Offset ROWS FETCH NEXT @_Size ROWS ONLY"
    _dialect.AppendNumberedPagination(sql, param, effectivePageSize, offset, primaryKey);

    // ⑦ Dapperで実行
    return await TimedAsync("GetAllAsync", entity, statement, () => _db.QueryAsync(statement, param));
}
```

### 4.3 セキュリティ層（ValidateMetadata）

```csharp
private static void ValidateMetadata(EntityDefinition meta, string entityName)
{
    // SQL識別子の安全パターン: ^[A-Za-z_][A-Za-z0-9_]*$
    // 許可: "customer", "FirstName", "order_date"
    // 拒否: "customer; DROP TABLE", "1invalid", "col--"
    static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // SQL式の安全パターン（計算列・フィルタ式用）
    // 許可: "CASE WHEN Status=1 THEN 'Active' ELSE 'Inactive' END"
    // 拒否: "'; DROP TABLE users; --"
    static readonly Regex ExpressionRegex = new("^[A-Za-z0-9_\\.\\s,()\\+\\-*/%<>=!'|]+$", RegexOptions.Compiled);

    // 危険トークンチェック
    static bool IsUnsafeToken(string? value) =>
        !string.IsNullOrEmpty(value) &&
        (value.Contains(';') || value.Contains("--") ||
         value.Contains("/*") || value.Contains("*/"));

    // テーブル名・主キー・全列名・フォームフィールド・フィルタ・JOIN設定を検証
    EnsureIdentifier(meta.Table, $"{entityName}.table");
    foreach (var pkCol in meta.GetPrimaryKeyColumns())
        EnsureIdentifier(pkCol, $"{entityName}.key.{pkCol}");
    foreach (var col in meta.Columns)
        EnsureIdentifier(col.Key, $"{entityName}.column");
    // ... 以下同様
}
```

### 4.4 ページング方式の比較

| 方式 | `mode:` 設定 | SQLパターン | メリット | デメリット |
|------|-------------|-----------|--------|----------|
| 番号付き | `numbered`（デフォルト） | `LIMIT n OFFSET m` | ページ番号でジャンプ可 | 大量データでOFFSET遅い |
| キーセット | `keyset` | `WHERE id > @cursor LIMIT n` | 一定速度、大量データ向き | ページ番号ジャンプ不可 |

---

## 5. フックシステム

### 5.1 設計思想

フックは「CRUD前後に処理を挟む」プラグイン機構。
コントローラーやリポジトリを変更せずにビジネスロジックを追加できる。

```
YAML entities.yml              C#実装
─────────────────             ─────────────────
hooks:                   →    IEntityHook.BeforeAsync()
  beforeCreate:                 ValidateEmailHook.BeforeAsync()
    - validate_email              │  ← メールアドレス形式チェック
    - trim                        │
    - audit_log                   ▼
  afterCreate:               IEntityHook.AfterAsync()
    - audit_log                 AuditLogHook.AfterAsync()
                                  │  ← 監査ログ記録
```

### 5.2 IEntityHook インターフェース

```csharp
// ■ フック実装の最小単位
public interface IEntityHook
{
    // EntityHookRegistry が名前でこのフックを検索する
    string Name { get; }  // "validate_email", "trim", "audit_log" 等

    // DB書き込み「前」に呼ばれる
    // ─ HookResult.Continue() を返せば次の処理へ進む
    // ─ HookResult.Abort("メッセージ") を返せば DB操作をキャンセル
    // ─ tx は「BeforeAsync では null」（BeforeHook はトランザクション外で実行）
    Task<HookResult> BeforeAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx);

    // DB書き込み「後」（同一トランザクション内）に呼ばれる
    // ─ 例外を投げるとトランザクションがロールバックされる
    // ─ BeforeAsync → DB → AfterAsync の順で実行
    Task AfterAsync(EntityHookContext ctx, IDbConnection db, IDbTransaction? tx);
}
```

### 5.3 EntityHookContext（フック間のデータ共有）

```csharp
public class EntityHookContext
{
    public string Entity { get; set; }        // "customer"
    public CrudOperation Operation { get; set; }  // Create/Update/Delete
    public int? Id { get; set; }              // 単一主キー（Update/Delete時）
    public Dictionary<string, object?>? KeyValues { get; set; }  // 複合主キー（Update/Delete時）
    public Dictionary<string, object?> Values { get; set; }  // フォーム値（変換済み）
    public string? UserName { get; set; }     // 操作ユーザー名

    // ■ BeforeAsync → AfterAsync間のデータ受け渡しに使う自由領域
    // 例: BeforeAsync で DB から取得した古い値を保存し AfterAsync で参照
    public Dictionary<string, object?> Data { get; set; } = new();
    //   "before_available_count" → 5  (在庫減少フックで: 変更前在庫数を記録)
    //   "__hookConfig" → "Email,BackupEmail"  (フック設定パラメータ)
}
```

### 5.4 フック設定構文（entities.yml）

```yaml
hooks:
  # 単一フック（文字列形式）
  beforeCreate: validate_email

  # 複数フック（リスト形式、順次実行）
  beforeCreate:
    - validate_required:FirstName,LastName,Email  # ← コロン以降がフック設定
    - validate_email:Email,BackupEmail            # ← 検証対象の列名をカンマ区切りで
    - trim:FirstName,LastName                     # ← trim対象の列名
    - now:CreatedAt                               # ← 現在時刻を設定する列名

  afterCreate:
    - audit_log                                   # ← 監査ログ記録

  # プリセット機能（フックグループの再利用）
  presets:
    commonValidation:
      - validate_required:FirstName,Email
      - validate_email:Email
      - trim:FirstName,LastName

  # プリセット参照（@プレフィックス）
  beforeCreate:
    - "@commonValidation"    # ← commonValidation の全フックを展開
    - now:CreatedAt
```

### 5.5 フック実行優先順位

```
YAML hooks.beforeCreate: ["validate_available", "audit_log"]
       │
       ▼
EntityCrudExecutionService.RunBeforeHookAsync()
       │
       ├─ ProjectHookRegistry.Find("chinook", "validate_available")
       │   └─ プロジェクト固有フックが存在する → 実行
       │       ↳ 存在しない → 次のレジストリへ
       │
       └─ EntityHookRegistry.Find("validate_available")
           └─ フレームワーク共通フックが存在する → 実行
               ↳ 存在しない → 警告ログを出してスキップ
```

### 5.6 組み込みフック一覧（CommonHooks.cs）

| フック名 | 設定引数 | 処理内容 |
|---------|---------|--------|
| `validate_required` | `Field1,Field2,...` | 必須チェック（空値でAbort） |
| `validate_email` | `Field1,Field2,...` | メール形式チェック |
| `validate_phone` | `Field1,Field2,...` | 電話番号形式チェック |
| `validate_url` | `Field1,Field2,...` | URL形式チェック |
| `validate_unique` | `Field` | DB内重複チェック |
| `validate_range` | `Field:min=0,max=100` | 数値範囲チェック |
| `trim` | `Field1,Field2,...` | 前後空白除去 |
| `now` | `Field1,Field2,...` | 現在時刻を設定 |
| `concat` | `TargetField:Src1,Src2` | 文字列連結 |
| `audit_log` | `Entity,Field1,...` | 監査ログ記録 |

---

## 6. トランザクション管理

### 6.1 EntityCrudExecutionService の役割

```
責務: BeforeHook → DB書き込み → AfterHook を「原子的」に実行する

【重要】BeforeHookはトランザクション「外」で実行
 ─ 理由: TOCTOU（時間差競合）を避けるため
 ─ BeforeHookでDBを読んでも、実際の書き込みと競合する可能性がある
 ─ 真の排他制御が必要な場合は AfterHook + DB制約で行う

AfterHookはトランザクション「内」で実行
 ─ 理由: DB書き込みと副作用（在庫更新、通知等）を一緒にロールバックしたい
 ─ 例外を投げると全体がロールバックされる
```

```csharp
public async Task ExecuteCrudTransactionAsync(Func<IDbTransaction, Task> action)
{
    if (_db.State != ConnectionState.Open)
        _db.Open();

    using var tx = _db.BeginTransaction();
    try
    {
        // ─ コントローラーから渡された action の中で:
        // ─  1. RunBeforeHookAsync() ← tx=null（トランザクション外）
        // ─  2. repository.InsertAsync(tx) ← tx を渡して実行
        // ─  3. RunAfterHookAsync(tx) ← 同一トランザクション内
        await action(tx);
        tx.Commit();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "CRUD transaction failed");
        tx.Rollback();
        throw;
    }
}
```

### 6.2 テレメトリ（フック実行計測）

各フック実行は `HookExecutionTelemetry` に記録される：

```csharp
_hookTelemetry.Record(new HookExecutionTelemetryEvent(
    Phase: "before",          // "before" or "after"
    Source: "framework",      // "framework", "project", "missing"
    HookName: hookName,       // "validate_email"
    Entity: ctx.Entity,       // "customer"
    Operation: ctx.Operation, // Create/Update/Delete
    Result: "continue",       // "continue", "cancel", "error", "skipped_not_found"
    DurationMs: sw.ElapsedMilliseconds,
    CancelMessage: null,
    Exception: null
));
```

---

## 7. マルチプロジェクト機構

### 7.1 ProjectManager（起動時プロジェクト検出）

```
アプリ起動時（Singleton）:
  ProjectManager.__init__
    ├─ projects/ ディレクトリをスキャン
    ├─ 各 projects/<name>/ について:
    │   ├─ project.yaml を読み込む
    │   ├─ EntityMetadataProvider(projectDir, dbProvider) を生成
    │   ├─ Hooks/ ディレクトリを動的コンパイル・ロード
    │   └─ ProjectInfo にキャッシュ
    └─ _projects 辞書に格納（大文字小文字無視）

リクエスト時:
  ProjectMiddleware
    ├─ route["project"] = "chinook"
    ├─ ProjectManager.TryGet("chinook") → ProjectInfo
    ├─ ProjectScope.Set(projectInfo) → Scoped DIに注入
    └─ IEntityMetadataProvider → ProjectInfo.MetadataProvider を使用
```

### 7.2 ProjectScope（リクエストスコープ）

```csharp
// ■ AsyncLocal<T>によりリクエストごとに分離されたコンテキスト
// スレッド安全: 各HTTPリクエストは独自のProjectInfoを保持
public class ProjectScope
{
    private static readonly AsyncLocal<ProjectInfo?> _current = new();

    public ProjectInfo? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

// サービスがProjectScopeを通じて現在のプロジェクトにアクセス
public class SomeService
{
    private readonly ProjectScope _scope;

    public void DoWork()
    {
        var project = _scope.Current;  // "chinook" のProjectInfo
        var connectionString = project?.ConnectionString;
    }
}
```

---

## 8. セキュリティアーキテクチャ

### 8.1 SQL注入防止の多層防御

```
層1: YAMLスキーマ検証（YamlSchemaValidator）
  ─ YAML読み込み時にJSONスキーマで構造検証
  ─ ";" "--" "/*" "*/" を含む値を拒否

層2: 識別子検証（DynamicCrudRepository.ValidateMetadata）
  ─ テーブル名・列名・エイリアスを IdentifierRegex で検証
  ─ 式（Expression）を ExpressionRegex で検証
  ─ JOIN型を許可リスト（left/inner/right）で制限

層3: パラメータ化クエリ（Dapper）
  ─ 値は一切SQL文字列に埋め込まない
  ─ DynamicParameters でバインド: "WHERE id = @Id" + new { Id = id }

層4: Roslynアナライザー（DCS001–DCS004）
  ─ ビルド時に禁止パターンをエラーとして検出
  ─ DCS001: $"SELECT * FROM {table}" ← ビルドエラー
  ─ DCS002: asyncMethod().Result ← ビルドエラー
  ─ DCS003: new SqliteConnection() ← ビルドエラー
  ─ DCS004: user.Role == "Admin" ← 警告
```

### 8.2 認証・認可フロー

```
Cookie "auth" の検証フロー:
  AuthMiddleware
    ├─ Cookie存在チェック
    ├─ PasswordHasher.Verify(storedHash, submittedPassword)
    │   └─ PBKDF2-SHA256でハッシュ検証
    └─ HttpContext.User にClaimsPrincipalを設定

エンティティアクセス制御:
  DynamicEntityController.RejectIfNotVisible(meta)
    ├─ meta.IsPublic == true → 誰でもアクセス可
    ├─ meta.IsPublic == false && User.IsInRole("Admin") → アクセス可
    └─ meta.IsPublic == false && 非Admin → 403 Forbidden
```

---

## 9. 設計判断の背景

### なぜORMを使わないのか？

Dapper + 動的SQL生成を選択した理由:

1. **YAML駆動**: エンティティ定義が実行時に変わるため、コンパイル時型安全なORMは使えない
2. **動的JOIN**: YAMLでJOINを定義するため、コードでモデルを事前定義できない
3. **パフォーマンス**: `SELECT *` でなく必要な列だけを動的に選択する

### なぜフックシステムか？

継承・デコレータパターンではなくフックを選んだ理由:

1. **YAML統制**: フック設定をYAMLで宣言的に管理できる（コード変更不要）
2. **疎結合**: フック実装はフレームワークコアに依存しない
3. **テスト容易性**: フック単体でユニットテスト可能

### BeforeHookをトランザクション外にした理由

トランザクション内でBeforeHookを実行すると発生する問題:

```
Thread A: BEGIN TX → BeforeHook(DBチェック: 在庫=5) → INSERT → AfterHook → COMMIT
Thread B: BEGIN TX → BeforeHook(DBチェック: 在庫=5) → INSERT → AfterHook → COMMIT
  ↑ Thread A のCOMMIT前にThread BがDBチェックするため、同時に2件作成される
```

解決策: DB制約（UNIQUE, CHECK）でデータ整合性を保証し、BeforeHookはバリデーション用に限定。

---

## 付録: コードを読む順序（推奨）

初めてコードを読む場合の推奨順序:

```
1. Models/EntityMetadata.cs
   ─ EntityDefinition, ColumnDefinition, FormDefinition の構造を把握

2. Services/EntityMetadataProvider.cs
   ─ YAMLからEntityDefinitionがどう作られるかを把握

3. Services/DynamicCrudRepository.cs（GetAllAsync, InsertAsync）
   ─ EntityDefinitionからSQLがどう組み立てられるかを把握

4. Services/Hooks/IEntityHook.cs + EntityHookContext.cs
   ─ フックの契約を把握

5. Services/EntityCrudExecutionService.cs
   ─ フック実行とトランザクションの流れを把握

6. Controllers/DynamicEntityController.cs（Index, Create）
   ─ HTTPリクエストがどう処理されるかを把握
```
