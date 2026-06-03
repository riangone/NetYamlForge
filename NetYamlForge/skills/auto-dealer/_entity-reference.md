---
title: 汽车销售 AI - 实体字段参考
description: 各エンティティの完全なフィールド定義と分析ガイド
---

# 📊 エンティティ完全リファレンス

## エンティティ一覧

| エンティティ | 説明 | 主な用途 |
|-------------|------|---------|
| [`vehicles`](#vehicles-車両在庫) | 車両在庫情報 | 在庫照会、車両案内 |
| [`sales_leads`](#sales_leads-営業リード) | 営業リード情報 | リード管理、フォローアップ |
| [`service_appointments`](#service_appointments-予約) | サービス予約 | 予約確認、日程調整 |
| [`customers`](#customers-顧客) | 顧客マスタ | 顧客情報、購入履歴 |

---

## `vehicles` (車両在庫)

### フィールド定義

| フィールド | 型 | 必須 | デフォルト | 説明 |
|-----------|----|----|----------|------|
| `id` | int | ✅ | auto | 車両 ID（主キー） |
| `brand` | string | ✅ | - | メーカー名 |
| `model` | string | ✅ | - | モデル名 |
| `grade` | string | ❌ | - | グレード |
| `year` | int | ✅ | - | 年式 |
| `fuel_type` | string | ✅ | - | 燃料タイプ |
| `price` | decimal | ✅ | - | 価格（税込） |
| `color` | string | ❌ | - | 色 |
| `mileage` | int | ❌ | 0 | 走行距離 (km) |
| `status` | string | ✅ | `available` | 販売状態 |
| `description` | text | ❌ | - | 車両説明 |
| `features` | text | ❌ | - | 装備情報（JSON） |
| `image_url` | string | ❌ | - | 画像 URL |
| `created_at` | datetime | ✅ | now | 登録日 |
| `updated_at` | datetime | ✅ | now | 更新日 |

### 列挙値

#### `brand` (メーカー)
```
トヨタ、ホンダ、日産、マツダ、スバル、BMW、メルセデス、アウディ
```

#### `fuel_type` (燃料タイプ)
```
ガソリン、ハイブリッド、電気、ディーゼル、プラグインハイブリッド
```

#### `status` (販売状態)
```
available  - 販売中
reserved   - 商談中
sold       - 売約済
```

### 分析に使用するフィールド

| 分析タイプ | 使用するフィールド |
|-----------|------------------|
| 在庫状況 | `status`, `created_at`, `mileage` |
| 価格帯分析 | `price`, `brand`, `fuel_type` |
| 人気車種 | `brand`, `model`, `views_count` (別テーブル) |
| 長期在庫 | `created_at`, `status` |

### クエリ例

#### 販売中の SUV を取得
```json
{
  "entity": "vehicles",
  "filters": [
    { "field": "status", "op": "eq", "value": "available" },
    { "field": "model", "op": "in", "value": ["RAV4", "CX-5", "CR-V"] }
  ],
  "orderBy": { "field": "price", "dir": "asc" }
}
```

#### 在庫 90 日以上の車両を取得
```json
{
  "entity": "vehicles",
  "filters": [
    { "field": "status", "op": "eq", "value": "available" },
    { "field": "created_at", "op": "lte", "value": "90_days_ago" }
  ],
  "select": ["id", "brand", "model", "price", "created_at"]
}
```

---

## `sales_leads` (営業リード)

### フィールド定義

| フィールド | 型 | 必須 | デフォルト | 説明 |
|-----------|----|----|----------|------|
| `id` | int | ✅ | auto | リード ID（主キー） |
| `customer_id` | int | ✅ | - | 顧客 ID（外部キー） |
| `status` | string | ✅ | `new` | ステータス |
| `vehicle_interest` | string | ❌ | - | 興味車両 |
| `budget` | decimal | ❌ | - | 予算 |
| `lead_score` | int | ❌ | 50 | リードスコア (0-100) |
| `source` | string | ❌ | `web` | 獲得元 |
| `last_contact_at` | datetime | ❌ | - | 最終連絡日 |
| `next_followup_at` | datetime | ❌ | - | 次回フォローアップ日 |
| `notes` | text | ❌ | - | 備考 |
| `created_at` | datetime | ✅ | now | 作成日 |
| `updated_at` | datetime | ✅ | now | 更新日 |

### 列挙値

#### `status` (ステータス)
```
new         - 新規
contacted   - 連絡済み
qualified   - 資格済み（購入意向あり）
proposal    - 提案中
won         - 成約
lost        - 失注
```

#### `source` (獲得元)
```
web         - Web サイト
phone       - 電話
walk_in     - 来店
referral    - 紹介
event       - イベント
social      - SNS
```

### 優先度分類ガイド

分析レポート作成時は、以下の基準で優先度を分類してください：

| 優先度 | 条件 |
|--------|------|
| 🔴 **高** | ・`status = 'new'` で 24 時間以内<br>・`lead_score >= 80` で 3 日以上未連絡<br>・VIP 顧客の問い合わせ |
| 🟡 **中** | ・1-2 日未連絡<br>・`lead_score 60-79`<br>・シルバー/ゴールド顧客 |
| 🟢 **低** | ・24 時間以内に連絡済み<br>・`lead_score < 60`<br>・一般顧客 |

### 分析に使用するフィールド

| 分析タイプ | 使用するフィールド |
|-----------|------------------|
| 優先度分類 | `status`, `lead_score`, `last_contact_at`, `created_at` |
| 成約率 | `status`, `created_at` |
| ソース別効果 | `source`, `status` |
| 経過日数 | `last_contact_at`, `created_at` |

### クエリ例

#### 今日連絡すべきリードを取得
```json
{
  "entity": "sales_leads",
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
  "orderBy": { "field": "last_contact_at", "dir": "asc" }
}
```

#### 今月の成約数を取得
```json
{
  "entity": "sales_leads",
  "action": "count",
  "filters": [
    { "field": "status", "op": "eq", "value": "won" },
    { "field": "updated_at", "op": "gte", "value": "this_month" }
  ]
}
```

---

## `service_appointments` (予約)

### フィールド定義

| フィールド | 型 | 必須 | デフォルト | 説明 |
|-----------|----|----|----------|------|
| `id` | int | ✅ | auto | 予約 ID（主キー） |
| `customer_id` | int | ✅ | - | 顧客 ID（外部キー） |
| `appointment_type` | string | ✅ | - | 予約種別 |
| `preferred_date` | datetime | ✅ | - | 希望日時 |
| `status` | string | ✅ | `pending` | 状態 |
| `vehicle_id` | int | ❌ | - | 車両 ID（試乗時） |
| `staff_id` | int | ❌ | - | 担当者 ID |
| `notes` | text | ❌ | - | 備考 |
| `created_at` | datetime | ✅ | now | 作成日 |
| `updated_at` | datetime | ✅ | now | 更新日 |

### 列挙値

#### `appointment_type` (予約種別)
```
test_drive   - 試乗
service      - 整備・点検
consultation - 相談・見積もり
```

#### `status` (状態)
```
pending     - 未確認
confirmed   - 確定
completed   - 完了
cancelled   - キャンセル
no_show     - 無断キャンセル
```

### 分析に使用するフィールド

| 分析タイプ | 使用するフィールド |
|-----------|------------------|
| 予約状況 | `status`, `preferred_date`, `appointment_type` |
| 成約率 | `status`, `created_at` |
| 種別分析 | `appointment_type`, `status` |
| 担当者別 | `staff_id`, `status` |

### クエリ例

#### 今週の予約を取得
```json
{
  "entity": "service_appointments",
  "filters": [
    { "field": "preferred_date", "op": "gte", "value": "this_week" },
    { "field": "status", "op": "in", "value": ["pending", "confirmed"] }
  ],
  "orderBy": { "field": "preferred_date", "dir": "asc" }
}
```

#### 試乗予約の数を取得
```json
{
  "entity": "service_appointments",
  "action": "count",
  "filters": [
    { "field": "appointment_type", "op": "eq", "value": "test_drive" },
    { "field": "preferred_date", "op": "gte", "value": "this_week" }
  ]
}
```

---

## `customers` (顧客)

### フィールド定義

| フィールド | 型 | 必須 | デフォルト | 説明 |
|-----------|----|----|----------|------|
| `id` | int | ✅ | auto | 顧客 ID（主キー） |
| `name` | string | ✅ | - | 名前 |
| `phone` | string | ✅ | - | 電話番号 |
| `email` | string | ❌ | - | メールアドレス |
| `tier_level` | string | ✅ | `regular` | ランク |
| `last_visit_date` | datetime | ❌ | - | 最終来店日 |
| `purchase_count` | int | ❌ | 0 | 購入回数 |
| `total_purchase_amount` | decimal | ❌ | 0 | 累計購入金額 |
| `preferred_brand` | string | ❌ | - | 希望メーカー |
| `notes` | text | ❌ | - | 備考 |
| `created_at` | datetime | ✅ | now | 作成日 |
| `updated_at` | datetime | ✅ | now | 更新日 |

### 列挙値

#### `tier_level` (顧客ランク)
```
regular    - 一般
silver     - シルバー
gold       - ゴールド
vip        - VIP
platinum   - プラチナ
```

### 優先度分類ガイド

| 優先度 | 条件 |
|--------|------|
| 🔴 **高** | ・`tier_level = 'vip'` または `'platinum'` で 30 日以上未連絡<br>・`tier_level = 'gold'` で 60 日以上未連絡 |
| 🟡 **中** | ・`tier_level = 'silver'` で 90 日以上未連絡<br>・`tier_level = 'regular'` で 180 日以上未連絡 |
| 🟢 **低** | ・30 日以内に連絡済み<br>・アクティブな購入意向あり |

### 分析に使用するフィールド

| 分析タイプ | 使用するフィールド |
|-----------|------------------|
| ランク別分析 | `tier_level`, `purchase_count`, `total_purchase_amount` |
| フォローアップ | `tier_level`, `last_visit_date` |
| 購入傾向 | `preferred_brand`, `purchase_count` |
| 経過日数 | `last_visit_date` |

### クエリ例

#### VIP 顧客の一覧を取得
```json
{
  "entity": "customers",
  "filters": [
    { "field": "tier_level", "op": "eq", "value": "vip" }
  ],
  "select": [
    "name",
    "tier_level",
    "last_visit_date",
    "phone",
    "purchase_count"
  ],
  "orderBy": { "field": "last_visit_date", "dir": "asc" }
}
```

#### 顧客数をランク別に取得
```json
{
  "entity": "customers",
  "action": "count",
  "filters": [
    { "field": "tier_level", "op": "eq", "value": "vip" }
  ]
}
```

---

## 分析レポート作成ガイド

### 必須フィールドの取得

分析レポート作成時は、以下のフィールドを**必ず取得**してください：

| エンティティ | 必須フィールド | 用途 |
|-------------|---------------|------|
| **sales_leads** | `customer_id`, `status`, `vehicle_interest`, `last_contact_at`, `created_at`, `lead_score` | 優先度分類、経過日数計算 |
| **customers** | `name`, `tier_level`, `last_visit_date`, `phone`, `purchase_count` | ランク別分類、フォローアップ判断 |
| **service_appointments** | `appointment_type`, `preferred_date`, `status`, `notes` | 予約状況の分類 |
| **vehicles** | `brand`, `model`, `year`, `price`, `status`, `mileage`, `color` | 在庫状況の分析 |

### 分析レポートの形式

詳細は [_response-templates.md](./_response-templates.md) を参照してください。

---

*最終更新：2026 年 4 月 1 日*
