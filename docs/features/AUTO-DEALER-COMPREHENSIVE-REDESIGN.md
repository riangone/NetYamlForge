# 自動車ディーラー AI 窓口システム — 包括的改善設計書

> 作成日: 2026-03-28
> ブランチ: feature/ai-window-system
> 対象プロジェクト: `auto-dealer-demo`

---

## 0. エグゼクティブサマリー

現行システムは DB スキーマ・YAML 設定・基礎フック実装において約 **80% の完成度** に達しているが、「顧客が実際に触れるチャット UI」「オペレーターが会話をリアルタイム処理する機能」「AI が業務価値に直結するリードを自動生成する仕組み」の三点が欠落している。

本書では以下 **6 レイヤー** を横断した改善策を体系化し、実装優先度と具体的なファイル変更案まで落とし込む。

```
┌──────────────────────────────────────────────────────────┐
│  Layer F  経営ダッシュボード  (ROI / LTV / ファネル)           │
├──────────────────────────────────────────────────────────┤
│  Layer E  セールス管理  (Kanban / 担当者 KPI / 車両マッチング)    │
├──────────────────────────────────────────────────────────┤
│  Layer D  オペレーターコンソール  (リアルタイムチャット / AI Copilot)│
├──────────────────────────────────────────────────────────┤
│  Layer C  AI エンジン  (RAG / リードキャプチャ / 感情予警)        │
├──────────────────────────────────────────────────────────┤
│  Layer B  ナレッジベース  (FAQ / 学習 / 品質モニタリング)          │
├──────────────────────────────────────────────────────────┤
│  Layer A  顧客接点  (チャットウィジェット / プロアクティブ / 多言語)  │
└──────────────────────────────────────────────────────────┘
```

---

## 1. 現状の課題マップ

### 1-1. 顧客体験（顧客→システム）

| 課題 | 影響 | 現在の実装状況 |
|------|------|--------------|
| 顧客向けチャット UI が存在しない | 顧客はシステムと接触できない | ❌ 未実装 |
| 車両画像・比較表などリッチ表示がない | 購買意欲を高める視覚情報が提供できない | ❌ 未実装 |
| チャット内で予約完結できない | チャット→外部フォームへの離脱が発生 | ❌ 未実装 |
| プロアクティブ発話機能がない | 受動的な AI にとどまる | ❌ 未実装 |
| 多言語対応が設定のみ | 実際の多言語応答ロジックなし | ⚠️ 設定のみ |

### 1-2. AI 機能（AI エンジン）

| 課題 | 影響 | 現在の実装状況 |
|------|------|--------------|
| ResponseGenerator が 60% 未完成 | 回答品質が低い・テンプレートに偏る | ⚠️ 部分実装 |
| KnowledgeBaseService が 0% | FAQ 検索・学習ができない | ❌ 未実装 |
| リードキャプチャとの接続なし | 意図認識→商談機会への変換ができない | ❌ 未実装 |
| リアルタイム更新なし | ダッシュボードが最新状態を反映しない | ⚠️ 部分実装 |

### 1-3. オペレーター（内部スタッフ）

| 課題 | 影響 | 現在の実装状況 |
|------|------|--------------|
| OperatorConsole にチャット機能がない | 引き継ぎ後に別ツールでの対応が必要 | ❌ テーブル表示のみ |
| AI 補助（返信候補）機能がない | オペレーターが毎回一から回答を書く | ❌ 未実装 |
| SLA アラートがない | 待機超過が気づかれないリスク | ❌ 未実装 |
| 顧客プロフィールのサイドパネルなし | 対話中に顧客情報を確認するのが困難 | ❌ 未実装 |

### 1-4. セールス管理

| 課題 | 影響 | 現在の実装状況 |
|------|------|--------------|
| 担当者別 KPI ダッシュボードがない | 個人パフォーマンス管理が困難 | ❌ 未実装 |
| リードの Kanban ビューがない | パイプライン全体像が把握しづらい | ❌ 未実装 |
| 停滞リードのアラートがない | フォローアップ漏れが発生 | ❌ 未実装 |
| 車両マッチング機能がない | 顧客希望を在庫と自動照合できない | ❌ 未実装 |

### 1-5. 経営分析

| 課題 | 影響 | 現在の実装状況 |
|------|------|--------------|
| ROI ダッシュボードがない | AI の経済効果を経営層に示せない | ❌ 未実装 |
| 転換率の全体ファネルがない | どのステップで失注しているか不明 | ⚠️ 部分のみ |
| 顧客 LTV 分析がない | 優先顧客の選定根拠が薄い | ❌ 未実装 |
| ナレッジベース効果分析がない | FAQ の品質改善サイクルが回らない | ❌ 未実装 |

---

## 2. 改善設計：Layer A — 顧客接点

### 2-1. Web チャットウィジェット

**目的**: ディーラーのウェブサイトに埋め込み、顧客が 24 時間いつでも AI と対話できる入口を作る。

#### 画面設計

```
┌─────────────────────────────┐
│  自動車ディーラー AI 窓口   ✕  │  ← ヘッダー（閉じるボタン付き）
├─────────────────────────────┤
│ 🤖 AI: こんにちは！どのような  │
│    お車をお探しですか？         │
│                              │
│    [試乗予約]  [車両を探す]    │  ← クイックリプライボタン
│    [お見積り]  [お問い合わせ]  │
├─────────────────────────────┤
│ ┌─────────────────────────┐ │
│ │ 🚗 プリウス PHV Z           │ │  ← 車両カード（インライン表示）
│ │ ¥ 4,280,000               │ │
│ │ [詳細を見る]  [見積を依頼]  │ │
│ └─────────────────────────┘ │
├─────────────────────────────┤
│ [メッセージを入力...]   📎 🎤 📤│  ← 入力エリア（添付/音声/送信）
└─────────────────────────────┘
```

#### 実装要件

| 機能 | 実装方法 |
|------|---------|
| フローティングボタン | JavaScript ウィジェット（CDN 配信可能） |
| クイックリプライ | `ai_knowledge.category = 'faq'` から動的生成 |
| 車両カード | `vehicles` テーブルから画像・価格を取得 |
| 音声入力 | Web Speech API |
| ファイル添付 | 車両の傷写真など（整備依頼で活用） |
| セッション維持 | `localStorage` + `conversation_id` |

#### プロアクティブエンゲージメントルール

```yaml
# 新規設定ファイル: projects/auto-dealer-demo/config/proactive_rules.yml
rules:
  - trigger: page_dwell_seconds >= 60
    page_pattern: "/vehicles/*"
    message: "こちらの車両をご覧いただいているようですね。最新の金利優遇プランをご案内できます。いかがでしょうか？"

  - trigger: cart_abandon
    delay_seconds: 30
    message: "見積りが途中のようです。続きをお手伝いしましょうか？"

  - trigger: return_visitor AND days_since_last_visit >= 30
    message: "お久しぶりです！前回ご覧いただいた {last_viewed_vehicle} に新しいキャンペーン情報があります。"

  - trigger: service_appointment_due_days <= 14
    message: "車検の時期が近づいています。ご予約はお済みでしょうか？"
```

### 2-2. LINE チャネル深化

現在の設定は存在するが接続されていない。以下を実装する。

| 機能 | LINE API | 備考 |
|------|----------|------|
| Flex Message で車両カード | `FlexMessage` + `BubbleContainer` | 画像・価格・CTA |
| Quick Reply ボタン | `QuickReply` | 意図分類を補助 |
| リッチメニュー | `RichMenu` | 常設ショートカット（予約/車両検索/問い合わせ） |
| プッシュ通知 | `pushMessage` | 予約リマインダー・キャンペーン |
| LIFF 予約フォーム | LINE Front-end Framework | チャット内で予約完結 |

### 2-3. キオスクモード（店頭タブレット）

店頭のタブレットにより、来店待ち顧客が自分で情報収集できる。

```
チャンネル: tablet（既存 enum に含まれている）
特殊機能:
  - 大画面向けレイアウト（16:9 最適化）
  - QR コード表示（スマホで会話を引き継ぐ）
  - 顧客情報自己入力フォーム（来店時に紐づけ）
  - 満足度アンケート（退店時）
```

---

## 3. 改善設計：Layer B — ナレッジベース

### 3-1. KnowledgeBaseService 実装（優先度：高）

現在 0% の未実装状態。以下の機能を実装する。

#### 3-1-1. 意味検索（Semantic Search）

```
検索フロー:
顧客メッセージ
    ↓ トークナイズ・正規化
キーワードマッチング（BM25）
    ↓ スコアリング
ai_knowledge テーブル検索
    ↓ 上位 3 件を候補に
ResponseGenerator に渡す
    ↓
最終回答生成
```

`ai_knowledge` テーブルに必要な追加フィールド:
- `embedding_vector` TEXT — ベクトル埋め込み（JSON 配列）
- `last_trained_at` DATETIME — 最終学習時刻

#### 3-1-2. FAQ 自動学習サイクル

```
低置信度会話（confidence < 0.5）
    ↓ 毎日バッチ処理
未解決質問の抽出
    ↓
管理者レビューキュー
    ↓ 承認
ai_knowledge に新規追加
    ↓
次回の検索に反映
```

#### 3-1-3. ナレッジベース管理 UI（新規ページ）

新規ページ: `KnowledgeBase.yaml`

```
セクション:
1. FAQ 統計カード
   - 総 FAQ 数 / アクティブ数 / 本日の命中数 / 平均評価

2. 未解決質問キュー
   - 低置信度会話から抽出した未回答質問
   - 管理者が回答を入力 → ai_knowledge に追加

3. FAQ 一覧（テーブル）
   - 検索・フィルタ付き
   - 命中率・評価を表示
   - インライン編集

4. カテゴリ別命中率（棒グラフ）
   - faq / vehicle_info / campaign / service_menu 別
```

### 3-2. RAG（検索拡張生成）統合

```
文書ソース:
  - 車種カタログ PDF → テキスト抽出 → チャンク化
  - サービスメニュー表 → 構造化データ
  - キャンペーン情報 → 定期更新

パイプライン:
PDF/文書
    ↓ テキスト抽出（Apache PDFBox / iTextSharp）
チャンク分割（500 トークン単位）
    ↓
埋め込みベクトル生成（Claude API / OpenAI Embeddings）
    ↓
ベクトル DB（SQLite-vec または Chroma）
    ↓ 検索クエリ時
コサイン類似度でトップ 3 チャンク取得
    ↓
プロンプトに文脈として注入
    ↓
Claude API → 回答生成
```

---

## 4. 改善設計：Layer C — AI エンジン強化

### 4-1. インテリジェント・リードキャプチャ（優先度：最高）

現在 AI はリードを自動生成しない。会話の中から営業機会を自動検出し `sales_leads` に保存する。

#### 4-1-1. リードキャプチャトリガー

| 意図（intent） | 抽出エンティティ | アクション |
|--------------|----------------|---------|
| `price_inquiry` | 車種名・予算 | lead_score += 30 |
| `vehicle_inquiry` | 車種名・用途 | lead_score += 20 |
| `test_drive_request` | 日付・車種 | lead_score += 40、予約リンク送付 |
| `financing_inquiry` | 頭金・月払い希望 | lead_score += 35 |
| `trade_in_inquiry` | 現在の車種・年式 | lead_score += 25 |
| `visit_intent` | 日時・来店目的 | lead_score += 50、アポイント確認 |

#### 4-1-2. リードスコアリング計算式

```
最終リードスコア =
    意図スコアの合計（最大 100）
    + 顧客ティアボーナス（VIP: +20, Gold: +10, Silver: +5）
    + 感情スコアボーナス（sentiment > 0.5 なら +10）
    - 放棄ペナルティ（直前メッセージが 10 分以上前: -15）
    + 多回訪問ボーナス（同週内 2 回目以降の会話: +15）

スコア 80+ → ホットリード → 即時担当者通知（SignalR）
スコア 50-79 → ウォームリード → 日次まとめレポート
スコア < 50 → コールドリード → 週次フォローリスト
```

#### 4-1-3. フック実装追加（AutoDealerHooks.cs）

```csharp
// 新規追加フック
public class AutoCreateLeadFromConversationHook : IEntityHook
{
    // ai_conversations の AfterAsync で発火
    // last_intent が price_inquiry / test_drive_request 等のとき
    // sales_leads に自動挿入
    // SignalR で営業担当者に通知
}

public class LeadScoreCalculatorHook : IEntityHook
{
    // sales_leads の BeforeAsync で発火
    // 会話コンテキストを参照してスコア自動計算
}
```

### 4-2. 感情プリエンプション（Sentiment Pre-emption）

感情スコアが閾値を超えた時点で、AI が自主的に行動を変える。

```
感情スコア監視フロー:

各メッセージ受信
    ↓ SentimentAnalyzer
sentiment_score 更新
    ↓
    ├─ score < -0.5 → 即時エスカレーション（reason: negative_sentiment）
    │                  エスカレーション理由を添えて引き継ぎ通知
    ├─ score < -0.3 → AI トーン変更（謝罪モード）
    │                  「ご不便をおかけして申し訳ございません」
    └─ score > 0.7 → アップセルトリガー
                      「大変ご満足いただいているようで！
                       特別なキャンペーン情報をご案内できます」
```

### 4-3. 車両レコメンデーションエンジン

会話の中で顧客が述べた条件を抽出し、在庫マッチングを行う。

```
エンティティ抽出 → 在庫マッチング フロー:

会話テキスト
    ↓ IntentClassifier（entity extraction）
{
  "budget": 3000000,
  "vehicle_type": ["suv", "minivan"],
  "passengers": 5,
  "fuel_type": "hybrid",
  "use_case": "family"
}
    ↓ SQL クエリ生成
SELECT * FROM vehicles
  WHERE price <= :budget
  AND vehicle_type IN (:types)
  AND fuel_type = :fuel
  AND status = 'available'
  ORDER BY (price / :budget) DESC  -- 予算消費率でソート
  LIMIT 3
    ↓
車両カードをインライン表示
    ↓ 顧客が選択
リードエンティティに vehicle_interest として記録
```

### 4-4. 予測分析モジュール

```
来訪確率スコア（Visit Probability Score）:
  - 最終来店から 90 日以内: +30
  - 車検期限まで 30 日以内: +40
  - 過去 30 日以内に会話あり: +20
  - Web サイト閲覧 3 回以上: +10
  → スコア 70+ → 「来訪促進キャンペーン」の対象として batch_job が自動選定

チャーン予測:
  - 最終来店から 365 日以上: リスク高
  - フォローアップ未実施 14 日以上: リスク中
  → vip_followup バッチジョブの対象に自動追加
```

---

## 5. 改善設計：Layer D — オペレーターコンソール

### 5-1. リアルタイムチャット UI（最重要改善）

現在の OperatorConsole はテーブル表示のみ。実際のチャット機能を追加する。

#### 5-1-1. 画面レイアウト（3 カラム構成）

```
┌──────────┬──────────────────┬──────────────┐
│          │                  │              │
│ 案件キュー │   チャット画面    │ 顧客情報     │
│          │                  │              │
│ ⚠️ 緊急 2 │ 顧客: ミライの価  │ 田中 太郎 様 │
│ 🔴 高  5  │ 格を教えてください │ VIP         │
│ 🟡 中  8  │                  │ 購入歴: 2 回 │
│ 🟢 低  12 │ AI: ミライの価格  │ 最終来店:    │
│          │ は 3,270,000 円   │ 2025-12-01   │
│ [担当中]  │ からとなります...  │              │
│ 会話 #123 │                  │ 📞 090-xxxx  │
│ 会話 #145 │ [引き継ぎ完了]    │ ✉️ tanaka@.. │
│          │                  │              │
│          ├──────────────────┤ 会話履歴     │
│          │ 💡 AI 提案返信    │ [3 件]       │
│          ├──────────────────┤              │
│          │ ① ミライは HV と  │ リード情報   │
│          │ PHEV の 2 種あり… │ ミライ希望   │
│          │ ② 現在のキャンペ  │ 予算: 350万  │
│          │ ーン情報もご案内… │ スコア: 85   │
│          │ ③ 試乗のご予約は… │              │
│          ├──────────────────┤ [予約を作成] │
│          │ [返信入力...]  📤 │ [リードを更新]│
└──────────┴──────────────────┴──────────────┘
```

#### 5-1-2. OperatorConsole.yaml 改修

```yaml
# 現行の sections に以下を追加/変更

- type: custom_chat_console
  title: "リアルタイム対応コンソール"
  signalr_hub: "/hubs/conversation"
  panels:
    queue:
      query: |
        SELECT h.handover_id, c.name, h.reason, h.priority,
               ROUND((JULIANDAY('now') - JULIANDAY(h.escalated_at)) * 24 * 60) AS wait_minutes
        FROM ai_handovers h
        JOIN customers c ON h.customer_id = c.customer_id  -- 要 conversation 経由の join
        WHERE h.status IN ('pending', 'assigned')
        ORDER BY CASE h.priority WHEN 'urgent' THEN 1 WHEN 'high' THEN 2 WHEN 'medium' THEN 3 ELSE 4 END,
                 h.escalated_at ASC
      sla_alert_minutes: 10  # 10 分超過で赤ハイライト

    chat:
      conversation_id_param: "id"
      show_ai_suggestions: true
      suggestions_source: ai_knowledge
      max_suggestions: 3

    customer_panel:
      show_fields: [name, tier_level, phone, mobile, email, purchase_count, last_visit_date]
      show_lead_info: true
      quick_actions:
        - label: "予約を作成"
          target: "/auto-dealer-demo/ServiceAppointments/Create"
        - label: "リードを更新"
          target: "/auto-dealer-demo/SalesLeads/Edit"
```

### 5-2. SLA モニタリング

```
SLA 設定（config/sla_config.yml）:
  - priority: urgent
    max_wait_minutes: 3
    escalation_path: manager_notification

  - priority: high
    max_wait_minutes: 10
    escalation_path: team_lead_notification

  - priority: medium
    max_wait_minutes: 30
    escalation_path: email_alert

  - priority: low
    max_wait_minutes: 60
    escalation_path: daily_report
```

SLA 超過時のアクション:
- 案件キューで赤ハイライト + タイマー表示
- SignalR でチームリーダーに通知
- `ai_handovers.priority` を自動昇格

### 5-3. チーム稼働ダッシュボード（新規ページ）

新規ページ: `TeamPerformance.yaml`

```
セクション:
1. リアルタイム稼働状況
   - オペレーター名 / 対応中件数 / 平均処理時間 / 本日の解決数

2. シフト別対応能力
   - 時間帯別の想定流入数 vs. 対応可能人数

3. エスカレーション品質
   - 平均解決時間 / 初回解決率 / エスカレーション戻し率

4. ナレッジ活用率
   - AI 提案返信の採用率（オペレーターが一言一句変えずに送った割合）
```

---

## 6. 改善設計：Layer E — セールス管理

### 6-1. リード Kanban ビュー（新規ページ: LeadKanban.yaml）

```
画面イメージ:

[新規 🆕 (12)] → [接触済 📞 (8)] → [商談中 💼 (5)] → [提案中 📋 (3)] → [成約 🏆 (2)] / [失注 ✗]

各カード:
  田中 太郎
  🚗 プリウス PHV
  💰 300-350 万
  🎯 スコア: 85
  📅 最終接触: 2 日前
  [詳細] [フォロー]

停滞アラート:
  7 日以上接触なし → カード右上に ⚠️ アイコン
  14 日以上 → 担当者にメール自動送信（バッチジョブ）
```

### 6-2. 担当者ダッシュボード（SalesRepDashboard.yaml）

```
個人 KPI カード（月次目標対比）:
  - 担当リード数: 15 件 / 目標 20 件（75%）
  - 成約件数: 3 件 / 目標 5 件（60%）
  - 商談率: 60% / 目標 70%
  - 平均成約日数: 18 日

今日のアクション:
  - フォローアップ期限 [3 件] → クリックで詳細
  - 試乗予約 [2 件] → 時刻・顧客名・車種
  - AI 生成の顧客別提案メール [未送信 5 件]

AI からの推奨アクション:
  「田中様（スコア 92）が昨日 ランドクルーザーのページを 3 回閲覧しています。
   今日中に連絡することをお勧めします。」
```

### 6-3. 車両マッチング機能

```
フロー:

sales_leads レコードを開く
    ↓ vehicle_interest, budget フィールドを参照
AI マッチング API を呼び出す
    ↓ vehicles テーブルをクエリ
マッチした車両カード 3 件を表示
    ↓ 担当者が選択
提案書（見積り PDF）の自動生成
    ↓
顧客にメール or LINE で送付
```

### 6-4. AI 生成提案メール

会話コンテキストから、顧客別パーソナライズドメールを自動生成。

```
入力:
  - 顧客名・ティア
  - 興味のある車種
  - 提示した予算
  - 最後の会話内容（要約）
  - 現在のキャンペーン情報

出力（Claude API）:
  件名: 「田中様 / ランドクルーザー 300 特別案内」
  本文:
    先日はお問い合わせいただきありがとうございました。
    ご検討中のランドクルーザー 300 について、
    現在実施中の特別金利（1.9% → 0.9%）キャンペーンをご案内します。
    ...（パーソナライズされた内容）
```

---

## 7. 改善設計：Layer F — 経営分析

### 7-1. ROI ダッシュボード（ExecDashboard.yaml）

経営層向けの投資対効果ダッシュボード。

```
KPI カード:
  - AI 処理コスト削減（月）
    計算式: AI自動解決数 × 平均人件費単価（¥2,500/件）

  - AI 起点の成約金額（月）
    計算式: SUM(vehicles.price) WHERE lead.source_conversation_id IS NOT NULL

  - 平均応答時間（AI vs. 人工）
    AI: 8 秒 / 人工: 4 分 23 秒

  - 顧客獲得コスト（CAC）
    計算式: 月間 AI 運用コスト / 成約件数

チャート:
  - 月次 ROI 推移（折れ線）
  - チャネル別 CAC 比較（棒グラフ）
  - AI vs. 人工の成約率比較（横棒）
  - 時間帯別流入・解決率（ヒートマップ）
```

### 7-2. 全体転換ファネル（ConversionFunnel.yaml）

```
ファネル段階 → 数値例:

来訪者 (Visitors)        : 10,000 人
    ↓ 接触率 15%
チャット開始 (Engaged)   :  1,500 件
    ↓ 意図認識成功率 85%
意図識別済み (Intent)    :  1,275 件
    ↓ リード化率 30%
リード登録 (Lead)        :    383 件
    ↓ 商談化率 40%
商談開始 (Opportunity)   :    153 件
    ↓ 成約率 25%
成約 (Won)               :     38 件

ドロップ分析:
  各段階のドロップ要因を色分け表示
  「接触→意図識別」のドロップ → AI 精度の問題
  「リード→商談」のドロップ → 人的フォローの問題
```

### 7-3. 顧客 LTV 分析（CustomerLTV.yaml）

```
セグメント別分析:
  - Platinum: 平均 LTV ¥12,800,000（車両 3 台+整備）
  - VIP:      平均 LTV ¥8,200,000
  - Gold:     平均 LTV ¥5,400,000
  - Silver:   平均 LTV ¥2,800,000
  - Regular:  平均 LTV ¥1,200,000

予測収益（次 12 ヶ月）:
  - 車検予定顧客: XXX 名 × 平均整備費 → ¥XXX
  - 購入後 3 年の顧客（乗り換えタイミング）: XXX 名

アップセル機会:
  - Silver 顧客で来店頻度が高い → Gold 昇格対象
  - 複数台保有の法人客 → フリート割引提案
```

### 7-4. ナレッジベース効果分析（KBEffectiveness.yaml）

```
KPI:
  - FAQ 総命中率（ai_knowledge.usage_count 累計 / 総質問数）
  - 平均 FAQ 評価スコア（helpful / total）
  - 未解決質問数（confidence < 0.5 かつ knowledge 未登録）

改善サイクル可視化:
  未解決質問 → レビュー → 追加 → 命中率向上 の週次推移

低品質 FAQ ランキング（改善優先度高）:
  - 命中したが not_helpful が多い FAQ 上位 10 件
  → 管理者が直接修正できるリンク付き
```

---

## 8. UI/UX デザイン改善

### 8-1. チャット UI のリッチ化

| コンポーネント | 内容 | 実装優先度 |
|--------------|------|---------|
| 車両カルーセル | 3 台を横スクロールで表示 | 高 |
| 比較モーダル | 選択した 2 台を並列比較 | 中 |
| インライン予約 | チャット内で日時選択 → 予約確定 | 高 |
| 見積りシミュレーター | 頭金・月払い・年数を入力 → 即計算 | 中 |
| 動画埋め込み | 試乗動画・車両紹介 YouTube リンク | 低 |
| マップ表示 | 最寄り店舗・アクセスマップ | 低 |

### 8-2. ダッシュボード UI 統一

現在のダッシュボードは各ページで独自実装されており、一貫性がない。

**統一デザインシステム**:
- カラー: Primary (#1a73e8 ブルー) / Warning (#ff9800) / Danger (#dc3545) / Success (#28a745)
- KPI カード: アイコン左寄せ / 数値大フォント / 前日比（↑↓%）
- チャート: Chart.js 統一 / ダークモード対応
- テーブル: ページネーション / 列ソート / CSV エクスポート
- ナビゲーション: 現在の 4 区分を維持 + バッジ表示（未処理件数）

### 8-3. モバイル最適化

| 対象 | 現状 | 改善案 |
|------|------|--------|
| オペレーターコンソール | PC 前提 | タブレット対応（iPad での移動中確認） |
| 顧客チャット | 未実装 | スマホファーストで実装 |
| 担当者ダッシュボード | PC 前提 | スマホで KPI 確認可能に |
| 予約管理 | PC 前提 | 整備士がスマホで作業状況更新 |

---

## 9. 実装ロードマップ

### Phase 1 — 即効施策（2 週間）

| タスク | 担当レイヤー | 変更ファイル | 難易度 |
|--------|------------|------------|--------|
| OperatorConsole にチャット UI 追加 | Layer D | `OperatorConsole.yaml` | 中 |
| リードキャプチャフック実装 | Layer C | `AutoDealerHooks.cs` | 中 |
| AI リードスコア計算フック | Layer C | `AutoDealerHooks.cs` + `sales_leads.yml` | 中 |
| KnowledgeBase ページ作成 | Layer B | `KnowledgeBase.yaml`（新規） | 低 |
| Dashboard にリアルタイム更新 | Layer F | `AIDashboard.yaml` + SignalR | 高 |
| 停滞リードアラートバッチ | Layer E | `jobs/stale_lead_alert.yml`（新規） | 低 |

### Phase 2 — コア機能強化（1 ヶ月）

| タスク | 担当レイヤー | 変更ファイル | 難易度 |
|--------|------------|------------|--------|
| Web チャットウィジェット実装 | Layer A | JavaScript + CSS（新規） | 高 |
| 車両レコメンデーションエンジン | Layer C | `VehicleMatchService.cs`（新規） | 高 |
| LeadKanban ページ | Layer E | `LeadKanban.yaml`（新規） | 低 |
| SalesRepDashboard ページ | Layer E | `SalesRepDashboard.yaml`（新規） | 低 |
| ROI ダッシュボード | Layer F | `ExecDashboard.yaml`（新規） | 低 |
| LINE Flex Message 対応 | Layer A | `LineWebhookController.cs` | 高 |

### Phase 3 — AI 高度化（2-3 ヶ月）

| タスク | 担当レイヤー | 変更ファイル | 難易度 |
|--------|------------|------------|--------|
| RAG パイプライン構築 | Layer B/C | `RagService.cs`（新規） | 最高 |
| 感情プリエンプション | Layer C | `SentimentPreemptionHook.cs`（新規） | 中 |
| 顧客 LTV 分析ページ | Layer F | `CustomerLTV.yaml`（新規） | 低 |
| AI 提案メール生成 | Layer E | `AiEmailService.cs`（新規） | 高 |
| 来訪予測スコア | Layer C | `VisitPredictionService.cs`（新規） | 最高 |
| 多言語応答（英/中） | Layer A | `i18n.yml` + AI プロンプト | 中 |

---

## 10. 新規エンティティ追加提案

現行スキーマに追加が必要なエンティティ・フィールド。

### 10-1. `conversation_proactive_triggers`（プロアクティブトリガー）

```yaml
columns:
  - trigger_id: PK
  - trigger_type: enum [page_dwell, cart_abandon, return_visit, service_due]
  - condition_json: TEXT  # JSON 形式のトリガー条件
  - message_template: TEXT  # {変数} を含むメッセージテンプレート
  - channel: enum [web, line, email, sms, all]
  - is_active: boolean
  - fired_count: integer
  - conversion_count: integer  # トリガー後に商談化した件数
```

### 10-2. `lead_activities`（リードアクティビティログ）

```yaml
columns:
  - activity_id: PK
  - lead_id: FK → sales_leads
  - activity_type: enum [call, email, visit, proposal_sent, test_drive, ai_message]
  - notes: TEXT
  - outcome: enum [positive, neutral, negative, no_answer]
  - next_action: TEXT
  - next_action_date: datetime
  - created_by: TEXT
  - created_at: datetime
```

### 10-3. `vehicles` フィールド追加

```yaml
追加フィールド:
  - video_url: TEXT  # 試乗動画 URL
  - gallery_images_json: TEXT  # 複数画像 URL の JSON 配列
  - spec_json: TEXT  # スペック詳細（馬力・燃費など）
  - availability_date: date  # 入庫予定日（予約受付用）
  - match_score_cache: TEXT  # 顧客プロファイル別マッチスコアキャッシュ
```

---

## 11. 技術アーキテクチャの改善提案

### 11-1. SignalR ハブ設計

```csharp
// 新規: Hubs/ConversationHub.cs
public class ConversationHub : Hub
{
    // グループ管理
    // - "conversation_{id}": その会話に参加中の全員
    // - "operators": オペレーター全員（キューの更新通知）
    // - "sales_team": 営業担当者全員（ホットリード通知）
    // - "management": 管理者（SLA 超過通知）

    // イベント
    // - NewMessage: メッセージ送受信
    // - StatusChanged: 会話ステータス変化
    // - NewLead: ホットリード生成
    // - SlaAlert: SLA 超過
    // - SentimentAlert: 感情急変
}
```

### 11-2. API エンドポイント追加

```
// 顧客向けチャット API
POST /api/auto-dealer-demo/chat/message
POST /api/auto-dealer-demo/chat/feedback
GET  /api/auto-dealer-demo/chat/{conversation_id}/messages

// 車両検索 API
GET  /api/auto-dealer-demo/vehicles/match?budget=X&type=Y
GET  /api/auto-dealer-demo/vehicles/{id}/details

// リード管理 API
POST /api/auto-dealer-demo/leads/score
GET  /api/auto-dealer-demo/leads/hot  (score >= 80)

// ナレッジベース API
GET  /api/auto-dealer-demo/knowledge/search?q=
POST /api/auto-dealer-demo/knowledge/feedback
```

### 11-3. バッチジョブ追加

```yaml
# jobs/stale_lead_alert.yml
name: stale_lead_alert
schedule: "0 9 * * 1-5"  # 平日 9:00
description: "7 日間フォローなしのリードを担当者に通知"
type: sql_to_notification
query: |
  SELECT l.lead_id, l.customer_id, l.vehicle_interest, l.lead_score,
         l.assigned_to_user_id,
         ROUND(JULIANDAY('now') - JULIANDAY(l.last_contact_at)) AS days_stale
  FROM sales_leads l
  WHERE l.status NOT IN ('won', 'lost')
  AND (l.last_contact_at IS NULL OR JULIANDAY('now') - JULIANDAY(l.last_contact_at) >= 7)
  ORDER BY l.lead_score DESC, days_stale DESC

# jobs/knowledge_gap_analysis.yml
name: knowledge_gap_analysis
schedule: "0 7 * * *"  # 毎日 7:00
description: "前日の低置信度会話から未解決質問を抽出"
type: sql_to_review_queue
```

---

## 12. まとめ：優先度マトリックス

| 改善項目 | ビジネス価値 | 実装工数 | 優先度 |
|---------|------------|---------|--------|
| **リードキャプチャフック** | ⭐⭐⭐⭐⭐ | S | **P0 最優先** |
| **OperatorConsole チャット UI** | ⭐⭐⭐⭐⭐ | M | **P0 最優先** |
| **KnowledgeBase ページ** | ⭐⭐⭐⭐ | S | **P1 高** |
| **LeadKanban ページ** | ⭐⭐⭐⭐ | S | **P1 高** |
| **SLA アラート** | ⭐⭐⭐⭐ | M | **P1 高** |
| **ROI ダッシュボード** | ⭐⭐⭐⭐ | S | **P1 高** |
| **Web チャットウィジェット** | ⭐⭐⭐⭐⭐ | XL | **P2 中** |
| **車両レコメンデーション** | ⭐⭐⭐⭐ | L | **P2 中** |
| **AI 提案メール生成** | ⭐⭐⭐ | L | **P2 中** |
| **RAG パイプライン** | ⭐⭐⭐⭐ | XL | **P3 低** |
| **顧客 LTV 分析** | ⭐⭐⭐ | M | **P3 低** |
| **来訪予測スコア** | ⭐⭐⭐ | XL | **P3 低** |

---

## 付録：改善後のナビゲーション構成案

```
AI 窓口
  ├─ AI ダッシュボード (AIDashboard) ← 既存・強化
  ├─ オペレーターコンソール (OperatorConsole) ← 大幅改修
  ├─ AI 分析レポート (AIAnalytics) ← 既存・維持
  └─ ナレッジベース管理 (KnowledgeBase) ← 新規

セールス管理
  ├─ リード管理 Kanban (LeadKanban) ← 新規
  ├─ セールス担当者 KPI (SalesRepDashboard) ← 新規
  └─ 担当案件一覧 (SalesLeads) ← 既存・維持

顧客管理
  ├─ 顧客一覧 (Customers) ← 既存・強化
  └─ 顧客 LTV 分析 (CustomerLTV) ← 新規（Phase 3）

予約管理
  ├─ サービス予約 (Appointments) ← 既存・維持
  └─ スタッフスケジュール (StaffSchedule) ← 新規（Phase 3）

経営レポート [管理者専用]
  ├─ ROI ダッシュボード (ExecDashboard) ← 新規
  ├─ 転換率ファネル (ConversionFunnel) ← 新規
  └─ チーム稼働状況 (TeamPerformance) ← 新規
```

---

> **次のアクション**: 本書の「Phase 1 — 即効施策」から実装を開始する。
> 特に **リードキャプチャフック** と **OperatorConsole のチャット UI 改修** が最大の ROI をもたらす。
