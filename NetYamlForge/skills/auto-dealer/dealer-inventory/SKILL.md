---
name: dealer-inventory
tier: 1
version: 1.0.0
description: |
  自動車販売ディーラー在庫管理スキル
  車両在庫の照会、分析、推奨アクションを生成
allowed-tools:
  - Bash
  - Read
  - AskUserQuestion
---

## Preamble (run first)

```bash
# 環境チェック
PROJECT="auto-dealer-demo"
cd /home/ubuntu/ws/NetYamlForge

# データベース接続確認
DB_PATH="NetYamlForge/projects/$PROJECT/database/$PROJECT.db"
if [ ! -f "$DB_PATH" ]; then
    echo "⚠️  データベースが見つかりません：$DB_PATH"
    exit 1
fi

echo "🚗 自動車販売 AI 在庫管理スキル"
echo "プロジェクト：$PROJECT"
echo "データベース：$DB_PATH"
```

## Voice

**Tone:** 丁寧な敬語、顧客志向、データに基づく提案
**Writing rules:**
- 日本語（顧客向け）
- 数値は常に根拠を示す
- 推奨アクションは具体的に

## Completion Status Protocol

- **DONE** — 照会完了、推奨アクション提示
- **DONE_WITH_CONCERNS** — 完了だが注意点あり
- **BLOCKED** — データベース接続エラー等
- **NEEDS_CONTEXT** — 顧客の追加情報が必要

## 在庫照会ワークフロー

### Step 1: 基本在庫情報取得

```bash
# 在庫総数取得
sqlite3 "$DB_PATH" "SELECT COUNT(*) as total FROM vehicles WHERE status='available';"

# 価格帯別集計
sqlite3 "$DB_PATH" "
SELECT 
    CASE 
        WHEN price >= 5000000 THEN '500 万円以上'
        WHEN price >= 3000000 THEN '300-500 万円'
        ELSE '300 万円未満'
    END as price_range,
    COUNT(*) as count,
    AVG(price) as avg_price
FROM vehicles 
WHERE status='available'
GROUP BY price_range
ORDER BY price_range;
"

# メーカー別内訳
sqlite3 "$DB_PATH" "
SELECT maker, COUNT(*) as count, AVG(price) as avg_price
FROM vehicles 
WHERE status='available'
GROUP BY maker
ORDER BY count DESC;
"

# 車種タイプ別
sqlite3 "$DB_PATH" "
SELECT vehicle_type, COUNT(*) as count
FROM vehicles 
WHERE status='available'
GROUP BY vehicle_type
ORDER BY count DESC;
"
```

### Step 2: 長期在庫分析

```bash
# 30 日以上在庫の車両
sqlite3 "$DB_PATH" "
SELECT vehicle_id, maker, model, price, arrival_date,
    julianday('now') - julianday(arrival_date) as days_in_stock
FROM vehicles
WHERE status='available' 
    AND arrival_date <= date('now', '-30 days')
ORDER BY days_in_stock DESC;
"

# 在庫回転率計算
sqlite3 "$DB_PATH" "
SELECT 
    COUNT(*) as total_stock,
    AVG(julianday('now') - julianday(arrival_date)) as avg_days
FROM vehicles
WHERE status='available';
"
```

### Step 3: 推奨アクション生成

```markdown
## 在庫分析レポート

### 在庫概要
- 在庫総数：{total} 台
- 在庫総額：{total_value} 万円
- 平均価格：{avg_price} 万円
- 平均在庫日数：{avg_days} 日

### 価格帯別内訳
{price_range_table}

### メーカー別内訳
{maker_table}

### 長期在庫（30 日以上）
{stale_inventory}

## 推奨アクション

### 優先度：高
1. **長期在庫車両の販促強化**
   - 対象：{stale_vehicles} 台
   - 理由：資金拘束コスト {cost} 万円/月
   - アクション：特別価格設定、試乗会開催

2. **高価格帯車両の重点販売**
   - 対象：500 万円以上 {count} 台
   - 理由：在庫金額の {ratio}%を占める
   - アクション：VIP 顧客への優先案内

### 優先度：中
3. **SUV 比較試乗会の開催**
   - 対象：SUV {suv_count} 台
   - 理由：ラインナップが豊富、比較検討しやすい
   - アクション：週末試乗会イベント

4. **低価格帯車両の補充検討**
   - 理由：300 万円未満が {count} 台のみ
   - 推奨：ヤリス、デミオ、ノート等

### 優先度：低
5. **在庫レポートの週次更新**
   - 理由：在庫動向の継続的把握
   - アクション：毎週月曜日に自動生成
```

## 特殊クエリ

### 新車・中古車別分析

```bash
sqlite3 "$DB_PATH" "
SELECT 
    CASE WHEN mileage = 0 THEN '新車' ELSE '中古車' END as condition,
    COUNT(*) as count,
    AVG(price) as avg_price,
    AVG(mileage) as avg_mileage
FROM vehicles
WHERE status='available'
GROUP BY condition;
"
```

### 色別人気分析

```bash
sqlite3 "$DB_PATH" "
SELECT color, COUNT(*) as count, AVG(price) as avg_price
FROM vehicles
WHERE status='available'
GROUP BY color
ORDER BY count DESC
LIMIT 5;
"
```

## 出力形式

### 在庫レポート

```markdown
# 🚗 在庫車両レポート

## 該当件数：**{count} 件**

### 在庫車両一覧
| 車両 ID | メーカー | 車種名 | 価格 (万円) | タイプ | カラー | 在庫日数 |
|--------|---------|--------|-----------|-------|--------|---------|
| {id} | {maker} | {model} | {price} | {type} | {color} | {days} |

- **{maker} {model}** ({price} 万円) — {type} | {color} — [詳細を見る](/auto-dealer-demo/DynamicEntity/DetailPage?entity=vehicles&id={id})

## 統計情報
| 指標 | 値 |
|------|-----|
| **在庫総数** | {total} 台 |
| **在庫総額** | {total_value} 万円 |
| **平均価格** | {avg_price} 万円 |
| **平均在庫日数** | {avg_days} 日 |

## 💡 洞察
{insights}

## 📋 推奨アクション
{actions}
```

## 他スキルとの連携

| スキル | 連携方法 |
|-------|---------|
| `/dealer-sales` | 販売リードと在庫のマッチング |
| `/dealer-customer` | 顧客嗜好に合った車両提案 |
| `/dealer-report` | 週次レポート自動生成 |

## Command Reference

| Command | Description |
|---------|-------------|
| `/dealer-inventory` | 在庫一覧表示 |
| `/dealer-inventory --analysis` | 詳細分析レポート |
| `/dealer-inventory --stale` | 長期在庫のみ表示 |
| `/dealer-inventory --recommend` | 推奨アクション表示 |

## Tips

1. **朝礼で確認**：毎朝の在庫確認に使用
2. **顧客対応前に**：顧客の嗜好に合った車両を事前確認
3. **月末に分析**：在庫回転率の改善に活用
