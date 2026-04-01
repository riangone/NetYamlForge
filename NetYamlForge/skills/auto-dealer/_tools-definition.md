---
title: 汽车销售 AI - 工具定义
description: query_data 和其他工具的完整定义
---

# 🔧 利用可能なツール

## `query_data` - データ検索

**重要**: ユーザーがデータ（顧客・車両・予約・リードなど）について尋ねた場合は、**必ず `query_data` ツールを呼び出してください**。

### クエリモード

| モード | 説明 | 用途 |
|-------|------|------|
| `structured` | 構造化クエリパラメータ（デフォルト） | 通常の CRUD 操作 |
| `template` | 事前定義済みクエリテンプレート使用 | 複雑な集計・分析クエリ |
| `raw_sql` | 生 SQL 実行（要特別権限） | 高度な分析・カスタムクエリ |

### ツール呼び出し形式（構造化クエリ - デフォルト）

```json
{
  "mode": "structured",
  "entity": "vehicles|sales_leads|service_appointments|customers",
  "action": "list|count|aggregate",
  "filters": [
    { "field": "status", "op": "eq", "value": "available" },
    { "field": "created_at", "op": "gte", "value": "this_week" }
  ],
  "orderBy": { "field": "created_at", "dir": "desc" },
  "groupBy": ["brand", "vehicle_type"],
  "aggregations": [
    { "function": "count", "field": "id", "alias": "total_count" },
    { "function": "avg", "field": "price", "alias": "avg_price" }
  ],
  "top": 20,
  "select": ["field1", "field2"]
}
```

### パラメータ説明

| パラメータ | 型 | 必須 | 説明 |
|-----------|----|----|------|
| `mode` | string | ❌ | クエリモード (`structured`/`template`/`raw_sql`)。デフォルトは `structured` |
| `entity` | string | ✅ | 対象エンティティ名 |
| `action` | string | ❌ | `list` (一覧) / `count` (件数) / `aggregate` (集計)。デフォルトは `list` |
| `filters` | array | ❌ | 絞り込み条件 |
| `orderBy` | object | ❌ | ソート指定 |
| `groupBy` | array | ❌ | グループ化フィールド（`action: aggregate` の場合） |
| `aggregations` | array | ❌ | 集計関数（`action: aggregate` の場合） |
| `top` | int | ❌ | 取得件数上限 |
| `select` | array | ❌ | 取得フィールド指定（省略時は全フィールド） |

### `action` パラメータの使い分け

| 用途 | action | 例 |
|------|--------|-----|
| 一覧表示 | `list` (デフォルト) | 「車両を見せて」「リード一覧」 |
| 件数取得 | `count` | 「顧客数は？」「何件ある？」 |

### フィルター演算子

| 演算子 | 説明 | 例 |
|-------|------|-----|
| `eq` | 一致 | `{ "field": "status", "op": "eq", "value": "available" }` |
| `ne` | 不一致 | `{ "field": "status", "op": "ne", "value": "sold" }` |
| `gt` | より大きい | `{ "field": "price", "op": "gt", "value": 2000000 }` |
| `gte` | 以上 | `{ "field": "lead_score", "op": "gte", "value": 80 }` |
| `lt` | より小さい | `{ "field": "price", "op": "lt", "value": 3000000 }` |
| `lte` | 以下 | `{ "field": "mileage", "op": "lte", "value": 50000 }` |
| `in` | 含まれる | `{ "field": "status", "op": "in", "value": ["new", "active"] }` |
| `contains` | 部分一致 | `{ "field": "notes", "op": "contains", "value": "緊急" }` |
| `startswith` | 前方一致 | `{ "field": "name", "op": "startswith", "value": "山田" }` |

### 日付相対指定

`filters` の `value` に以下の文字列を使用すると自動変換されます：

| 文字列 | 意味 |
|-------|------|
| `today` | 今日 |
| `yesterday` | 昨日 |
| `this_week` | 今週 |
| `last_week` | 先週 |
| `this_month` | 今月 |
| `last_month` | 先月 |
| `this_year` | 今年 |
| `last_year` | 昨年 |

### 使用例

#### 例 1: 販売中の車両を一覧

```json
{
  "entity": "vehicles",
  "action": "list",
  "filters": [
    { "field": "status", "op": "eq", "value": "available" }
  ],
  "orderBy": { "field": "created_at", "dir": "desc" },
  "top": 10
}
```

#### 例 2: 今週の新規リードを取得

```json
{
  "entity": "sales_leads",
  "action": "list",
  "filters": [
    { "field": "status", "op": "eq", "value": "new" },
    { "field": "created_at", "op": "gte", "value": "this_week" }
  ],
  "select": ["customer_id", "vehicle_interest", "lead_score", "created_at"]
}
```

#### 例 3: VIP 顧客の数を取得

```json
{
  "entity": "customers",
  "action": "count",
  "filters": [
    { "field": "tier_level", "op": "eq", "value": "vip" }
  ]
}
```

#### 例 4: 優先度の高いリードを取得（分析用）

```json
{
  "entity": "sales_leads",
  "action": "list",
  "filters": [
    { "field": "status", "op": "in", "value": ["new", "active"] }
  ],
  "select": [
    "customer_id",
    "status",
    "vehicle_interest",
    "last_contact_at",
    "created_at",
    "lead_score"
  ],
  "orderBy": { "field": "last_contact_at", "dir": "asc" },
  "top": 50
}
```

#### 例 5: 聚合查询 - 按品牌和车型统计库存

```json
{
  "entity": "vehicles",
  "action": "aggregate",
  "groupBy": ["brand", "model"],
  "aggregations": [
    { "function": "count", "field": "vehicle_id", "alias": "vehicle_count" },
    { "function": "avg", "field": "price", "alias": "avg_price" },
    { "function": "sum", "field": "price", "alias": "total_value" }
  ],
  "filters": [
    { "field": "status", "op": "eq", "value": "available" }
  ],
  "orderBy": { "field": "vehicle_count", "dir": "desc" },
  "top": 20
}
```

#### 例 6: 模板查询 - 使用预定义模板

```json
{
  "mode": "template",
  "template": "inventory_analysis",
  "templateParams": {
    "status": "available"
  }
}
```

**可用模板列表:**
- `inventory_analysis` - 库存分析（按品牌/车型统计）
- `sales_lead_analysis` - 销售线索分析（按状态/优先级）
- `vehicle_inventory_summary` - 车辆库存汇总（按车型分类）
- `customer_tier_analysis` - 顾客等级分析

#### 例 7: 原始 SQL 查询（需要特殊权限）

```json
{
  "mode": "raw_sql",
  "raw_sql": "SELECT brand, COUNT(*) as count, AVG(price) as avg_price FROM vehicles WHERE status = @status GROUP BY brand",
  "sql_params": {
    "status": "available"
  }
}
```

⚠️ **注意**: `raw_sql` 模式需要特殊权限，默认情况下仅使用 `structured` 或 `template` 模式。

---

## クエリテンプレート

クエリテンプレートは `projects/<project-name>/queries/` ディレクトリの YAML ファイルとして定義されます。

### テンプレート定義例

```yaml
name: inventory_analysis
description: 在庫分析レポート - ブランドとモデル別統計
entity: vehicles
action: aggregate
groupBy:
  - brand
  - model
aggregations:
  - function: count
    field: vehicle_id
    alias: vehicle_count
  - function: avg
    field: price
    alias: avg_price
filters:
  - field: status
    op: eq
    value: "{status}"
orderBy:
  field: vehicle_count
  dir: desc
parameters:
  - name: status
    type: string
    required: false
    default: available
    description: 車両ステータス
```

### テンプレートの使用

AI は以下の形式でテンプレートを呼び出します：

```json
{
  "mode": "template",
  "template": "inventory_analysis",
  "templateParams": {
    "status": "available"
  }
}
```

---

## `create_appointment_request` - 予約作成（顧客向け）

顧客からの予約リクエストを作成します。

### ツール呼び出し形式

```json
{
  "customer_name": "山田太郎",
  "phone": "090-1234-5678",
  "email": "taro@example.com",
  "appointment_type": "test_drive|service|consultation",
  "preferred_date": "2026-04-05",
  "preferred_time": "14:00",
  "vehicle_id": "12345",
  "notes": "初めて電気自動車を購入検討中"
}
```

### パラメータ説明

| パラメータ | 型 | 必須 | 説明 |
|-----------|----|----|------|
| `customer_name` | string | ✅ | 顧客名 |
| `phone` | string | ✅ | 電話番号 |
| `email` | string | ❌ | メールアドレス |
| `appointment_type` | string | ✅ | `test_drive` (試乗) / `service` (整備) / `consultation` (相談) |
| `preferred_date` | string | ✅ | 希望日 (YYYY-MM-DD) |
| `preferred_time` | string | ❌ | 希望時間 (HH:MM) |
| `vehicle_id` | string | ❌ | 車両 ID（試乗予約時） |
| `notes` | string | ❌ | 備考 |

### 使用例

```json
{
  "customer_name": "山田太郎",
  "phone": "090-1234-5678",
  "appointment_type": "test_drive",
  "preferred_date": "2026-04-05",
  "preferred_time": "14:00",
  "vehicle_id": "7",
  "notes": "RAV4 の試乗を希望。初めて電気自動車を購入検討中。"
}
```

---

## 利用可能なエンティティとフィールド

### `vehicles` (車両在庫)

| フィールド | 型 | 説明 |
|-----------|----|------|
| `id` | int | 車両 ID |
| `brand` | string | メーカー（トヨタ、ホンダ等） |
| `model` | string | モデル名 |
| `grade` | string | グレード |
| `year` | int | 年式 |
| `fuel_type` | string | 燃料タイプ |
| `price` | decimal | 価格（税込） |
| `color` | string | 色 |
| `mileage` | int | 走行距離 (km) |
| `status` | string | 状態 |
| `created_at` | datetime | 登録日 |

**`status` の値:**
- `available` - 販売中
- `reserved` - 商談中
- `sold` - 売約済

**`fuel_type` の値:**
- `ガソリン`
- `ハイブリッド`
- `電気`
- `ディーゼル`

---

### `sales_leads` (営業リード)

| フィールド | 型 | 説明 |
|-----------|----|------|
| `id` | int | リード ID |
| `customer_id` | int | 顧客 ID |
| `status` | string | ステータス |
| `vehicle_interest` | string | 興味車両 |
| `budget` | decimal | 予算 |
| `lead_score` | int | リードスコア (0-100) |
| `last_contact_at` | datetime | 最終連絡日 |
| `created_at` | datetime | 作成日 |

**`status` の値:**
- `new` - 新規
- `contacted` - 連絡済み
- `qualified` - 資格済み
- `proposal` - 提案中
- `won` - 成約
- `lost` - 失注

---

### `service_appointments` (予約)

| フィールド | 型 | 説明 |
|-----------|----|------|
| `id` | int | 予約 ID |
| `customer_id` | int | 顧客 ID |
| `appointment_type` | string | 予約種別 |
| `preferred_date` | datetime | 希望日 |
| `status` | string | 状態 |
| `notes` | string | 備考 |

**`appointment_type` の値:**
- `test_drive` - 試乗
- `service` - 整備
- `consultation` - 相談

**`status` の値:**
- `pending` - 未確認
- `confirmed` - 確定
- `completed` - 完了
- `cancelled` - キャンセル

---

### `customers` (顧客)

| フィールド | 型 | 説明 |
|-----------|----|------|
| `id` | int | 顧客 ID |
| `name` | string | 名前 |
| `phone` | string | 電話番号 |
| `email` | string | メール |
| `tier_level` | string | ランク |
| `last_visit_date` | datetime | 最終来店日 |
| `purchase_count` | int | 購入回数 |

**`tier_level` の値:**
- `regular` - 一般
- `silver` - シルバー
- `gold` - ゴールド
- `vip` - VIP
- `platinum` - プラチナ

---

## 詳細ページ URL パターン

各エンティティの詳細ページへのリンクは以下の形式で作成してください：

| エンティティ | URL パターン |
|-------------|-------------|
| `vehicles` | `/auto-dealer-demo/DynamicEntity/DetailPage?entity=vehicles&id={id}` |
| `sales_leads` | `/auto-dealer-demo/DynamicEntity/DetailPage?entity=sales_leads&id={id}` |
| `service_appointments` | `/auto-dealer-demo/DynamicEntity/DetailPage?entity=service_appointments&id={id}` |
| `customers` | `/auto-dealer-demo/DynamicEntity/DetailPage?entity=customers&id={id}` |

**Markdown リンク形式:**
```markdown
[詳細を見る](/auto-dealer-demo/DynamicEntity/DetailPage?entity=vehicles&id=7)
```

---

*最終更新：2026 年 4 月 1 日*
