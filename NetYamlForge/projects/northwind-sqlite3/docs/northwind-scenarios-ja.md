# Northwind-SQLite3 サブプロジェクト 実装ガイド

## 概要
このサブプロジェクトは `northwind-sqlite3` という名前で追加されています。  
Northwind 風の SQLite データを使い、以下の業務シナリオをすぐ試せます。

- 受注登録・更新（顧客/担当者/配送業者の参照付き）
- 受注明細管理（商品参照、金額計算）
- 在庫監視（再発注レベル割れ）
- 配送遅延監視（RequiredDate 超過）
- 売上KPI（顧客別・担当者別・商品別）

## 追加ファイル
- `projects/northwind-sqlite3/project.yaml`
- `projects/northwind-sqlite3/layout.yml`
- `projects/northwind-sqlite3/dashboard.yml`
- `projects/northwind-sqlite3/database/init.sql`
- `projects/northwind-sqlite3/database/northwind.db`
- `projects/northwind-sqlite3/entities/*.yml`
- `projects/northwind-sqlite3/pages/*.yaml`

## DB初期化
初期化 SQL は `database/init.sql` にあります。

```bash
cd NetYamlForge
sqlite3 projects/northwind-sqlite3/database/northwind.db < projects/northwind-sqlite3/database/init.sql
```

## 業務シナリオ

### 1. 受注登録（picker / multipicker）
URL: `/northwind-sqlite3/DynamicEntity/Index?entity=order`

- `CustomerId`: `foreignKey.picker: true`
  - `displayColumns: [CompanyName, ContactName, Country]`
  - `query` で `Active = 1` の顧客だけを候補表示
- `RelatedProductIds`: `foreignKey.multiPicker: true`
  - `displayColumns: [ProductName, UnitPrice, UnitsInStock]`
  - `query` で `Discontinued = 0` の商品だけ候補表示

### 2. 受注明細管理
URL: `/northwind-sqlite3/DynamicEntity/Index?entity=orderdetail`

- `OrderId` は受注情報（ID/日付/状態）を複合表示
- `ProductId` は picker で選択
- 一覧に `LineTotal = UnitPrice * Quantity * (1 - Discount)` を表示

### 3. 在庫再発注アラート
URL: `/northwind-sqlite3/Page/LowStockAlert`

- `UnitsInStock <= ReorderLevel` の商品を抽出
- 不足数量 `Shortage` を表示
- 商品名・仕入先名で絞り込み可能

### 4. 配送遅延監視
URL: `/northwind-sqlite3/Page/ShippingDelayMonitor`

- `ShippedDate` または現在日が `RequiredDate` を超えた受注を表示
- 遅延日数 `DelayDays` を表示
- 顧客名・ステータスで絞り込み可能

### 5. 営業KPI
URL: `/northwind-sqlite3/Page/SalesKpi`

- 顧客別売上 Top10
- 担当者別売上
- 商品別出荷数量

## ダッシュボード
URL: `/northwind-sqlite3/Dashboard`

- 統計カード: 未出荷受注、遅延受注、売上、低在庫商品数
- グラフ: 月別受注件数、出荷国別件数、商品別数量Top

## 補足
- 全ての YAML は既存フォーマットに合わせています。
- `displayColumn` 互換を維持しつつ、`displayColumns` と `query` を優先利用できます。
