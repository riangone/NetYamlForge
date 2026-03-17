# NetYamlForge 開発者チュートリアル（完全版）

> このチュートリアルでは「ゼロから業務アプリを構築する」手順を段階的に解説します。
> 更新日: 2026-03-11

---

## 目次

- [前提知識](#前提知識)
- [ステップ1: プロジェクト起動と動作確認](#ステップ1-プロジェクト起動と動作確認)
- [ステップ2: 新規プロジェクトの作成](#ステップ2-新規プロジェクトの作成)
- [ステップ3: 既存DBからエンティティ生成](#ステップ3-既存dbからエンティティ生成)
- [ステップ4: エンティティをカスタマイズ](#ステップ4-エンティティをカスタマイズ)
- [ステップ5: 外部キー（FK）の設定](#ステップ5-外部キーfkの設定)
- [ステップ6: フィルターの追加](#ステップ6-フィルターの追加)
- [ステップ7: バリデーションフックの追加](#ステップ7-バリデーションフックの追加)
- [ステップ8: カスタムフックの実装](#ステップ8-カスタムフックの実装)
- [ステップ9: ダッシュボードの設定](#ステップ9-ダッシュボードの設定)
- [ステップ10: 複合主キーへの対応](#ステップ10-複合主キーへの対応)
- [ステップ11: 多言語対応（i18n）](#ステップ11-多言語対応i18n)
- [ステップ12: SQL Serverへの移行](#ステップ12-sql-serverへの移行)
- [よくある問題と解決策](#よくある問題と解決策)
- [YAML全プロパティリファレンス](#yaml全プロパティリファレンス)

---

## 前提知識

- .NET 10 / C# の基本知識
- SQL（SELECT / INSERT / UPDATE / DELETE）の基本
- YAML の基本構文（インデント、`key: value`、リスト `-`）

---

## ステップ1: プロジェクト起動と動作確認

### 1.1 ビルドと起動

```bash
cd /home/ubuntu/ws/ccc
dotnet restore
dotnet build ./NetYamlForge/NetYamlForge.csproj
dotnet run --project ./NetYamlForge/NetYamlForge.csproj
```

起動後のURL: `http://localhost:5239`

### 1.2 サンプルプロジェクトにアクセス

ブラウザで以下を確認:

| URL | 内容 |
|-----|------|
| `http://localhost:5239` | ホームページ（全プロジェクト一覧） |
| `http://localhost:5239/chinook/Dashboard` | Chinookサンプルダッシュボード |
| `http://localhost:5239/library/Dashboard` | 図書館サンプルダッシュボード |

**ログイン情報**:
- ユーザー名: `admin`
- パスワード: `Admin@123`

### 1.3 CRUD画面の確認

```
http://localhost:5239/chinook/DynamicEntity/Index?entity=customer
```

このURLの構造:
- `chinook` → プロジェクト名（`projects/chinook/` に対応）
- `DynamicEntity` → コントローラー名
- `Index` → アクション名
- `entity=customer` → エンティティ名（`entities/customer.yml` に対応）

---

## ステップ2: 新規プロジェクトの作成

### 2.1 CLIコマンドで初期化

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj -- \
  --init-project \
  --project=shop \
  --display-name="ショップ管理" \
  --db-type=sqlite \
  --db-path=database/shop.db
```

生成されるファイル構造:
```
projects/shop/
├── project.yaml          # プロジェクト設定
├── entities/             # エンティティYAML（管理者編集用）
├── entities.generated/   # スキャフォールド生成用
├── database/
│   └── shop.db          # SQLiteデータベース（空）
├── pages/               # カスタムページ定義
│   └── StarterOverview.yaml
├── views/               # プロジェクト固有ビュー
│   └── StarterOverview.cshtml
└── config/
    ├── dashboard.yml    # ダッシュボード統計設定
    └── home-page.yml    # ホームページカード設定
```

### 2.2 project.yaml の内容

```yaml
# プロジェクト名・表示名
name: shop
displayName: "ショップ管理"

# データベース設定
database:
  type: sqlite                        # sqlite / sqlserver / postgresql / mysql
  path: database/shop.db              # SQLite の場合は path で指定（connectionString は不要）

# 機能フラグ（オプション）
features:
  multiLanguage: false
  userAuthentication: true
```

### 2.3 DBテーブルの作成

```bash
# SQLiteデータベースにテーブルを作成
sqlite3 projects/shop/database/shop.db << 'EOF'
CREATE TABLE product (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    price DECIMAL(10,2) NOT NULL,
    stock INTEGER NOT NULL DEFAULT 0,
    category_id INTEGER,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    IsDeleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE category (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);

INSERT INTO category (name) VALUES ('電子機器'), ('衣類'), ('食品');
INSERT INTO product (name, price, stock, category_id) VALUES
    ('スマートフォン', 89800, 50, 1),
    ('ノートパソコン', 128000, 20, 1),
    ('Tシャツ', 2980, 100, 2);
EOF
```

### 2.4 アプリを再起動して確認

```bash
dotnet run --project ./NetYamlForge/NetYamlForge.csproj
```

`http://localhost:5239/shop/Dashboard` でダッシュボードを確認。

---

## ステップ3: 既存DBからエンティティ生成

### 3.1 スキャフォールドコマンド

既存のDBテーブルからYAMLを自動生成:

```bash
# entities.generated/ に生成（デフォルト）
dotnet run -- --scaffold-entities --project=shop

# entities/ に生成（管理者編集用）
dotnet run -- --scaffold-entities --project=shop --output-dir=entities

# 既存ファイルを上書きしない
dotnet run -- --scaffold-entities --project=shop --no-overwrite
```

### 3.2 生成されたYAMLの確認

`projects/shop/entities.generated/product.yml`:
```yaml
entities:
  product:
    table: product
    key: id
    displayName: 'Product'
    softDelete: false         # IsDeleted 列を確認後 true に変更する
    paging:
      pageSize: 20
      mode: numbered
    columns:
      id: { type: int, identity: true, label: 'Id', sortable: true }
      name: { type: string, required: true, label: 'Name', searchable: true, sortable: true }
      price: { type: decimal, required: true, label: 'Price', sortable: true }
      stock: { type: int, required: true, label: 'Stock', sortable: true }
      category_id: { type: int, label: 'Category', sortable: true }
      is_active: { type: int, label: 'Is Active', sortable: true }
      created_at: { type: string, label: 'Created At', sortable: true }
    forms:
      # forms はフィールド名をキーにしたフラットマップ形式
      name: { type: string, required: true, label: 'Name', editable: true }
      price: { type: decimal, required: true, label: 'Price', editable: true }
      stock: { type: int, required: true, label: 'Stock', editable: true }
      category_id: { type: int, label: 'Category', editable: true }
      is_active: { type: int, label: 'Is Active', editable: true }
    filters: {}
    links: {}
```

> **ポイント**: `forms` セクションはフィールド名をキーにしたフラットマップ形式です。
> `forms.create.fields` のような階層構造は使用しません。

---

## ステップ4: エンティティをカスタマイズ

`entities.generated/` は自動生成されるため編集しない。
`entities/product.yml` を作成して上書き定義する。

### 4.1 基本カスタマイズ

`projects/shop/entities/product.yml`:
```yaml
entities:
  product:
    table: product
    key: id

    # 表示名（日本語化）
    displayNameI18n:
      ja-JP: 商品
      en-US: Product

    # ソフトデリート有効（IsDeleted列で論理削除）
    softDelete: true

    # 一覧表示列の定義
    columns:
      id:
        type: int
        identity: true          # 自動採番（一覧表示のみ、フォームには出さない）
      name:
        type: string
        label: 商品名
        searchable: true        # 全文検索対象
        sortable: true          # ソート可能
      price:
        type: decimal
        label: 価格
        sortable: true
      stock:
        type: int
        label: 在庫数
      is_active:
        type: int
        label: 公開中
      created_at:
        type: string
        label: 登録日時
        sortable: true

    # フォームフィールドの定義
    forms:
      name:
        type: string
        label: 商品名
        required: true
      price:
        type: decimal
        label: 価格（円）
        required: true
      stock:
        type: int
        label: 在庫数
        required: true
      is_active:
        type: boolean           # チェックボックス（bool / boolean が有効）
        label: 公開中
      category_id:
        type: int               # FK設定後はドロップダウンになる
        label: カテゴリ

    # ページング設定
    paging:
      pageSize: 20
      mode: numbered            # numbered（番号付き）or keyset（無限スクロール）
      enableCount: true         # COUNT(*)クエリを実行するか
```

### 4.2 列の表示順序の制御

```yaml
layout:
  forms:
    columns: 2                  # フォームを2カラムレイアウト
    order:                      # フィールドの表示順
      - name
      - price
      - stock
      - category_id
      - is_active

  filters:
    columns: 4                  # フィルタバーを4カラム
    order:
      - category
      - is_active
```

### 4.3 確認ダイアログの設定

```yaml
confirmation:
  create: "この商品を登録してもよいですか？"
  update: "変更を保存してもよいですか？"
  delete: "この商品を削除してもよいですか？この操作は元に戻せません。"
```

---

## ステップ5: 外部キー（FK）の設定

### 5.1 シンプルなドロップダウン

```yaml
forms:
  category_id:
    type: select
    label: カテゴリ
    foreignKey:
      entity: category          # 参照先エンティティ名
      displayColumn: name       # ドロップダウンに表示する列名
```

### 5.2 複数列を結合して表示

```yaml
forms:
  author_id:
    type: select
    label: 著者
    foreignKey:
      entity: author
      displayColumns: [first_name, last_name]  # "田中 太郎" のように表示
```

### 5.3 カスタムSQLで絞り込み

```yaml
forms:
  active_category_id:
    type: select
    label: カテゴリ
    foreignKey:
      entity: category
      displayColumn: name
      query: "SELECT id AS Id, name FROM category WHERE is_active = 1 ORDER BY name"
```

### 5.4 ピッカーモーダル（大量データ対応）

ドロップダウンでは扱いにくい大量データ（100件以上）の場合:

```yaml
forms:
  product_id:
    type: string
    label: 商品
    foreignKey:
      entity: product
      displayColumns: [name, price]
      picker: true              # ドロップダウンではなくモーダルで選択
```

### 5.5 一覧での外部キー表示（JOIN）

カテゴリ名を一覧で表示するにはJOINが必要:

```yaml
joins:
  - table: category
    alias: cat
    on: "product.category_id = cat.id"
    type: left

columns:
  category_name:
    type: string
    label: カテゴリ名
    expression: "cat.name"      # JOIN先の列を表示
    sortable: true
```

---

## ステップ6: フィルターの追加

### 6.1 ドロップダウンフィルター（固定値）

```yaml
filters:
  # フィルターキー名 = SQLで使う列名（expression 未設定時は "テーブル名.キー名" が使われる）
  is_active:
    type: dropdown
    label: 公開状態
    options:
      - "1"
      - "0"

  # 表示テキストと実DB値が異なる場合は expression + options を組み合わせる
  # （フィルターキーと列名が異なる場合は expression で明示する）
  status:
    type: dropdown
    label: 公開状態
    expression: "product.is_active"   # フィルタ対象の実列名
    options:
      - 公開中
      - 非公開
```

### 6.2 外部キードロップダウンフィルター

```yaml
filters:
  # フィルターキー名をそのまま列名として使う場合: キー名 = 列名
  category_id:
    type: dropdown
    label: カテゴリ
    foreignKey:
      entity: category
      displayColumn: name

  # フィルターキー名と列名が異なる場合は expression で指定する
  category:
    type: dropdown
    label: カテゴリ
    expression: "product.category_id"   # 実際のDB列名を明示
    foreignKey:
      entity: category
      displayColumn: name
```

> **重要**: `column: xxx` プロパティは存在しません。フィルターのSQL列名は
> `expression` プロパティで指定するか、フィルターキー名と列名を一致させてください。

### 6.3 数値範囲フィルター

```yaml
filters:
  price:
    type: range
    label: 価格帯
    # URLパラメータ: ?price_min=1000&price_max=50000
```

### 6.4 日付範囲フィルター

```yaml
filters:
  created_at:
    type: date-range
    label: 登録日
    # URLパラメータ: ?created_at_from=2024-01-01&created_at_to=2024-12-31
```

### 6.5 複数選択フィルター

```yaml
filters:
  category:
    type: multi-select
    label: カテゴリ（複数選択可）
    foreignKey:
      entity: category
      displayColumn: name
```

---

## ステップ7: バリデーションフックの追加

YAMLの `hooks` セクションで組み込みフックを設定するだけでバリデーション可能。コード不要。

### 7.1 必須チェック

```yaml
hooks:
  beforeCreate:
    - validate_required:name,price,stock
  beforeUpdate:
    - validate_required:name,price,stock
```

### 7.2 数値範囲チェック

```yaml
hooks:
  beforeCreate:
    - validate_range:price:min=0,max=9999999
    - validate_range:stock:min=0,max=100000
  beforeUpdate:
    - validate_range:price:min=0,max=9999999
    - validate_range:stock:min=0,max=100000
```

### 7.3 文字列自動整形

```yaml
hooks:
  beforeCreate:
    - trim:name                 # 前後の空白を除去
    - now:created_at            # 現在時刻を自動設定
  beforeUpdate:
    - trim:name
```

### 7.4 プリセットで共通化

```yaml
hooks:
  presets:
    productValidation:
      - validate_required:name,price,stock
      - validate_range:price:min=0,max=9999999
      - validate_range:stock:min=0,max=100000
      - trim:name

  beforeCreate:
    - "@productValidation"
    - now:created_at

  beforeUpdate:
    - "@productValidation"
```

---

## ステップ8: カスタムフックの実装

組み込みフックで対応できない場合はC#でカスタムフックを実装する。

### 8.1 スキャフォールドコマンドで雛形生成

```bash
dotnet run -- --scaffold-hook --name=ValidateStock --project=shop --with-tests
```

生成ファイル:
- `projects/shop/Hooks/ValidateStockHook.cs`
- `NetYamlForge.Tests/Hooks/ValidateStockHookTests.cs`

### 8.2 生成された雛形の確認

`projects/shop/Hooks/ValidateStockHook.cs`:
```csharp
using System.Data;
using NetYamlForge.Services.Hooks;

namespace NetYamlForge.Projects.Shop.Hooks;

/// <summary>
/// ValidateStock フック: 在庫数の業務バリデーション
/// YAML での使用例:
///   hooks:
///     beforeCreate:
///       - validate_stock
/// </summary>
public class ValidateStockHook : IEntityHook
{
    private readonly ILogger<ValidateStockHook> _logger;

    public ValidateStockHook(ILogger<ValidateStockHook> logger)
    {
        _logger = logger;
    }

    public string Name => "validate_stock";

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // ここにバリデーションロジックを実装
        // ─ ctx.Values: フォームから送信された値（変換済み）
        // ─ ctx.Operation: Create / Update / Delete
        // ─ ctx.Id: Update/Delete時の主キー値
        // ─ db: DB接続（読み取りクエリに使用可）

        // 例: 在庫数が負の場合はエラー
        if (ctx.Values.TryGetValue("stock", out var stockRaw) &&
            int.TryParse(stockRaw?.ToString(), out var stock) &&
            stock < 0)
        {
            return HookResult.Abort("在庫数は0以上で入力してください。");
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // DB書き込み後の処理（同一トランザクション内）
        // ─ ここで例外を投げるとトランザクションがロールバックされる
        return Task.CompletedTask;
    }
}
```

### 8.3 実践的なカスタムフック例

**価格変更時に価格履歴を記録するフック**:

```csharp
public class RecordPriceHistoryHook : IEntityHook
{
    public string Name => "record_price_history";

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // Update時のみ処理（Create時はスキップ）
        if (ctx.Operation != CrudOperation.Update || ctx.Id == null)
            return HookResult.Continue();

        // 変更前の価格を取得してコンテキストに保存
        var oldPrice = await db.ExecuteScalarAsync<decimal?>(
            "SELECT price FROM product WHERE id = @Id",
            new { Id = ctx.Id });

        // AfterAsyncで参照するために保存
        ctx.Data["old_price"] = oldPrice;

        return HookResult.Continue();
    }

    public async Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        // Update時のみ処理
        if (ctx.Operation != CrudOperation.Update || ctx.Id == null)
            return;

        var oldPrice = ctx.Data.TryGetValue("old_price", out var op) ? op : null;
        var newPrice = ctx.Values.TryGetValue("price", out var np) ? np : null;

        // 価格が変更された場合のみ記録
        if (oldPrice?.ToString() != newPrice?.ToString())
        {
            // 同一トランザクション内でINSERT（tx を渡すことが重要）
            await db.ExecuteAsync(
                "INSERT INTO price_history (product_id, old_price, new_price, changed_at, changed_by) " +
                "VALUES (@ProductId, @OldPrice, @NewPrice, @ChangedAt, @ChangedBy)",
                new
                {
                    ProductId = ctx.Id,
                    OldPrice = oldPrice,
                    NewPrice = newPrice,
                    ChangedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ChangedBy = ctx.UserName
                },
                tx);  // ← tx を渡す（コミット前に記録）
        }
    }
}
```

### 8.4 フックの登録

`Program.cs` でDI登録（スキャフォールドコマンドが自動で追加することもある）:

```csharp
// DI登録: プロジェクト固有フックの場合
services.AddSingleton<IEntityHook, ValidateStockHook>();
services.AddSingleton<IEntityHook, RecordPriceHistoryHook>();
```

プロジェクト固有フックは `projects/shop/Hooks/` に置くと動的ロードされるため、
`Program.cs` の変更不要な場合もある。

### 8.5 YAMLに設定を追加

```yaml
hooks:
  beforeUpdate:
    - validate_stock
    - record_price_history
```

---

## ステップ9: ダッシュボードの設定

`projects/shop/config/dashboard.yml`:

```yaml
stats:
  # シンプルなカウント
  - id: total_products
    label: 商品総数
    entity: product
    type: count

  # 条件付きカウント
  - id: active_products
    label: 公開商品数
    entity: product
    type: count
    filter: "is_active = 1"

  # 合計値
  - id: total_stock_value
    label: 在庫総額
    entity: product
    type: sum
    column: price                  # SUM(price)
    filter: "is_active = 1"
    format: "¥{0:N0}"             # 表示フォーマット

  # 平均値
  - id: avg_price
    label: 平均価格
    entity: product
    type: avg
    column: price
    format: "¥{0:N0}"

  # クリック時のリンク先
  - id: low_stock
    label: 在庫少商品
    entity: product
    type: count
    filter: "stock < 10 AND is_active = 1"
    link:
      entity: product
      filter:
        stock_max: "10"            # ?stock_max=10 をURLに付与

charts:
  # カテゴリ別商品数（棒グラフ）
  - id: products_by_category
    title: カテゴリ別商品数
    type: bar                      # bar / line / doughnut / pie
    source: |
      SELECT c.name AS label, COUNT(p.id) AS value
      FROM product p
      LEFT JOIN category c ON p.category_id = c.id
      WHERE p.IsDeleted = 0
      GROUP BY c.name
      ORDER BY value DESC

  # 月別登録数（折れ線グラフ）
  - id: monthly_registrations
    title: 月別登録数
    type: line
    source: |
      SELECT strftime('%Y-%m', created_at) AS label,
             COUNT(*) AS value
      FROM product
      WHERE IsDeleted = 0
      GROUP BY label
      ORDER BY label DESC
      LIMIT 12
```

---

## ステップ10: 複合主キーへの対応

注文明細のように2つの列で主キーを構成する場合:

### 10.1 DBテーブル

```sql
CREATE TABLE order_item (
    order_id INTEGER NOT NULL,
    product_id INTEGER NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price DECIMAL(10,2) NOT NULL,
    PRIMARY KEY (order_id, product_id)
);
```

### 10.2 YAML設定

```yaml
entities:
  order_item:
    table: order_item
    # 複合主キー: key の代わりに keys を使う
    keys:
      - order_id
      - product_id
    displayName: 注文明細

    columns:
      order_id:
        type: int
        label: 注文ID
      product_id:
        type: int
        label: 商品ID
      quantity:
        type: int
        label: 数量
      unit_price:
        type: decimal
        label: 単価

    forms:
      order_id:
        type: int
        label: 注文ID
        required: true
      product_id:
        type: select
        label: 商品
        required: true
        foreignKey:
          entity: product
          displayColumn: name
      quantity:
        type: int
        label: 数量
        required: true
      unit_price:
        type: decimal
        label: 単価
        required: true
```

---

## ステップ11: 多言語対応（i18n）

### 11.1 エンティティ・フィールドラベルの多言語化

```yaml
entities:
  product:
    table: product
    key: id

    # エンティティ表示名の多言語化
    displayNameI18n:
      ja-JP: 商品
      en-US: Product
      zh-CN: 商品

    columns:
      name:
        type: string
        # フィールドラベルの多言語化
        labelI18n:
          ja-JP: 商品名
          en-US: Product Name
          zh-CN: 商品名称
```

### 11.2 YAMLファイルによるi18n設定

`projects/shop/config/i18n.yml`:
```yaml
# キーベースの翻訳
keys:
  "entity.product.label":
    ja-JP: 商品
    en-US: Product
  "entity.product.name.label":
    ja-JP: 商品名
    en-US: Product Name
```

### 11.3 言語切り替えUI

ヘッダーの言語ボタンをクリックするか、URLパラメータで指定:
```
?lang=ja-JP
?lang=en-US
?lang=zh-CN
```

---

## ステップ12: SQL Serverへの移行

### 12.1 接続設定

`projects/shop/project.yaml`:
```yaml
database:
  type: sqlserver
  connectionString: "Server=localhost;Database=ShopDB;User Id=sa;Password=YourPassword;TrustServerCertificate=true"
```

または `appsettings.json`:
```json
{
  "DatabaseProvider": "sqlserver",
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=...;"
  }
}
```

### 12.2 SQL Server固有のYAML上書き

SQLiteと式の書き方が異なる場合に使用:

`projects/shop/entities-sqlserver/product.yml`:
```yaml
entities:
  product:
    table: product
    key: id
    displayName: 商品
    columns:
      full_info:
        type: string
        label: 商品情報
        # SQLite: || で文字列結合
        # SQL Server: + で文字列結合
        expression: "product.name + ' (' + CAST(product.price AS NVARCHAR) + '円)'"
```

---

## よくある問題と解決策

### Q1: 起動時に "No entity yaml found" エラー

**原因**: `entities/` ディレクトリが空または存在しない

**解決策**:
```bash
# スキャフォールドコマンドでYAMLを生成
dotnet run -- --scaffold-entities --project=<project名>
```

### Q2: 一覧画面で列が表示されない

**確認点**:
1. `columns` セクションに列が定義されているか
2. JOIN定義で別名（alias）が正しいか
3. `expression` の列名がJOINエイリアスと一致しているか

```yaml
# ❌ 誤り: aliasが一致しない
joins:
  - table: category
    alias: cat
    on: "product.category_id = cat.id"
columns:
  category_name:
    expression: "category.name"    # ← aliasは "cat" なのに "category" を使っている

# ✅ 正しい
columns:
  category_name:
    expression: "cat.name"         # ← aliasと一致させる
```

### Q3: フックが実行されない

**確認点**:
1. YAML の `hooks` セクションのキー名が camelCase か
2. フック名のスペルが正しいか
3. `Program.cs` に DI登録されているか

```yaml
# ❌ 誤り: キー名がスネークケース
hooks:
  before_create:           # ← 誤り

# ✅ 正しい: camelCase
hooks:
  beforeCreate:            # ← 正しい
    - validate_email
```

### Q4: 外部キードロップダウンが空

**確認点**:
1. 参照先エンティティの `displayColumn` が実際の列名と一致しているか
2. 参照先テーブルにデータが存在するか
3. 外部キーエンティティ自体がYAMLに定義されているか

### Q5: ソフトデリートしたデータが一覧に残る

**原因**: テーブルに `IsDeleted` 列が存在しない

**解決策**:
```sql
ALTER TABLE product ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;
```

### Q6: ページングのCOUNTクエリが遅い

大量データの場合は COUNT を無効化:
```yaml
paging:
  pageSize: 50
  enableCount: false        # COUNT(*)クエリをスキップ
```

または キーセットページングに切り替え:
```yaml
paging:
  mode: keyset
  pageSize: 50
```

---

## YAML全プロパティリファレンス

### EntityDefinition（エンティティ定義）

| プロパティ | 型 | 必須 | 説明 |
|-----------|---|------|------|
| `table` | string | ✅ | DBテーブル名（変更禁止） |
| `key` | string | ✅* | 単一主キー列名（`keys` と排他） |
| `keys` | list | ✅* | 複合主キー列名リスト（`key` と排他） |
| `displayName` | string | ✅ | UI表示名 |
| `displayNameKey` | string | | i18nリソースキー |
| `displayNameI18n` | map | | 多言語表示名マップ |
| `softDelete` | bool | | true で論理削除（IsDeleted列が必要） |
| `isPublic` | bool | | false で Admin のみアクセス可 |
| `columns` | map | | 一覧表示列の定義 |
| `forms` | map | | フォームフィールドの定義 |
| `filters` | map | | フィルターの定義 |
| `joins` | list | | SQL JOINの定義 |
| `links` | map | | 他エンティティへのリンク |
| `paging` | object | | ページング設定 |
| `layout` | object | | レイアウト設定 |
| `hooks` | object | | フック設定 |
| `confirmation` | object | | 確認ダイアログメッセージ |

### ColumnDefinition / FormDefinition

| プロパティ | 型 | 説明 |
|-----------|---|------|
| `type` | string | フィールド型（string/int/decimal/date/datetime/email/textarea/select/checkbox/bool-toggle/toggle-group/radio/hidden） |
| `label` | string | UIラベル |
| `labelKey` | string | i18nリソースキー |
| `labelI18n` | map | 多言語ラベルマップ |
| `required` | bool | 必須入力フラグ（フォームのみ） |
| `identity` | bool | 自動採番列フラグ（INSERT/UPDATE対象外） |
| `editable` | bool | false で編集フォームで読み取り専用（デフォルト: true） |
| `searchable` | bool | 全文検索対象フラグ（列定義のみ） |
| `sortable` | bool | ソート可能フラグ |
| `hidden` | bool | UI非表示フラグ |
| `expression` | string | 計算列・JOIN列のSQL式 |
| `options` | list | select/toggle-group等の選択肢リスト |
| `foreignKey` | object | 外部キー設定 |

### FilterDefinition

| プロパティ | 型 | 説明 |
|-----------|---|------|
| `type` | string | dropdown/range/date-range/checkbox/multi-select |
| `label` | string | フィルターラベル |
| `expression` | string | フィルター対象のSQL式 |
| `options` | list | 固定値リスト（dropdown/checkboxで使用） |
| `foreignKey` | object | 外部キードロップダウン設定 |

### ForeignKeyDefinition

| プロパティ | 型 | 説明 |
|-----------|---|------|
| `entity` | string | 参照先エンティティ名 |
| `displayColumn` | string | ドロップダウン表示列名 |
| `displayColumns` | list | 複数列を結合して表示 |
| `query` | string | カスタムSELECT（Id列を含む必要あり） |
| `picker` | bool | true でモーダルピッカーを使用 |
| `multiPicker` | bool | true で複数選択ピッカーを使用 |

### PagingDefinition

| プロパティ | 型 | デフォルト | 説明 |
|-----------|---|---------|------|
| `pageSize` | int | 5 | 1ページあたりの件数 |
| `mode` | string | numbered | numbered/keyset |
| `enableCount` | bool | true | COUNT(*)クエリの実行フラグ |

### EntityHooksDefinition

| プロパティ | 型 | 説明 |
|-----------|---|------|
| `beforeCreate` | string or list | 新規作成前フック |
| `afterCreate` | string or list | 新規作成後フック |
| `beforeUpdate` | string or list | 更新前フック |
| `afterUpdate` | string or list | 更新後フック |
| `beforeDelete` | string or list | 削除前フック |
| `afterDelete` | string or list | 削除後フック |
| `presets` | map | 再利用可能なフックグループ定義 |

---

## 次のステップ

- [アーキテクチャ詳解](./annotated-architecture-ja.md) — コアコードの内部実装を理解する
- [共通フック一覧](./COMMON_HOOKS.md) — 組み込みフック20種以上の詳細
- [プロジェクト固有フックガイド](./project-hooks-guide.md) — カスタムフックの高度な実装
- [ダッシュボード詳細](./dashboard.md) — チャートとKPI集計の詳細設定
- [カスタムページ構築](./ui/custom-pages-ja.md) — 複雑なビジネス画面の作り方
