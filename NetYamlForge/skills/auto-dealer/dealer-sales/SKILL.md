---
name: dealer-sales
tier: 1
version: 1.0.0
description: |
  自動車販売ディーラー営業管理スキル
  販売リードの優先度分類、フォローアップ計画、成約率分析
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

echo "🤝 自動車販売 AI 営業管理スキル"
echo "プロジェクト：$PROJECT"
```

## Voice

**Tone:** 戦略的、データ駆動、行動志向
**Writing rules:**
- 日本語（スタッフ向け）
- 優先度は明確に
- アクションは具体的に

## Completion Status Protocol

- **DONE** — 分析完了、アクションプラン提示
- **DONE_WITH_CONCERNS** — 完了だが注意点あり
- **BLOCKED** — データエラー等
- **NEEDS_CONTEXT** — 追加情報が必要

## 営業リード分析ワークフロー

### Step 1: リード状況取得

```bash
# リード総数とステータス別
sqlite3 "$DB_PATH" "
SELECT status, COUNT(*) as count, 
    AVG(lead_score) as avg_score
FROM sales_leads
GROUP BY status
ORDER BY count DESC;
"

# 優先度の高いリード（スコア 80 以上、3 日以上未連絡）
sqlite3 "$DB_PATH" "
SELECT sl.id, sl.customer_id, sl.status, sl.lead_score,
    sl.last_contact_at, sl.vehicle_interest,
    c.name, c.tier_level, c.phone,
    julianday('now') - julianday(sl.last_contact_at) as days_since_contact
FROM sales_leads sl
JOIN customers c ON sl.customer_id = c.id
WHERE sl.lead_score >= 80 
    AND (julianday('now') - julianday(sl.last_contact_at)) >= 3
ORDER BY sl.lead_score DESC, days_since_contact DESC;
"

# 新規リード（24 時間以内）
sqlite3 "$DB_PATH" "
SELECT sl.id, sl.customer_id, sl.status, sl.lead_score,
    sl.created_at, sl.vehicle_interest,
    c.name, c.tier_level, c.phone
FROM sales_leads sl
JOIN customers c ON sl.customer_id = c.id
WHERE sl.status = 'new'
    AND sl.created_at >= datetime('now', '-1 day')
ORDER BY sl.created_at DESC;
"

# 失注リード分析
sqlite3 "$DB_PATH" "
SELECT sl.id, sl.customer_id, sl.lost_reason, sl.updated_at,
    c.name, sl.vehicle_interest
FROM sales_leads sl
JOIN customers c ON sl.customer_id = c.id
WHERE sl.status = 'lost'
    AND sl.updated_at >= date('now', '-30 days')
ORDER BY sl.updated_at DESC;
"
```

### Step 2: 成約率分析

```bash
# 月別成約率
sqlite3 "$DB_PATH" "
SELECT 
    strftime('%Y-%m', created_at) as month,
    COUNT(*) as total_leads,
    SUM(CASE WHEN status = 'won' THEN 1 ELSE 0 END) as won_count,
    ROUND(100.0 * SUM(CASE WHEN status = 'won' THEN 1 ELSE 0 END) / COUNT(*), 1) as win_rate
FROM sales_leads
WHERE created_at >= date('now', '-6 months')
GROUP BY month
ORDER BY month DESC;
"

# 顧客ランク別成約率
sqlite3 "$DB_PATH" "
SELECT c.tier_level,
    COUNT(*) as total_leads,
    SUM(CASE WHEN sl.status = 'won' THEN 1 ELSE 0 END) as won_count,
    ROUND(100.0 * SUM(CASE WHEN sl.status = 'won' THEN 1 ELSE 0 END) / COUNT(*), 1) as win_rate
FROM sales_leads sl
JOIN customers c ON sl.customer_id = c.id
GROUP BY c.tier_level
ORDER BY win_rate DESC;
"

# 車両タイプ別成約率
sqlite3 "$DB_PATH" "
SELECT sl.vehicle_interest,
    COUNT(*) as total_leads,
    SUM(CASE WHEN sl.status = 'won' THEN 1 ELSE 0 END) as won_count,
    ROUND(100.0 * SUM(CASE WHEN sl.status = 'won' THEN 1 ELSE 0 END) / COUNT(*), 1) as win_rate
FROM sales_leads sl
GROUP BY sl.vehicle_interest
ORDER BY total_leads DESC;
"
```

### Step 3: 優先度分類

```markdown
## 営業リード優先度分類

### 🔴 優先度：高（即時対応）

#### 新規リード（24 時間以内）
> 該当件数：**{count} 件**

| 顧客名 | ランク | リードスコア | 興味車両 | 連絡先 |
|--------|--------|-------------|---------|--------|
| {name} | {tier} | {score} | {vehicle} | {phone} |

**アクション**: 24 時間以内の初回連絡が成約率を 3 倍向上

#### 高スコア未連絡（3 日以上）
> 該当件数：**{count} 件**

| 顧客名 | ランク | リードスコア | 最終連絡 | 未連絡日数 |
|--------|--------|-------------|---------|-----------|
| {name} | {tier} | {score} | {date} | {days}日 |

**アクション**: 流失リスク 70%、優先的にフォローアップ

### 🟡 優先度：中（今日中）

#### 見積もり送付済み（1 週間以内）
> 該当件数：**{count} 件**

**アクション**: 購入意向確認のフォローアップ

#### 試乗予約済み
> 該当件数：**{count} 件**

**アクション**: 試乗前日確認、車両準備

### 🟢 優先度：低（今週中）

#### 情報収集中
> 該当件数：**{count} 件**

**アクション**: 週 1 回の情報提供
```

### Step 4: アクションプラン生成

```markdown
## 本日の営業アクションプラン

### 午前中（9:00-12:00）

#### 1. 新規リードへの初回連絡（3 件）
- **鈴木一郎** 様 — 090-1234-5678
  - 興味：RAV4 試乗
  - 話術：「この度はお問い合わせありがとうございます...」
  
- **佐藤花子** 様 — 080-2345-6789
  - 興味：プリウス PHV 見積もり
  - 話術：「お見積もりの件、詳細をご説明できます...」

#### 2. 高スコアリードのフォローアップ（2 件）
- **田中太郎** 様 — 最終連絡：3/25
  - 興味：ランドクルーザー 300
  - 話術：「前回のお問い合わせの件、その後のご検討状況は...」

### 午後（13:00-18:00）

#### 3. 試乗対応（2 件）
- 14:00 — **山田次郎** 様 — CR-V 試乗
- 16:00 — **鈴木三郎** 様 — アリア試乗

#### 4. 見積もり作成（2 件）
- **小林麻衣** 様 — ヴェゼル見積もり
- **伊藤健** 様 — フォレスター見積もり

### 明日の準備

#### 5. 車両準備確認
- 試乗車両の給油
- 新着車両の清掃

## KPI 進捗

| 指標 | 今月目標 | 現在値 | 達成率 |
|------|---------|-------|--------|
| 成約件数 | 15 件 | 8 件 | 53% |
| 新規リード | 50 件 | 32 件 | 64% |
| 試乗予約 | 30 件 | 18 件 | 60% |
| 成約率 | 30% | 25% | 83% |

**残り日数**: 15 日
**必要ペース**: 1 日 0.5 成約
```

## 他スキルとの連携

| スキル | 連携方法 |
|-------|---------|
| `/dealer-inventory` | 在庫車両とのマッチング |
| `/dealer-customer` | 顧客情報照会 |
| `/dealer-appointment` | 試乗予約管理 |

## Command Reference

| Command | Description |
|---------|-------------|
| `/dealer-sales` | 営業リード一覧 |
| `/dealer-sales --priority` | 優先度別分類 |
| `/dealer-sales --today` | 今日のアクションプラン |
| `/dealer-sales --analysis` | 成約率分析 |
| `/dealer-sales --lost` | 失注分析 |

## Tips

1. **朝礼で確認**：毎朝の優先リード確認
2. **顧客対応前に**：顧客の履歴を事前確認
3. **終業前に計画**：翌日のアクションプラン作成
