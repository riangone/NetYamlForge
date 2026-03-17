# northwind-sqlite3-ops 実装ガイド

## 1. 概要
`northwind-sqlite3-ops` は `northwind-sqlite3` の SQLite DB を再利用する派生サブプロジェクトです。  
対象URLは `/northwind-sqlite3-ops/...` です。

- 受注オペレーション管理
- 出荷優先度キュー
- 在庫補充計画
- 顧客リスク監視
- CRUD hook による業務ルール検証

## 2. 追加した主な構成
- `project.yaml`: サブプロジェクト定義、ナビゲーション、DB参照先
- `dashboard.yml`: KPI/チャートの業務監視指標
- `pages/*.yaml`: 3つの業務ページ
- `entities/order.yml`: 受注検証・ステータス遷移hook
- `entities/orderdetail.yml`: 在庫検証hook
- `Hooks/NorthwindOpsHooks.cs`: プロジェクト固有hook実装

## 3. hook 使用シナリオ
### 3.1 受注日付検証 (`nw_order_date_guard`)
対象: `order.beforeCreate`, `order.beforeUpdate`

- `RequiredDate < OrderDate` を拒否
- `Status` 未指定時に `Open` を自動設定
- `Freight` を 0-5000 で検証

### 3.2 ステータス遷移制御 (`nw_order_status_transition`)
対象: `order.beforeUpdate`, `order.afterUpdate`

- `Cancelled` から他ステータスへの戻しを禁止
- 更新後に `AuditLog` へ hook 操作記録を追加

### 3.3 在庫引当検証 (`nw_orderdetail_stock_guard`)
対象: `orderdetail.beforeCreate`, `orderdetail.beforeUpdate`

- `Quantity <= 0` を拒否
- `Products.UnitsInStock` を超える明細登録/更新を拒否

## 4. 業務ページ
### 4.1 FulfillmentQueue
`/northwind-sqlite3-ops/Page/FulfillmentQueue`

- 遅延日数・売上・優先度(P1/P2/P3)で受注を並べ替え

### 4.2 ReplenishmentPlan
`/northwind-sqlite3-ops/Page/ReplenishmentPlan`

- 未出荷受注量を含めた推奨補充数量を算出

### 4.3 CustomerRiskRadar
`/northwind-sqlite3-ops/Page/CustomerRiskRadar`

- 顧客ごとの遅延率と売上を集約し、フォロー優先度を可視化

## 5. 多言語対応
以下の主要箇所で `ja-JP / en-US / zh-CN` を追加済み:

- ナビゲーション項目 (`project.yaml`)
- KPI/チャートタイトル (`dashboard.yml`)
- 受注・受注明細・商品の主要ラベル (`entities/*.yml`)

## 6. 動作確認手順
```bash
cd NetYamlForge
dotnet build
dotnet run
```

確認URL:
- `/northwind-sqlite3-ops/Dashboard`
- `/northwind-sqlite3-ops/DynamicEntity/Index?entity=order`
- `/northwind-sqlite3-ops/DynamicEntity/Index?entity=orderdetail`
- `/northwind-sqlite3-ops/Page/FulfillmentQueue`
- `/northwind-sqlite3-ops/Page/ReplenishmentPlan`
- `/northwind-sqlite3-ops/Page/CustomerRiskRadar`
