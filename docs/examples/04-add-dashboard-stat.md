# 例04: ダッシュボード統計カードの追加

## 概要

Dashboard に統計カード（COUNT/SUM/AVG）またはチャートを追加する。
**コード変更は不要。`dashboard.yml` のみ変更する。**

---

## 変更ファイル

```
projects/<name>/config/dashboard.yml  ← 変更するのはこれだけ
```

プロジェクト固有の `dashboard.yml` がない場合は新規作成する。
（フレームワーク共通の `NetYamlForge/config/dashboard.yml` は編集しない）

---

## 統計カードの追加

### ケース1: COUNT カード（件数表示）

```yaml
# projects/<name>/config/dashboard.yml

stats:
  - id: total_customers
    title: "顧客数"
    titleI18n:
      en-US: "Total Customers"
      ja-JP: "顧客数"
    type: count
    table: customer
    icon: "👥"
    color: "primary"   # primary / success / warning / danger / info

  - id: active_orders
    title: "受注中"
    type: count
    table: orders
    where: "status = 'Open'"   # WHERE句（安全な固定値のみ）
    icon: "📦"
    color: "warning"
```

### ケース2: SUM カード（合計値表示）

```yaml
  - id: total_revenue
    title: "総売上"
    type: sum
    table: orders
    column: total_amount       # 集計対象列
    where: "status != 'Cancelled'"
    format: "currency"         # currency / number / decimal
    icon: "💰"
    color: "success"
```

### ケース3: AVG カード（平均値表示）

```yaml
  - id: avg_order_value
    title: "平均受注額"
    type: avg
    table: orders
    column: total_amount
    where: "status = 'Completed'"
    format: "currency"
    icon: "📊"
    color: "info"
```

---

## チャートの追加

```yaml
charts:
  - id: orders_by_status
    title: "ステータス別受注"
    type: pie      # pie / bar / line / doughnut
    table: orders
    groupBy: status        # GROUP BY 対象列
    aggregate: count       # count / sum
    # aggregate: sum の場合は column も指定
    # column: total_amount
    limit: 10              # 表示する最大グループ数

  - id: monthly_sales
    title: "月次売上推移"
    type: line
    table: orders
    groupBy: "strftime('%Y-%m', created_at)"  # 日付グルーピング式
    aggregate: sum
    column: total_amount
    limit: 12
    where: "status = 'Completed'"
```

---

## `where` 句の安全な書き方

`where` 句には**固定値のみ**を使用すること。動的なユーザー入力は絶対に埋め込まない。

```yaml
# ✅ 安全（固定値）
where: "status = 'Active'"
where: "is_deleted = 0"
where: "created_at >= '2026-01-01'"

# ❌ 危険（動的値・変数埋め込みは使用禁止）
where: "user_id = {currentUser}"   # ← 使用禁止
```

---

## Dashboard 全体構成例

```yaml
# projects/myproject/config/dashboard.yml

stats:
  - id: total_customers
    title: "顧客数"
    type: count
    table: customer
    icon: "👥"
    color: "primary"

  - id: active_products
    title: "有効商品数"
    type: count
    table: product
    where: "is_active = 1 AND is_deleted = 0"
    icon: "📦"
    color: "success"

  - id: total_revenue
    title: "総売上"
    type: sum
    table: orders
    column: total_amount
    where: "status = 'Completed'"
    format: "currency"
    icon: "💰"
    color: "info"

charts:
  - id: sales_by_month
    title: "月次売上"
    type: bar
    table: orders
    groupBy: "strftime('%Y-%m', created_at)"
    aggregate: sum
    column: total_amount
    where: "status = 'Completed'"
    limit: 6
```

---

## 検証チェックリスト

- [ ] `dotnet build` が通る
- [ ] Dashboard ページに新しいカードが表示される
- [ ] 数値が正しく集計されている（DBで手動確認）
- [ ] `where` 句に動的な値を埋め込んでいないか確認
- [ ] SQL Server でも動作させる場合: 日付関数が方言依存でないか確認

---

## よくある間違い

| 間違い | 正しい方法 |
|--------|-----------|
| `where` に `strftime` を使ったが SQL Server でエラー | SQL Server では `FORMAT(col, 'yyyy-MM')` を使う |
| `type: sum` で `column` を指定しなかった | `type: sum/avg` は `column` が必須 |
| `format: currency` で通貨記号が表示されない | フロントエンドのフォーマット設定を確認（Views/Dashboard/Index.cshtml） |
