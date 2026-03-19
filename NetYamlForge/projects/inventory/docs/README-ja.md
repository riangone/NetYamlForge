# 在庫管理システム (Inventory Management System)

このディレクトリは CLI 自動生成スクリプトで作成された在庫管理システムのプロジェクトです。

## ディレクトリ構造

```
inventory/
├── project.yaml              # プロジェクト定義
├── dashboard.yml             # ダッシュボード設定
├── config/
│   ├── home-page.yml         # ホームページ設定
│   ├── layout.yml            # レイアウト定義
│   └── i18n.yml              # 多言語設定
├── database/
│   └── inventory.db          # SQLite DB
├── entities/
│   ├── product.yml           # 商品エンティティ
│   ├── category.yml          # カテゴリエンティティ
│   └── stockmovement.yml     # 在庫移動エンティティ
├── entities.generated/       # 自動生成エンティティ
├── pages/
│   └── StockStatus.yaml      # 在庫状況ページ
├── views/
│   ├── _Layout.cshtml        # プロジェクトレイアウト
│   ├── _ViewImports.cshtml   # View 共通設定
│   ├── _ViewStart.cshtml     # View 開始設定
│   └── StockStatus.cshtml    # 在庫状況ビュー
└── Hooks/
    ├── ValidateProductPriceHook.cs      # 価格検証フック
    ├── CheckStockThresholdHook.cs       # 在庫閾値チェック
    ├── UpdateProductStockHook.cs        # 在庫更新フック
    └── CheckCategoryUsageHook.cs        # カテゴリ使用チェック
```

## 機能

### エンティティ

- **商品 (Products)**: 商品マスタ。カテゴリ、価格、在庫数、最小在庫を管理
- **カテゴリ (Categories)**: 商品カテゴリ
- **在庫移動 (StockMovements)**: 入荷・出荷・調整の在庫移動記録

### フック

| フック名 | 対象 | 説明 |
|---------|------|------|
| `validate_product_price` | 商品 | 価格が 0〜1,000,000 の範囲か検証 |
| `check_stock_threshold` | 商品 | 最小在庫を下回った場合に警告ログ出力 |
| `update_product_stock` | 在庫移動 | 移動記録に基づき商品在庫を自動更新 |
| `check_category_usage` | カテゴリ | 関連商品がある場合削除を防止 |

### ダッシュボード

- 総商品数、有効商品数、カテゴリ数
- 在庫総数、在庫金額
- 最小在庫割れアラート
- 本月庫移動数（入荷/出荷）
- 商品カテゴリ別在庫数（ドーナツチャート）
- 月別在庫移動推移（ラインチャート）
- 商品別在庫金額 TOP10（バーチャート）

### カスタムページ

- **在庫状況 (StockStatus)**: 
  - 最小在庫割れアラート一覧
  - 商品別在庫一覧（ステータス表示）
  - カテゴリ別集計

## 使用方法

### 1. フックの DI 登録

`NetYamlForge/Program.cs` に以下を追加：

```csharp
// Inventory プロジェクト用フック登録
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.ValidateProductPriceHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.CheckStockThresholdHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.UpdateProductStockHook>();
builder.Services.AddSingleton<IEntityHook, NetYamlForge.Projects.Inventory.Hooks.CheckCategoryUsageHook>();
```

### 2. アプリケーションの起動

```bash
dotnet run --project NetYamlForge
```

### 3. ブラウザで確認

- ホームページ：`http://localhost:5000/inventory`
- ダッシュボード：`http://localhost:5000/inventory/Dashboard`
- 商品一覧：`http://localhost:5000/inventory/DynamicEntity/Index?entity=product`
- 在庫状況：`http://localhost:5000/inventory/Page/StockStatus`

## データベースの初期化

SQLite データベースを初期化する場合は、以下のスクリプトを実行：

```bash
sqlite3 database/inventory.db < ../../scripts/windows/inventory-demo/create_inventory_db.sql
```

## 関連ドキュメント

- [CLI 子项目开发完全ガイド](../../../docs/guides/cli-subproject-complete-guide-ja.md)
- [Windows 一键生成脚本](../../../scripts/windows/inventory-demo/README-ja.md)
