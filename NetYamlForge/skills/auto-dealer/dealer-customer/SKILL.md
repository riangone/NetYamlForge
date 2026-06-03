---
name: dealer-customer
tier: 1
version: 1.0.0
description: |
  自動車販売ディーラー顧客管理スキル
  顧客情報の照会、購入履歴、フォローアップ計画
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

DB_PATH="NetYamlForge/projects/$PROJECT/database/$PROJECT.db"

echo "👥 自動車販売 AI 顧客管理スキル"
echo "プロジェクト：$PROJECT"
```

## Voice

**Tone:** 丁寧、顧客中心、パーソナライズ
**Writing rules:**
- 日本語（顧客名を必ず使用）
- 購入履歴を参照
- 次のアクションを提案

## Completion Status Protocol

- **DONE** — 顧客情報照会完了、次のアクション提示
- **DONE_WITH_CONCERNS** — 完了だが注意点あり
- **BLOCKED** — 顧客が見つからない等
- **NEEDS_CONTEXT** — 顧客名の特定が必要

## 顧客管理ワークフロー

### Step 1: 顧客情報取得

```bash
# 顧客基本情報
sqlite3 "$DB_PATH" "
SELECT id, name, tier_level, phone, email,
    last_visit_date, purchase_count,
    CASE 
        WHEN last_visit_date >= date('now', '-30 days') THEN 'アクティブ'
        WHEN last_visit_date >= date('now', '-90 days') THEN '要注意'
        ELSE '流失リスク'
    END as status
FROM customers
ORDER BY tier_level DESC, last_visit_date DESC;
"

# VIP 顧客（30 日以上未連絡）
sqlite3 "$DB_PATH" "
SELECT id, name, tier_level, phone, email,
    last_visit_date, purchase_count,
    julianday('now') - julianday(last_visit_date) as days_since_visit
FROM customers
WHERE tier_level IN ('vip', 'platinum')
    AND last_visit_date <= date('now', '-30 days')
ORDER BY days_since_visit DESC;
"

# 顧客の購入履歴
sqlite3 "$DB_PATH" "
SELECT c.name, v.maker, v.model, v.vehicle_type, v.price,
    sl.status as sale_status, sl.created_at
FROM customers c
JOIN sales_leads sl ON c.id = sl.customer_id
LEFT JOIN vehicles v ON sl.vehicle_id = v.id
WHERE c.id = :customer_id
ORDER BY sl.created_at DESC;
"

# 顧客のサービス予約履歴
sqlite3 "$DB_PATH" "
SELECT c.name, sa.appointment_type, sa.preferred_date, 
    sa.status, sa.notes
FROM customers c
JOIN service_appointments sa ON c.id = sa.customer_id
WHERE c.id = :customer_id
ORDER BY sa.preferred_date DESC
LIMIT 5;
"
```

### Step 2: 顧客セグメント分析

```bash
# ランク別顧客数
sqlite3 "$DB_PATH" "
SELECT tier_level, 
    COUNT(*) as count,
    AVG(purchase_count) as avg_purchases,
    SUM(CASE WHEN last_visit_date >= date('now', '-30 days') THEN 1 ELSE 0 END) as active_count
FROM customers
GROUP BY tier_level
ORDER BY count DESC;
"

# 誕生月顧客（今月）
sqlite3 "$DB_PATH" "
SELECT id, name, tier_level, phone, birth_month
FROM customers
WHERE birth_month = strftime('%m', 'now')
ORDER BY tier_level DESC;
"

# 車検時期の顧客（3 ヶ月以内）
sqlite3 "$DB_PATH" "
SELECT c.id, c.name, c.phone, v.model, v.inspection_date,
    julianday(v.inspection_date) - julianday('now') as days_until
FROM customers c
JOIN vehicles v ON c.id = v.owner_id
WHERE v.inspection_date BETWEEN date('now') AND date('now', '+90 days')
ORDER BY v.inspection_date;
"
```

### Step 3: 顧客情報出力

```markdown
# 👤 顧客情報レポート

## 基本情報

| 項目 | 値 |
|------|-----|
| **お名前** | {name} 様 |
| **ランク** | {tier_level} |
| **お電話番号** | {phone} |
| **メールアドレス** | {email} |
| **最終来店** | {last_visit} ({days}日前) |
| **ご購入回数** | {count} 回 |
| **ステータス** | {status} |

## ご購入履歴

| 購入日 | 車両 | 車種 | 価格 | ステータス |
|-------|------|-----|------|-----------|
| {date} | {maker} | {model} | {price} | {status} |

## サービス予約履歴

| 予約日 | 種別 | 状態 | 備考 |
|-------|------|-----|------|
| {date} | {type} | {status} | {notes} |

## 次の推奨アクション

### 優先度：高
1. **フォローアップお電話**
   - 理由：{days} 日ご連絡しておりません
   - 話術：「その後、お車の調子はいかがでしょうか...」

### 優先度：中
2. **車検ご案内**
   - 時期：{inspection_date}
   - 内容：「車検の時期が近づいてまいりましたが...」

3. **試乗ご案内**
   - 対象：{new_vehicle}
   - 理由：「前回ご購入いただいた{model} の新型が入荷いたしました」

### 優先度：低
4. **ニュースレター送付**
   - 内容：月間キャンペーン情報
   - 頻度：月 1 回
```

## 他スキルとの連携

| スキル | 連携方法 |
|-------|---------|
| `/dealer-sales` | 営業リードと連携 |
| `/dealer-inventory` | 嗜好に合った車両提案 |
| `/dealer-appointment` | 予約管理 |

## Command Reference

| Command | Description |
|---------|-------------|
| `/dealer-customer` | 顧客一覧 |
| `/dealer-customer {name}` | 特定顧客情報 |
| `/dealer-customer --vip` | VIP 顧客のみ |
| `/dealer-customer --inactive` | 非アクティブ顧客 |
| `/dealer-customer --birthday` | 誕生月顧客 |

## Tips

1. **顧客対応前に確認**：履歴を把握して適切な対応
2. **定期連絡**：ランク別に応じ頻度を設定
3. **特別な日**：誕生日、購入記念日に連絡
