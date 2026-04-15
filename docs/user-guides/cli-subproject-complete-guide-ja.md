# CLI によるサブプロジェクト開発完全ガイド

NetYamlForge の CLI スキャフォールド機能を使用して、ゼロから実用的なサブプロジェクトを構築する完全ガイドです。

---

## 目次

1. [チュートリアル概要](#チュートリアル概要)
2. [事前準備](#事前準備)
3. [ステップ 1: プロジェクトの初期化](#ステップ 1-プロジェクトの初期化)
4. [ステップ 2: データベース設計](#ステップ 2-データベース設計)
5. [ステップ 3: エンティティ YAML のカスタマイズ](#ステップ 3-エンティティ yaml-のカスタマイズ)
6. [ステップ 4: レイアウトとナビゲーション](#ステップ 4-レイアウトとナビゲーション)
7. [ステップ 5: ダッシュボードのカスタマイズ](#ステップ 5-ダッシュボードのカスタマイズ)
8. [ステップ 6: フックの実装](#ステップ 6-フックの実装)
9. [ステップ 7: カスタムページの作成](#ステップ 7-カスタムページの作成)
10. [ステップ 8: 多言語対応](#ステップ 8-多言語対応)
11. [完成後の確認事項](#完成後の確認事項)
12. [トラブルシューティング](#トラブルシューティング)

---

## チュートリアル概要

### 作成するアプリケーション

**在庫管理システム (Inventory Management System)**

- 商品管理 (Products)
- カテゴリ管理 (Categories)
- 在庫移動管理 (StockMovements)
- 売上集計ダッシュボード

### 学ぶこと

- CLI によるプロジェクト初期化
- データベース設計とスキャフォールド
- YAML 設定のカスタマイズ
- フックによるビジネスロジック実装
- カスタムページの作成
- 多言語対応 (i18n)

### 所要時間

- 初回：約 60 分
- 慣れ後：約 15 分

---

## 事前準備

### 1. 開発環境

```bash
# .NET 10.0 SDK の確認
dotnet --version

# Git の確認
git --version
```

### 2. プロジェクトルートの確認

```bash
# 作業ディレクトリに移動
cd /path/to/NetYamlForge

# projects ディレクトリの存在確認
ls -la projects/
```

---

## ステップ 1: プロジェクトの初期化

### 1.1 基本コマンド

SQLite を使用した最小構成のプロジェクトを作成します：

```bash
dotnet run -- --init-project \
  --project=inventory \
  --display-name="在庫管理システム"
```

**出力例**:
```
[ok] project template created: /path/to/projects/inventory
next: dotnet run -- --scaffold-entities --project=inventory
```

### 1.2 オプションパラメータ

| パラメータ | 説明 | 例 |
|-----------|------|-----|
| `--db-path` | SQLite DB ファイルのパス | `--db-path=data/inventory.db` |
| `--db-type` | データベース種別 | `sqlserver`, `postgresql`, `mysql` |
| `--db-connection` | 接続文字列 (SQLite 以外) | `--db-connection="Server=..."` |
| `--no-auto-scaffold` | 自動スキャフォールドをスキップ | |
| `--force` | 既存ディレクトリを上書き | |

### 1.3 生成されるファイル構造

```
projects/inventory/
├── project.yaml              # プロジェクト定義
├── dashboard.yml             # ダッシュボード設定
├── config/
│   ├── home-page.yml         # ホームページ
│   ├── layout.yml            # レイアウト
│   └── i18n.yml              # 多言語設定
├── database/
│   └── inventory.db          # SQLite DB
├── entities/
│   └── .gitkeep
├── entities.generated/       # 自動生成されたエンティティ
├── pages/
│   └── StarterOverview.yaml  # スターターページ
├── views/
│   ├── _Layout.cshtml        # プロジェクトレイアウト
│   └── StarterOverview.cshtml
└── docs/
    └── README-ja.md
```

---

## ステップ 2: データベース設計

### 2.1 テーブル設計

SQLite で使用するテーブルを作成します：

```sql
-- カテゴリテーブル
CREATE TABLE Categories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 商品テーブル
CREATE TABLE Products (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    CategoryId INTEGER,
    Price DECIMAL(10,2) NOT NULL DEFAULT 0,
    Stock INTEGER NOT NULL DEFAULT 0,
    MinStock INTEGER NOT NULL DEFAULT 10,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
);

-- 在庫移動テーブル
CREATE TABLE StockMovements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    Quantity INTEGER NOT NULL,
    MovementType TEXT NOT NULL,  -- 'IN', 'OUT', 'ADJUST'
    Reason TEXT,
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

-- インデックス作成
CREATE INDEX IX_Products_CategoryId ON Products(CategoryId);
CREATE INDEX IX_StockMovements_ProductId ON StockMovements(ProductId);
CREATE INDEX IX_StockMovements_MovementType ON StockMovements(MovementType);

-- サンプルデータ
INSERT INTO Categories (Name, Description) VALUES 
    ('電子機器', 'スマートフォン、PC 等'),
    ('オフィス用品', '文具、事務機器等'),
    ('家具', 'デスク、チェア等');

INSERT INTO Products (Name, CategoryId, Price, Stock, MinStock) VALUES 
    ('iPhone 15 Pro', 1, 159800, 50, 10),
    ('MacBook Pro 14"', 1, 248800, 30, 5),
    ('ボールペン 10 本セット', 2, 580, 200, 50),
    ('オフィスチェア', 3, 25800, 20, 5);
```

### 2.2 DB ファイルの作成

```bash
# SQLite で DB ファイルを作成
sqlite3 projects/inventory/database/inventory.db < create_tables.sql

# または対話的に作成
sqlite3 projects/inventory/database/inventory.db
```

---

## ステップ 3: エンティティ YAML のカスタマイズ

### 3.1 自動スキャフォールドの実行

DB から YAML を自動生成します：

```bash
dotnet run -- --scaffold-entities --project=inventory
```

### 3.2 商品エンティティのカスタマイズ

`projects/inventory/entities/product.yml` を編集：

```yaml
entities:
  product:
    table: Products
    key: Id
    displayName: 商品
    displayColumn: Name
    softDelete: false
    paging:
      pageSize: 20
      mode: numbered
    layout:
      forms:
        columns: 2
        order:
        - Name
        - CategoryId
        - Price
        - Stock
        - MinStock
        - IsActive
      filters:
        columns: 3
        order:
        - Name
        - CategoryId
        - IsActive
    columns:
      Id:
        type: int
        identity: true
        label: ID
        searchable: false
        sortable: true
      Name:
        type: string
        label: 商品名
        searchable: true
        sortable: true
        labelKey: entities.product.columns.Name.label
      CategoryId:
        type: int
        label: カテゴリ
        foreignKey:
          entity: category
          displayColumn: Name
        labelKey: entities.product.columns.CategoryId.label
      Price:
        type: decimal
        label: 価格
        format: "¥{0:N0}"
        labelKey: entities.product.columns.Price.label
      Stock:
        type: int
        label: 在庫数
        labelKey: entities.product.columns.Stock.label
      MinStock:
        type: int
        label: 最小在庫
        labelKey: entities.product.columns.MinStock.label
      IsActive:
        type: bool
        label: 有効
        labelKey: entities.product.columns.IsActive.label
      CreatedAt:
        type: string
        label: 作成日
        readonly: true
        labelKey: entities.product.columns.CreatedAt.label
      UpdatedAt:
        type: string
        label: 更新日
        readonly: true
        labelKey: entities.product.columns.UpdatedAt.label
    forms:
      Name:
        type: string
        label: 商品名
        editable: true
        required: true
        labelKey: entities.product.forms.Name.label
      CategoryId:
        type: int
        label: カテゴリ
        editable: true
        dropdown: true
        labelKey: entities.product.forms.CategoryId.label
      Price:
        type: decimal
        label: 価格
        editable: true
        required: true
        min: 0
        labelKey: entities.product.forms.Price.label
      Stock:
        type: int
        label: 在庫数
        editable: true
        min: 0
        labelKey: entities.product.forms.Stock.label
      MinStock:
        type: int
        label: 最小在庫
        editable: true
        min: 0
        labelKey: entities.product.forms.MinStock.label
      IsActive:
        type: bool
        label: 有効
        editable: true
        labelKey: entities.product.forms.IsActive.label
    filters:
      Name:
        type: text
        label: 商品名
        expression: Products.Name
        labelKey: entities.product.filters.Name.label
      CategoryId:
        type: select
        label: カテゴリ
        expression: Products.CategoryId
        dropdown: true
        labelKey: entities.product.filters.CategoryId.label
      IsActive:
        type: bool
        label: 有効/無効
        expression: Products.IsActive
        labelKey: entities.product.filters.IsActive.label
    hooks:
      beforeCreate: [validate_product_price, check_stock_threshold]
      beforeUpdate: [validate_product_price, check_stock_threshold, update_timestamp]
      afterCreate: [log_product_change]
      afterUpdate: [log_product_change]
    confirmation:
      create: 新しい商品を登録してよろしいですか？
      update: 商品情報を更新してよろしいですか？
      delete: この商品を削除してもよろしいですか？
```

### 3.3 カテゴリエンティティ

`projects/inventory/entities/category.yml`:

```yaml
entities:
  category:
    table: Categories
    key: Id
    displayName: カテゴリ
    displayColumn: Name
    columns:
      Id:
        type: int
        identity: true
        label: ID
      Name:
        type: string
        label: カテゴリ名
        searchable: true
        sortable: true
      Description:
        type: string
        label: 説明
        multiline: true
      CreatedAt:
        type: string
        label: 作成日
        readonly: true
    forms:
      Name:
        type: string
        label: カテゴリ名
        editable: true
        required: true
      Description:
        type: string
        label: 説明
        editable: true
        multiline: true
    filters:
      Name:
        type: text
        label: カテゴリ名
        expression: Categories.Name
    hooks:
      beforeDelete: [check_category_usage]
```

### 3.4 在庫移動エンティティ

`projects/inventory/entities/stockmovement.yml`:

```yaml
entities:
  stockmovement:
    table: StockMovements
    key: Id
    displayName: 在庫移動
    displayColumn: Reason
    columns:
      Id:
        type: int
        identity: true
        label: ID
      ProductId:
        type: int
        label: 商品
        foreignKey:
          entity: product
          displayColumn: Name
      Quantity:
        type: int
        label: 数量
      MovementType:
        type: string
        label: 移動種別
      Reason:
        type: string
        label: 理由
      CreatedAt:
        type: string
        label: 移動日時
        readonly: true
    forms:
      ProductId:
        type: int
        label: 商品
        editable: true
        required: true
        dropdown: true
      Quantity:
        type: int
        label: 数量
        editable: true
        required: true
      MovementType:
        type: string
        label: 移動種別
        editable: true
        required: true
        options:
          - value: IN
            label: 入荷
          - value: OUT
            label: 出荷
          - value: ADJUST
            label: 調整
      Reason:
        type: string
        label: 理由
        editable: true
        multiline: true
    filters:
      ProductId:
        type: select
        label: 商品
        expression: StockMovements.ProductId
        dropdown: true
      MovementType:
        type: select
        label: 移動種別
        expression: StockMovements.MovementType
        options:
          - value: IN
            label: 入荷
          - value: OUT
            label: 出荷
          - value: ADJUST
            label: 調整
    hooks:
      beforeCreate: [validate_stock_movement, update_product_stock]
      afterCreate: [check_stock_threshold]
```

---

## ステップ 4: レイアウトとナビゲーション

### 4.1 レイアウト設定

`projects/inventory/config/layout.yml` を編集：

```yaml
# 在庫管理システム用レイアウト設定

header:
  title: 在庫管理システム

navigation:
  showDashboard: true
  # 主要エンティティのみ表示
  entities:
    - product
    - category
    - stockmovement
  items:
    - label: ダッシュボード
      controller: Dashboard
      action: Index
      icon: 📊
    - label: 商品一覧
      url: /inventory/DynamicEntity/Index?entity=product
      icon: 📦
    - label: カテゴリ
      url: /inventory/DynamicEntity/Index?entity=category
      icon: 🏷️
    - label: 在庫移動
      url: /inventory/DynamicEntity/Index?entity=stockmovement
      icon: 🔄
    - label: 在庫状況
      url: /inventory/Page/StockStatus
      icon: 📈
```

### 4.2 ホームページ設定

`projects/inventory/config/home-page.yml`:

```yaml
hero:
  eyebrow: 在庫管理システム
  title: 在庫管理ダッシュボード
  description: 商品・在庫・売上を一元的に管理するシステムです。
  primaryActionLabel: 商品一覧へ
  primaryActionUrl: /inventory/DynamicEntity/Index?entity=product
  secondaryActionLabel: ダッシュボード
  secondaryActionUrl: /inventory/Dashboard
  highlights:
    - リアルタイム在庫管理
    - 自動発注アラート
    - 売上分析

projectsSectionTitle: 在庫管理ワークスペース
projectsSectionLead: 商品管理から在庫移動、売上分析までを一元管理。

quickActions:
  - label: 商品一覧
    url: /inventory/DynamicEntity/Index?entity=product
    style: btn-primary
    icon: 📦
  - label: 新規商品登録
    url: /inventory/DynamicEntity/CreatePage?entity=product
    style: btn-accent
    icon: ➕
  - label: 在庫移動
    url: /inventory/DynamicEntity/CreatePage?entity=stockmovement
    style: btn-outline
    icon: 🔄
  - label: 在庫状況
    url: /inventory/Page/StockStatus
    style: btn-outline
    icon: 📈
```

---

## ステップ 5: ダッシュボードのカスタマイズ

### 5.1 統計カードとチャート

`projects/inventory/dashboard.yml`:

```yaml
stats:
  - label: 総商品数
    entity: product
    aggregate: count
    icon: 📦
    color: badge-primary
  - label: 有効商品
    entity: product
    aggregate: count
    filter:
      IsActive: true
    icon: ✅
    color: badge-success
  - label: カテゴリ数
    entity: category
    aggregate: count
    icon: 🏷️
    color: badge-secondary
  - label: 在庫総数
    entity: product
    aggregate: sum
    column: Stock
    icon: 📊
    color: badge-accent
  - label: 在庫金額
    entity: product
    aggregate: custom
    expression: "SUM(Stock * Price)"
    icon: 💰
    color: badge-warning
  - label: 最小在庫割れ
    entity: product
    aggregate: count
    filter:
      Stock: "< MinStock"
    icon: ⚠️
    color: badge-error
  - label: 本月庫移動数
    entity: stockmovement
    aggregate: count
    filter:
      CreatedAt: ">= this_month"
    icon: 🔄
    color: badge-info
  - label: 本月庫移動 (入荷)
    entity: stockmovement
    aggregate: sum
    column: Quantity
    filter:
      MovementType: IN
      CreatedAt: ">= this_month"
    icon: ⬇️
    color: badge-success
  - label: 本月庫移動 (出荷)
    entity: stockmovement
    aggregate: sum
    column: Quantity
    filter:
      MovementType: OUT
      CreatedAt: ">= this_month"
    icon: ⬆️
    color: badge-warning

charts:
  - title: 商品カテゴリ別在庫数
    type: doughnut
    entity: product
    valueAggregate: sum
    valueColumn: Stock
    labelJoinEntity: category
    labelJoinKey: CategoryId
    labelJoinDisplay: Name
    orderBy: value
    orderDir: desc
    limit: 10
    colors:
      - rgba(99, 102, 241, 0.85)
      - rgba(16, 185, 129, 0.85)
      - rgba(245, 158, 11, 0.85)
      - rgba(239, 68, 68, 0.85)
      - rgba(59, 130, 246, 0.85)

  - title: 月別在庫移動推移
    type: line
    entity: stockmovement
    valueAggregate: sum
    valueColumn: Quantity
    groupExpression: strftime('%Y-%m', CreatedAt)
    orderBy: label
    orderDir: asc
    limit: 12
    colorBg: rgba(99, 102, 241, 0.15)
    colorBorder: rgba(99, 102, 241, 1)

  - title: 商品別在庫金額 TOP10
    type: bar
    entity: product
    valueAggregate: custom
    expression: "SUM(Stock * Price)"
    orderBy: value
    orderDir: desc
    limit: 10
    colorBg: rgba(16, 185, 129, 0.7)
    colorBorder: rgba(16, 185, 129, 1)

  - title: 移動種別別集計 (本月)
    type: bar
    entity: stockmovement
    valueAggregate: count
    groupExpression: MovementType
    filter:
      CreatedAt: ">= this_month"
    orderBy: value
    orderDir: desc
    colorBg: rgba(245, 158, 11, 0.7)
    colorBorder: rgba(245, 158, 11, 1)
```

---

## ステップ 6: フックの実装

### 6.1 フックのスキャフォールド

```bash
# 商品価格検証フック
dotnet run -- --scaffold-hook \
  --name=ValidateProductPrice \
  --project=inventory \
  --with-tests

# 在庫閾値チェックフック
dotnet run -- --scaffold-hook \
  --name=CheckStockThreshold \
  --project=inventory \
  --with-tests

# 在庫移動更新フック
dotnet run -- --scaffold-hook \
  --name=UpdateProductStock \
  --project=inventory \
  --with-tests

# カテゴリ使用チェックフック
dotnet run -- --scaffold-hook \
  --name=CheckCategoryUsage \
  --project=inventory \
  --with-tests
```

### 6.2 フック実装例

`projects/inventory/Hooks/ValidateProductPriceHook.cs`:

```csharp
// 責務: 商品価格のバリデーションを行うフック
// entities.yml の hooks.beforeCreate / hooks.beforeUpdate で使用

using System.Data;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 在庫管理フック：商品価格の検証
/// - 0 以上の価格のみ許可
/// - 上限 1,000,000 円
/// </summary>
public sealed class ValidateProductPriceHook : IEntityHook
{
    private readonly ILogger<ValidateProductPriceHook> _logger;

    public string Name => "validate_product_price";

    public ValidateProductPriceHook(ILogger<ValidateProductPriceHook> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create && ctx.Operation != CrudOperation.Update)
            return Task.FromResult(HookResult.Continue());

        // 価格フィールドの検証
        if (ctx.Values.TryGetValue("Price", out var priceObj) && priceObj is decimal price)
        {
            if (price < 0)
            {
                _logger.LogWarning("商品価格が負の値です：{Price}", price);
                return Task.FromResult(HookResult.Abort("価格は 0 以上である必要があります。"));
            }

            if (price > 1000000)
            {
                _logger.LogWarning("商品価格が上限を超えています：{Price}", price);
                return Task.FromResult(HookResult.Abort("価格の上限は 1,000,000 円です。"));
            }
        }

        return Task.FromResult(HookResult.Continue());
    }

    /// <inheritdoc />
    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
```

`projects/inventory/Hooks/CheckStockThresholdHook.cs`:

```csharp
// 責務: 在庫閾値をチェックするフック

using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 在庫管理フック：最小在庫閾値の警告
/// 在庫が最小在庫を下回った場合に警告をログ出力
/// </summary>
public sealed class CheckStockThresholdHook : IEntityHook
{
    private readonly ILogger<CheckStockThresholdHook> _logger;
    private readonly IDbConnection _db;

    public string Name => "check_stock_threshold";

    public CheckStockThresholdHook(ILogger<CheckStockThresholdHook> logger, IDbConnection db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return HookResult.Continue();
    }

    public async Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create && ctx.Operation != CrudOperation.Update)
            return;

        // 商品エンティティのみ処理
        if (ctx.Entity != "product")
            return;

        // 商品 ID から現在の商品情報を取得
        if (ctx.Id is int productId)
        {
            var sql = @"
                SELECT p.Name, p.Stock, p.MinStock, c.Name as CategoryName
                FROM Products p
                LEFT JOIN Categories c ON p.CategoryId = c.Id
                WHERE p.Id = @ProductId";

            var product = await (tx != null ?
                db.QueryFirstOrDefaultAsync<ProductRow>(sql, new { ProductId = productId }, tx) :
                db.QueryFirstOrDefaultAsync<ProductRow>(sql, new { ProductId = productId }));

            if (product != null && product.Stock < product.MinStock)
            {
                _logger.LogWarning(
                    "[在庫アラート] 商品「{ProductName}」(カテゴリ：{CategoryName}) の在庫が閾値を下回っています。現在：{Stock}, 最小：{MinStock}",
                    product.Name, product.CategoryName ?? "未設定", product.Stock, product.MinStock);

                // 必要に応じてメール通知や外部システム連携を実装
            }
        }
    }

    private class ProductRow
    {
        public string Name { get; set; } = "";
        public int Stock { get; set; }
        public int MinStock { get; set; }
        public string? CategoryName { get; set; }
    }
}
```

`projects/inventory/Hooks/UpdateProductStockHook.cs`:

```csharp
// 責務: 在庫移動時に商品在庫を更新するフック

using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 在庫管理フック：在庫移動に基づく商品在庫数の自動更新
/// </summary>
public sealed class UpdateProductStockHook : IEntityHook
{
    private readonly ILogger<UpdateProductStockHook> _logger;

    public string Name => "update_product_stock";

    public UpdateProductStockHook(ILogger<UpdateProductStockHook> logger)
    {
        _logger = logger;
    }

    public Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Create)
            return Task.FromResult(HookResult.Continue());

        // 在庫移動のバリデーション
        if (!ctx.Values.TryGetValue("ProductId", out var productIdObj) || productIdObj is not int productId)
            return Task.FromResult(HookResult.Abort("商品 ID が指定されていません。"));

        if (!ctx.Values.TryGetValue("Quantity", out var quantityObj) || quantityObj is not int quantity)
            return Task.FromResult(HookResult.Abort("数量が指定されていません。"));

        if (!ctx.Values.TryGetValue("MovementType", out var movementTypeObj) || movementTypeObj is not string movementType)
            return Task.FromResult(HookResult.Abort("移動種別が指定されていません。"));

        // 移動種別による数量の符号決定
        int stockChange = movementType switch
        {
            "IN" => quantity,      // 入荷：増加
            "OUT" => -quantity,    // 出荷：減少
            "ADJUST" => quantity,  // 調整：指定値
            _ => 0
        };

        // 現在の在庫数を取得
        var currentStockSql = "SELECT Stock FROM Products WHERE Id = @ProductId";
        var currentStock = db.ExecuteScalar<int?>(currentStockSql, new { ProductId = productId }, tx) ?? 0;

        var newStock = currentStock + stockChange;

        if (newStock < 0)
        {
            _logger.LogWarning("在庫数が負になります。商品 ID: {ProductId}, 現在：{CurrentStock}, 変更：{StockChange}", 
                productId, currentStock, stockChange);
            return Task.FromResult(HookResult.Abort($"在庫不足です。現在：{currentStock}, 必要：{quantity}"));
        }

        // 商品在庫を更新
        var updateSql = "UPDATE Products SET Stock = @Stock, UpdatedAt = datetime('now') WHERE Id = @ProductId";
        db.Execute(updateSql, new { Stock = newStock, ProductId = productId }, tx);

        _logger.LogInformation(
            "商品 ID {ProductId} の在庫を更新：{Before} → {After}",
            productId, currentStock, newStock);

        return Task.FromResult(HookResult.Continue());
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
```

`projects/inventory/Hooks/CheckCategoryUsageHook.cs`:

```csharp
// 責務: カテゴリ削除時に使用状況をチェックするフック

using System.Data;
using Dapper;
using NetYamlForge.Services.Hooks;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Projects.Inventory.Hooks;

/// <summary>
/// 在庫管理フック：カテゴリ削除時の使用チェック
/// 関連商品がある場合は削除を防止
/// </summary>
public sealed class CheckCategoryUsageHook : IEntityHook
{
    private readonly ILogger<CheckCategoryUsageHook> _logger;

    public string Name => "check_category_usage";

    public CheckCategoryUsageHook(ILogger<CheckCategoryUsageHook> logger)
    {
        _logger = logger;
    }

    public async Task<HookResult> BeforeAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        if (ctx.Operation != CrudOperation.Delete)
            return HookResult.Continue();

        if (ctx.Id is int categoryId)
        {
            // 関連する商品数をチェック
            var sql = "SELECT COUNT(*) FROM Products WHERE CategoryId = @CategoryId";
            var productCount = await db.ExecuteScalarAsync<int>(sql, new { CategoryId = categoryId }, tx);

            if (productCount > 0)
            {
                _logger.LogWarning(
                    "カテゴリ {CategoryId} には {ProductCount} 件の商品が関連しています。",
                    categoryId, productCount);
                return HookResult.Abort($"このカテゴリには {productCount} 件の商品が関連しているため削除できません。");
            }
        }

        return HookResult.Continue();
    }

    public Task AfterAsync(
        EntityHookContext ctx,
        IDbConnection db,
        IDbTransaction? tx)
    {
        return Task.CompletedTask;
    }
}
```

### 6.3 DI 登録

`NetYamlForge/Program.cs` の `AddNetYamlForge` メソッド後に追加：

```csharp
// Inventory プロジェクト用フック登録
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.ValidateProductPriceHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.CheckStockThresholdHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.UpdateProductStockHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.CheckCategoryUsageHook>();
```

---

## ステップ 7: カスタムページの作成

### 7.1 在庫状況ページ

`projects/inventory/pages/StockStatus.yaml`:

```yaml
title: 在庫状況
description: 商品別の在庫状況とアラートを一覧表示
main_table: Products

ui:
  page:
    layout: single
    density: comfortable

sections:
  # 在庫アラートセクション
  - id: stock_alerts
    title: ⚠️ 最小在庫割れアラート
    source_type: custom
    source: |
      SELECT 
        p.Id,
        p.Name as ProductName,
        c.Name as CategoryName,
        p.Stock as CurrentStock,
        p.MinStock as MinStock,
        p.MinStock - p.Stock as Shortage
      FROM Products p
      LEFT JOIN Categories c ON p.CategoryId = c.Id
      WHERE p.IsActive = 1 AND p.Stock < p.MinStock
      ORDER BY Shortage DESC
    columns:
      - ProductName
      - CategoryName
      - CurrentStock
      - MinStock
      - Shortage
    page_size: 10
    editable: false
    read_only: true
    ui:
      component: DataTable
      selectable: none
      rowClass: "bg-error/10"

  # 商品別在庫一覧
  - id: product_stock_list
    title: 📦 商品別在庫一覧
    source_type: custom
    source: |
      SELECT 
        p.Id,
        p.Name as ProductName,
        c.Name as CategoryName,
        p.Price as UnitPrice,
        p.Stock as Stock,
        p.Stock * p.Price as StockValue,
        CASE 
          WHEN p.Stock = 0 THEN '在庫切れ'
          WHEN p.Stock < p.MinStock THEN '在庫不足'
          WHEN p.Stock < p.MinStock * 2 THEN '要注意'
          ELSE '正常'
        END as StockStatus
      FROM Products p
      LEFT JOIN Categories c ON p.CategoryId = c.Id
      WHERE p.IsActive = 1
      ORDER BY StockValue DESC
    page_size: 20
    editable: false
    read_only: true
    ui:
      component: DataTable
      selectable: single
      rowClassByField:
        StockStatus:
          "在庫切れ": "bg-error/20"
          "在庫不足": "bg-warning/20"
          "要注意": "bg-info/20"
          "正常": ""

  # カテゴリ別集計
  - id: category_summary
    title: 🏷️ カテゴリ別集計
    source_type: custom
    source: |
      SELECT 
        c.Name as CategoryName,
        COUNT(p.Id) as ProductCount,
        SUM(p.Stock) as TotalStock,
        SUM(p.Stock * p.Price) as TotalValue,
        AVG(p.Price) as AvgPrice
      FROM Categories c
      LEFT JOIN Products p ON c.Id = p.CategoryId AND p.IsActive = 1
      GROUP BY c.Id, c.Name
      ORDER BY TotalValue DESC
    columns:
      - CategoryName
      - ProductCount
      - TotalStock
      - TotalValue
      - AvgPrice
    page_size: 10
    editable: false
    read_only: true
    ui:
      component: DataTable
      selectable: none
```

### 7.2 カスタムビュー

`projects/inventory/views/StockStatus.cshtml`:

```csharp
@model Dictionary<string, (IEnumerable<Dictionary<string, object>> Rows, int Total)>
@{
    var title = ViewData["Title"]?.ToString() ?? "在庫状況";
}
<div class="space-y-6">
    <div class="flex justify-between items-center">
        <h1 class="text-2xl font-bold">@title</h1>
        <div class="flex gap-2">
            <a href="/inventory/DynamicEntity/CreatePage?entity=stockmovement" 
               class="btn btn-primary btn-sm">
                🔄 在庫移動
            </a>
            <a href="/inventory/DynamicEntity/CreatePage?entity=product" 
               class="btn btn-accent btn-sm">
                ➕ 新規商品
            </a>
        </div>
    </div>

    @foreach (var section in Model)
    {
        <div class="card bg-base-100 border border-base-300 shadow-sm">
            <div class="card-body">
                <h2 class="card-title text-lg">@section.Key</h2>
                
                @if (section.Value.Rows.Any())
                {
                    <div class="overflow-x-auto">
                        <table class="table table-zebra table-sm w-full">
                            <thead>
                                <tr>
                                    @foreach (var col in section.Value.Rows.First().Keys)
                                    {
                                        <th>@col</th>
                                    }
                                </tr>
                            </thead>
                            <tbody>
                                @foreach (var row in section.Value.Rows)
                                {
                                    <tr>
                                        @foreach (var val in row.Values)
                                        {
                                            <td>@(val?.ToString())</td>
                                        }
                                    </tr>
                                }
                            </tbody>
                        </table>
                    </div>
                }
                else
                {
                    <p class="text-center opacity-70 py-4">データがありません</p>
                }
            </div>
        </div>
    }
</div>
```

---

## ステップ 8: 多言語対応

### 8.1 i18n 設定

`projects/inventory/config/i18n.yml`:

```yaml
translations:
  # エンティティ関連
  entities.product.displayName:
    en-US: Product
    zh-CN: 商品
    ja-JP: 商品
  entities.product.columns.Name.label:
    en-US: Product Name
    zh-CN: 商品名称
    ja-JP: 商品名
  entities.product.columns.Price.label:
    en-US: Price
    zh-CN: 价格
    ja-JP: 価格
  entities.product.columns.Stock.label:
    en-US: Stock
    zh-CN: 库存
    ja-JP: 在庫数
  entities.product.columns.MinStock.label:
    en-US: Min Stock
    zh-CN: 最小库存
    ja-JP: 最小在庫
  entities.product.columns.IsActive.label:
    en-US: Active
    zh-CN: 有效
    ja-JP: 有効
  entities.product.columns.CreatedAt.label:
    en-US: Created At
    zh-CN: 创建时间
    ja-JP: 作成日
  entities.product.columns.UpdatedAt.label:
    en-US: Updated At
    zh-CN: 更新时间
    ja-JP: 更新日
  entities.product.columns.CategoryId.label:
    en-US: Category
    zh-CN: 类别
    ja-JP: カテゴリ
  entities.category.displayName:
    en-US: Category
    zh-CN: 类别
    ja-JP: カテゴリ
  entities.stockmovement.displayName:
    en-US: Stock Movement
    zh-CN: 库存移动
    ja-JP: 在庫移動

  # フォーム関連
  entities.product.forms.Name.label:
    en-US: Product Name
    zh-CN: 商品名称
    ja-JP: 商品名
  entities.product.forms.Price.label:
    en-US: Price
    zh-CN: 价格
    ja-JP: 価格
  entities.product.forms.Stock.label:
    en-US: Stock Quantity
    zh-CN: 库存数量
    ja-JP: 在庫数

  # フィルター関連
  entities.product.filters.Name.label:
    en-US: Product Name
    zh-CN: 商品名称
    ja-JP: 商品名
  entities.product.filters.IsActive.label:
    en-US: Active Status
    zh-CN: 有效状态
    ja-JP: 有効/無効

  # ダッシュボード関連
  projects.inventory.dashboard.stats.0.label:
    en-US: Total Products
    zh-CN: 总商品数
    ja-JP: 総商品数
  projects.inventory.dashboard.stats.1.label:
    en-US: Active Products
    zh-CN: 有效商品
    ja-JP: 有効商品
  projects.inventory.dashboard.stats.4.label:
    en-US: Total Stock Value
    zh-CN: 库存总额
    ja-JP: 在庫総額
  projects.inventory.dashboard.stats.5.label:
    en-US: Low Stock Alert
    zh-CN: 库存不足警报
    ja-JP: 最小在庫割れ

  # ホームページ関連
  projects.inventory.home.hero.eyebrow:
    en-US: Inventory Management System
    zh-CN: 库存管理系统
    ja-JP: 在庫管理システム
  projects.inventory.home.hero.title:
    en-US: Inventory Dashboard
    zh-CN: 库存仪表板
    ja-JP: 在庫管理ダッシュボード
  projects.inventory.home.hero.description:
    en-US: Centralized system for managing products, inventory, and sales.
    zh-CN: 用于管理商品、库存和销售的集中系统。
    ja-JP: 商品・在庫・売上を一元的に管理するシステムです。

  # ページ関連
  projects.inventory.pages.stockStatus.title:
    en-US: Stock Status
    zh-CN: 库存状况
    ja-JP: 在庫状況
  projects.inventory.pages.stockStatus.description:
    en-US: View product stock levels and alerts
    zh-CN: 查看商品库存水平和警报
    ja-JP: 商品別の在庫状況とアラートを一覧表示
```

---

## 完成後の確認事項

### チェックリスト

- [ ] アプリケーション起動
  ```bash
  dotnet run --project NetYamlForge
  ```

- [ ] ブラウザで確認
  - `http://localhost:5000/inventory` - ホームページ
  - `http://localhost:5000/inventory/Dashboard` - ダッシュボード
  - `http://localhost:5000/inventory/DynamicEntity/Index?entity=product` - 商品一覧
  - `http://localhost:5000/inventory/Page/StockStatus` - 在庫状況

- [ ] フックの動作確認
  - 商品登録時に価格検証が働くか
  - 在庫移動時に商品在庫が更新されるか
  - 最小在庫割れ時に警告が出力されるか
  - カテゴリ削除時に使用チェックが働くか

- [ ] 多言語切り替え
  - 言語切り替えボタンで表示が切り替わるか

---

## トラブルシューティング

### Q1: フックが実行されない

**確認点**:
1. フッククラスが `IEntityHook` を実装しているか
2. `Name` プロパティが YAML のフック名と一致しているか
3. DI に登録されているか
4. YAML の `hooks` セクションのキー名が camelCase か

**解決**:
```yaml
# ❌ 誤り
hooks:
  before_create: [validate_product_price]

# ✅ 正しい
hooks:
  beforeCreate: [validate_product_price]
```

---

### Q2: カスタムページが表示されない

**確認点**:
1. `pages/` ディレクトリに YAML ファイルがあるか
2. YAML の構文が正しいか
3. ビューファイル (`views/`) が存在するか
4. URL が正しいか (`/{project}/Page/{pageName}`)

---

### Q3: ダッシュボードのチャートが表示されない

**確認点**:
1. `valueAggregate` と `valueColumn` の組み合わせが正しいか
2. `labelJoinEntity` が存在するか
3. SQL 式に構文エラーがないか

---

### Q4: 在庫移動時にエラー

**確認点**:
1. 商品が存在するか
2. 在庫数は十分か
3. 移動種別が有効か (IN/OUT/ADJUST)

---

## 次のステップ

### 機能拡張のアイデア

1. **ユーザー権限管理**: 管理者/一般ユーザーの権限分離
2. **発注管理**: 自動発注機能の実装
3. **売上分析**: 月次/年次レポート
4. **バーコード連携**: 商品バーコード読み込み
5. **外部システム連携**: ERP/会計システムとの連携

### 参考ドキュメント

- [CLI クイックリファレンス](cli-create-subproject-tutorial-ja.md)
- [フック設計ガイド](../hooks/project-hooks-guide.md)
- [YAML 実例集](../examples/chinook-yaml-examples.md)
- [スキャフォールド運用ガイド](entity-scaffold-workflow-ja.md)

---

## コマンドクイックリファレンス

```bash
# プロジェクト初期化
dotnet run -- --init-project --project=inventory --display-name="在庫管理"

# エンティティ生成
dotnet run -- --scaffold-entities --project=inventory

# フック生成
dotnet run -- --scaffold-hook --name=ValidateProductPrice --project=inventory --with-tests

# アプリ起動
dotnet run --project NetYamlForge

# テスト実行
dotnet test --filter "Inventory"
```
