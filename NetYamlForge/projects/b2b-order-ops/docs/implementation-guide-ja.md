# b2b-order-ops 実装ガイド

## 1. 概要
`b2b-order-ops` は `northwind-sqlite3` の SQLite DB を再利用する派生サブプロジェクトです。  
対象URLは `/b2b-order-ops/...` です。

- 受注オペレーション管理
- 受発注ワークベンチ
- 出荷優先度キュー
- 在庫補充計画
- 顧客リスク監視
- CRUD機能の実運用検証（picker / foreignKey / フィルタ / ページング）

## 2. 追加した主な構成
- `project.yaml`: サブプロジェクト定義、ナビゲーション、DB参照先
- `dashboard.yml`: KPI/チャートの業務監視指標
- `pages/*.yaml`: 4つの業務ページ（Workbench含む）
- `entities/order.yml`: 受注一覧・検索・参照リンク定義
- `entities/orderdetail.yml`: 明細一覧・検索・参照リンク定義
- `entities/product.yml`: 在庫管理向け基本定義

## 3. 業務ページ
### 3.1 OrderWorkbench
`/b2b-order-ops/Page/OrderWorkbench`

- 受注ヘッダと受注明細を同一画面で参照
- YAML駆動UI（`ui` 拡張キー）によるコンポーネント定義の実例

### 3.2 FulfillmentQueue
`/b2b-order-ops/Page/FulfillmentQueue`

- 遅延日数・売上・優先度(P1/P2/P3)で受注を並べ替え

### 3.3 ReplenishmentPlan
`/b2b-order-ops/Page/ReplenishmentPlan`

- 未出荷受注量を含めた推奨補充数量を算出

### 3.4 CustomerRiskRadar
`/b2b-order-ops/Page/CustomerRiskRadar`

- 顧客ごとの遅延率と売上を集約し、フォロー優先度を可視化

## 4. 多言語対応
以下の主要箇所で `ja-JP / en-US / zh-CN` を追加済み:

- ナビゲーション項目 (`project.yaml`)
- KPI/チャートタイトル (`dashboard.yml`)
- 受注・受注明細・商品の主要ラベル (`entities/*.yml`)

## 5. 動作確認手順
```bash
cd NetYamlForge
dotnet build
dotnet run
```

確認URL:
- `/b2b-order-ops/Dashboard`
- `/b2b-order-ops/DynamicEntity/Index?entity=order`
- `/b2b-order-ops/DynamicEntity/Index?entity=orderdetail`
- `/b2b-order-ops/Page/OrderWorkbench`
- `/b2b-order-ops/Page/FulfillmentQueue`
- `/b2b-order-ops/Page/ReplenishmentPlan`
- `/b2b-order-ops/Page/CustomerRiskRadar`
