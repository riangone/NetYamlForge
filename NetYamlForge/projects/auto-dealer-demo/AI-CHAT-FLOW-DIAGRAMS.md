# 自動車販売 AI チャット - ビジュアルフロー図

> **版本**: 1.0  
> **最後更新**: 2026 年 4 月 9 日

---

## 📊 目次

1. [メッセージ処理の全体シーケンス図](#メッセージ処理の全体シーケンス図)
2. [会話状態遷移図](#会話状態遷移図)
3. [Slot-filling フロー図](#slot-fillingフロー図)
4. [エスカレーション検出フロー](#エスカレーション検出フロー)
5. [AI プロバイダー フォールバック図](#aiプロバイダーフォールバック図)
6. [データベース関連図](#データベース関連図)

---

## メッセージ処理の全体シーケンス図

```
┌─────────────────┐
│  顧客・ウェブUI  │
└────────┬────────┘
         │
         │ POST /api/ai/chat/session/{id}/message
         │ {"message": "試乗したい"}
         ▼
┌──────────────────────────────────────────┐
│  AutoDealerChatController                │
│  .SendMessage()                          │
└────────┬─────────────────────────────────┘
         │
         │ [入力検証]
         │ - message が空でないか確認
         │ - タイムアウト設定（3600秒）
         ▼
┌──────────────────────────────────────────┐
│  AutoDealerChatService                   │
│  .SendMessageAsync()                     │
└────────┬─────────────────────────────────┘
         │
         │ ┌─────────────────────┐
         │ │ ステップ 1-2:       │
         │ │ エスカレーション    │
         │ │ 感情分析            │
         └─┬──┬─────────────────┘
           │  │
           │  ├─ needsHandover? → HandleEscalationAsync へ
           │  │
           │  └─ sentiment < -0.5? → HandleEscalationAsync へ
           │
           ▼
┌──────────────────────────────────────────┐
│ ┌─────────────────────┐                  │
│ │ ステップ 3:         │                  │
│ │ メッセージ受信の    │                  │
│ │ ai_messages に      │                  │
│ │ 保存                │                  │
│ └─────────────────────┘                  │
│ ┌─────────────────────┐                  │
│ │ ステップ 4:         │                  │
│ │ Slot-filling       │                  │
│ │ セッション確認      │                  │
│ │ （オプション）      │                  │
│ └─────────────────────┘                  │
│ ┌─────────────────────┐                  │
│ │ ステップ 5:         │                  │
│ │ インテント分類      │                  │
│ │ （オプション）      │                  │
│ └─────────────────────┘                  │
│  AutoDealerChatService                   │
└────────┬─────────────────────────────────┘
         │
         │ [Slot-filling が active && complete？]
         │
         ├─ YES → CompleteScenarioAsync
         │        ├─ service_appointments INSERT
         │        ├─ メール送信
         │        └─ sales_leads INSERT
         │
         └─ NO → GenerateAiResponseAsync
                  ├─ BuildSystemPrompt
                  ├─ BuildPromptWithHistory
                  ├─ ExecuteWithSystemPromptOverrideAsync
                  └─ ProcessAiResponseAsync
                     ├─ インテント抽出
                     ├─ データ行抽出
                     └─ ナビゲーション URL 抽出
           │
           ▼
┌──────────────────────────────────────────┐
│ ステップ 6-8:                            │
│ - AI 応答を ai_messages に保存           │
│ - ai_conversations メタデータ更新        │
│ - chat_history に保存                    │
│ - クイックリプライ生成                   │
│ - HTTP レスポンス構築                    │
└────────┬─────────────────────────────────┘
         │
         │ {"responseText": "...", "intent": "..."}
         ▼
┌──────────────────────┐
│  AutoDealerChat      │
│  Controller          │
│  (レスポンス返却)    │
└────────┬─────────────┘
         │
         │ HTTP 200 OK
         │ Content-Type: application/json
         ▼
┌──────────────────────┐
│  顧客・ウェブUI      │
│  (レスポンス表示)    │
└──────────────────────┘
```

---

## 会話状態遷移図

```
                    [セッション開始]
                           │
                           ▼
                    ┌─────────────┐
                    │   active    │◄──────┐
                    │ (会話中)    │       │
                    └──┬──┬───┬──┘       │
                       │  │   │         │
          ┌────────────┘  │   └────────┐│
          │               │            ││
          │ [エスカレ      │ [完了]     ││
          │  ーション]     │            ││
          │               ▼            ││
          │        ┌──────────────┐   ││
          │        │ completed    │   ││
          │        │ (終了)       │   ││
          │        └──────────────┘   ││
          │                           ││
          ▼                           ▼│
      ┌────────────┐            ┌─────────────┐
      │ escalated  │            │ abandoned   │
      │(エスカレ   │            │ (放置)      │
      │ーション中) │            └─────────────┘
      └─────┬──────┘
            │
            │ [解決]
            │
            ▼
       ┌─────────────┐
       │ resolved    │
       │ (解決)      │
       └─────────────┘

状態の説明:
  - active: セッション開始から終了まで
  - completed: ユーザーが会話を終了
  - escalated: AI→オペレーター エスカレーション
  - resolved: エスカレーション解決
  - abandoned: タイムアウト・放置
```

---

## Slot-filling フロー図

```
┌──────────────────────────────────────┐
│ メッセージ受信                        │
│ "試乗したいです"                      │
└────────────┬─────────────────────────┘
             │
             ▼
┌──────────────────────────────────────┐
│ インテント分類                        │
│ → "test_drive_booking"              │
└────────────┬─────────────────────────┘
             │
             ▼
┌──────────────────────────────────────┐
│ MapIntentToScenario                  │
│ → "test_drive"                       │
└────────────┬─────────────────────────┘
             │
             ▼
┌──────────────────────────────────────┐
│ Slot-filling セッション開始           │
│ scenario = "test_drive"              │
│ required_slots = [                  │
│   vehicle_model,                     │
│   preferred_date,                    │
│   preferred_time,                    │
│   customer_name,                     │
│   customer_phone                     │
│ ]                                    │
└────────────┬─────────────────────────┘
             │
             │ ◄─────────────────┐
             │                   │
             ▼                   │
┌──────────────────────────────────┐  │
│ メッセージ処理                   │  │
│ "トヨタ カムリ"                   │  │
└────────────┬────────────────────┘  │
             │                       │
             ▼                       │
┌──────────────────────────────────┐  │
│ ExtractSlotValuesFromMessageAsync│  │
│ → vehicle_model = "トヨタ カムリ" │  │
└────────────┬────────────────────┘  │
             │                       │
             ▼                       │
┌──────────────────────────────────┐  │
│ GetNextRequiredSlotAsync          │  │
│ → nextSlot = preferred_date       │  │
└────────────┬────────────────────┘  │
             │                       │
             ▼                       │
┌──────────────────────────────────┐  │
│ BuildSlotStatusMessage            │  │
│ "✅ 車種: トヨタ カムリ           │  │
│  🎯 次: 試乗希望日を教えてください" │  │
└────────────┬────────────────────┘  │
             │                       │
             ▼                       │
┌──────────────────────────────────┐  │
│ GenerateAiResponseAsync           │  │
│ (slot status message を AI に注入) │  │
│ → "ご利用ありがとうございます。   │  │
│    試乗希望日をお知らせください。" │  │
└────────────┬────────────────────┘  │
             │                       │
             ▼                       │
┌──────────────────────────────────┐  │
│ session.IsComplete?               │  │
│ (すべてのスロット埋まった？)       │  │
└────┬──────────────────────────┬───┘  │
     │ NO                       │      │
     │ [続行]              [YES] │      │
     │                          ▼      │
     │          ┌──────────────────────┐│
     │          │CompleteScenarioAsync ││
     │          │                      ││
     │          │- スロット値を確認    ││
     │          │- DB に INSERT        ││
     │          │- メール送信         ││
     │          │- リード生成         ││
     │          │- セッション クリア  ││
     │          │                      ││
     │          │ ResponseText:       ││
     │          │ "試乗予約承知しました││
     │          │  4/15 14:00"        ││
     │          └──────────┬───────────┘│
     │                     │            │
     │ ┌──────────────────┘            │
     │ │                               │
     └─┴───────────────────────────────┘
             │
             ▼
        ┌─────────────┐
        │ レスポンス  │
        │ 返却        │
        └─────────────┘
```

---

## エスカレーション検出フロー

```
┌─────────────────────────────────┐
│  メッセージ受信                   │
│  "詐欺だ、警察に通報する！"       │
└──────────┬──────────────────────┘
           │
           ▼
┌─────────────────────────────────┐
│  DetectEscalation()              │
│                                  │
│  キーワード分析:                │
│  criticalKeywords = [            │
│    "苦情", "返金", "詐欺",      │
│    "警察", "弁護士"              │
│  ]                               │
│                                  │
│  message.contains("詐欺")?       │
│  → YES (critical found)          │
└──────────┬──────────────────────┘
           │
           ▼
┌─────────────────────────────────┐
│  escalationIntent                │
│  = "escalation_critical"         │
│                                  │
│  needsHandover = TRUE            │
│  priority = "critical"           │
└──────────┬──────────────────────┘
           │
           ▼
┌─────────────────────────────────┐
│  HandleEscalationAsync()         │
│                                  │
│  1. ai_handovers INSERT:        │
│     handover_id = "HAND-xxxx"   │
│     reason = "escalation_critical"
│     priority = "critical"        │
│     status = "pending"           │
│                                  │
│  2. ai_conversations UPDATE:    │
│     status = "escalated"         │
│     assigned_to_user_id = NULL   │
│                                  │
│  3. Slot-filling RESET          │
│     (予約途中の場合はクリア)     │
│                                  │
│  4. ResponseText 作成:          │
│     "申し訳ございません。         │
│      詳しいスタッフが対応します。"│
└──────────┬──────────────────────┘
           │
           ▼
┌─────────────────────────────────┐
│  {                               │
│    "suggestHandover": true,     │
│    "responseText": "申し訳...",  │
│    "processingTimeMs": 1250      │
│  }                               │
└─────────────────────────────────┘
           │
           ▼
┌─────────────────────────────────┐
│  オペレーター通知:               │
│  - 新しいエスカレーション      │
│  - 優先度: critical              │
│  - メッセージ: "詐欺だ..."       │
└─────────────────────────────────┘
```

---

## AI プロバイダー フォールバック図

```
┌────────────────────────────────────────┐
│ GenerateAiResponseAsync()              │
│ providerOverride = "claude"? null?     │
└─────────────┬────────────────────────┘
              │
              ▼
        ┌──────────────┐
        │ providerOver │
        │ ride が指定? │
        └──┬───────┬──┘
           │ YES   │ NO
           │       └──────┐
           │              │
           ▼              ▼
      ┌─────────┐   ┌──────────────┐
      │ Claude  │   │ CLI ファースト│
      │ 使用    │   │ チェーン      │
      │ (単体) │   └──┬───────────┘
      └──┬─────┘      │
         │            ▼
         │        ┌──────────┐
         │        │ qwen     │
         │        │ (Alibaba)│
         │        └──┬────┬─┘
         │           │    │
         │           │    └─ 失敗
         │           │       │
         │           │       ▼
         │           │     ┌────────┐
         │           │     │ gemini │
         │           │     │(Google)│
         │           │     └──┬──┬─┘
         │           │        │  │
         │           │        │  └─ 失敗
         │           │        │     │
         │           │        │     ▼
         │           │        │   ┌───────┐
         │           │        │   │ ollama│
         │           │        │   │(Local)│
         │           │        │   └──┬──┬─┘
         │           │        │      │  │
         │           │        │      │  └─ 失敗
         │           │        │      │     │
         │ 成功時    │ 成功時 │  成功時 │ 成功時
         │ ↓        │ ↓      │   ↓    │ ↓
         └────┬─────┴─┴──────┴─────┴──┬──┘
              │                       │
              ▼                       ▼
        ┌──────────────────────┐  ┌────────────┐
        │ AI 応答を取得       │  │ エラー    │
        │ responseText="..."  │  │ "すべてのプ│
        │                      │  │ロバイダー │
        │ ProcessAiResponseAsync │  │が失敗"   │
        │ へ進む                │  │           │
        └─────┬────────────────┘  └────────────┘
              │
              ▼
        ┌─────────────────┐
        │レスポンス構築   │
        │返却             │
        └─────────────────┘
```

---

## データベース関連図

### テーブル間の関連性

```
┌──────────────────────────┐
│ customers                │
│ ├─ customer_id (PK)      │
│ ├─ name                  │
│ ├─ phone                 │
│ └─ email                 │
└────────────┬─────────────┘
             │
             │ 1:N
             │
             ▼
┌──────────────────────────┐
│ ai_conversations         │
│ ├─ conversation_id (PK)  │
│ ├─ customer_id (FK)      │
│ ├─ channel               │
│ ├─ status                │
│ ├─ last_intent           │
│ ├─ sentiment_score       │
│ ├─ context_data (JSON)   │◄─ Slot状態を保持
│ ├─ assigned_to_user_id   │
│ └─ timestamps            │
└────────────┬─────────────┘
             │
             │ 1:N
             │
             ▼
┌──────────────────────────┐
│ ai_messages              │
│ ├─ message_id (PK)       │
│ ├─ conversation_id (FK)  │
│ ├─ sender                │◄─ customer, ai, agent
│ ├─ content               │
│ ├─ intent                │
│ ├─ confidence_score      │
│ ├─ sentiment_score       │
│ └─ timestamp             │
└──────────────────────────┘

┌──────────────────────────┐
│ ai_handovers             │
│ ├─ handover_id (PK)      │
│ ├─ conversation_id (FK)  │
│ ├─ reason                │
│ ├─ priority              │
│ ├─ status                │
│ ├─ assigned_to_user_id   │
│ └─ resolution_notes      │
└──────────────────────────┘

┌──────────────────────────┐
│ sales_leads              │
│ ├─ lead_id (PK)          │
│ ├─ customer_id (FK)      │◄─ 自動リード生成
│ ├─ conversation_id (FK)  │
│ ├─ intent                │
│ ├─ priority              │
│ ├─ status                │
│ └─ created_at            │
└──────────────────────────┘

┌──────────────────────────┐
│ service_appointments     │
│ ├─ appointment_id (PK)   │
│ ├─ customer_id (FK)      │◄─ Slot-filling完了時に自動INSERT
│ ├─ vehicle_id (FK)       │
│ ├─ appointment_date      │
│ ├─ appointment_time      │
│ └─ status                │
└──────────────────────────┘
```

### ai_conversations.context_data（JSON 構造）

```json
{
  "slot_sessions": {
    "test_drive": {
      "scenario": "test_drive",
      "created_at": "2026-04-09T14:00:00Z",
      "collected_slots": {
        "vehicle_model": "トヨタ カムリ",
        "preferred_date": "2026-04-15",
        "preferred_time": "14:00",
        "customer_name": "田中太郎",
        "customer_phone": "090-1234-5678"
      },
      "required_slots": [
        "vehicle_model",
        "preferred_date",
        "preferred_time",
        "customer_name",
        "customer_phone"
      ],
      "is_complete": true,
      "completion_time": "2026-04-09T14:05:00Z"
    }
  },
  "summary": {
    "closed_at": "2026-04-09T14:10:00Z",
    "message_count": 12,
    "last_intent": "test_drive_booking"
  }
}
```

---

## 処理フローの時間軸図

```
タイムスタンプ      イベント                          DB状態
──────────────────────────────────────────────────────────

14:00:00.000       メッセージ受信
                   "試乗したい"
                        │
14:00:00.050       エスカレーション判定
                   感情分析
                        │
14:00:00.100       ai_messages INSERT       ai_messages
                   (sender=customer)           ↑ +1

14:00:00.150       Slot-filling 開始
                   "test_drive" シナリオ

14:00:00.500       AI に要求送信

14:00:01.200       Claude API より応答   ai_conversations
                   受信                     last_intent =
                                          "test_drive_booking"
                        │
14:00:01.250       応答パース

14:00:01.300       ai_messages INSERT       ai_messages
                   (sender=ai)                 ↑ +1

14:00:01.350       chat_history 保存
                   (長期分析用)

14:00:01.400       HTTP レスポンス構築

14:00:01.450       HTTP 200 返却

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
          📊 処理時間: 1.45 秒 (processingTimeMs: 1450)
```

---

## エラーハンドリング経路図

```
┌────────────────────────────────┐
│ メッセージ処理 開始              │
└──────────┬─────────────────────┘
           │
           ▼
       ┌─────────────┐
       │ 入力検証    │
       │ 成功?       │
       └──┬──────┬──┘
          │ NO   │ YES
          │      └─────────┐
          ▼                 │
    ┌──────────┐            │
    │ 400 Bad  │            │
    │ Request  │            │
    └──────────┘            │
                            ▼
                   ┌──────────────────┐
                   │ タイムアウト設定 │
                   │ (3600 sec)       │
                   └────┬──┬──────────┘
                        │  │
                   成功  │  │ timeout
                        │  │
                        ▼  ▼
                   ┌──────────────┐
                   │ AI 処理開始  │
                   └────┬──┬──────┘
                        │  │
                   成功  │  │ timeout/error
                        │  │
                        ▼  ▼
                   ┌──────────────┐
                   │ 504 Gateway  │
                   │ Timeout      │
                   └──────────────┘

┌────────────────────────────────┐
│ AI 応答生成 エラーハンドリング  │
└──────────┬─────────────────────┘
           │
           ▼
  ┌──────────────────┐
  │ Claude API 呼び出し
  └──┬────────────┬──┘
     │ success    │ exception
     │            │
     ▼            ▼
  ┌──────┐    ┌─────────────────┐
  │ Parse│    │ Qwen へ自動F/O  │
  │      │    │ (retry)         │
  └──┬───┘    └────┬──┬──────────┘
     │             │  │
     │ success      │  │ exception
     │             │  │
     ▼             ▼  ▼
  ┌────────┐    ┌──────────────────┐
  │ Return│    │ Gemini へ自動F/O │
  │ response
  └────────┘    └────┬──┬──────────┘
                     │  │
                     │  │ exception
                     │  │
                     ▼  ▼
                  ┌──────────────────┐
                  │ Ollama へ自動F/O │
                  └────┬──┬──────────┘
                       │  │
                       │  │ exception
                       │  │
                       ▼  ▼
                  ┌──────────────────┐
                  │ 500 Internal     │
                  │ Server Error     │
                  │ "すべてのプロバイダー失敗"
                  └──────────────────┘
```

---

## デバッグ情報フロー

```
┌─────────────────────────────┐
│ SendMessage 開始             │
│ ログレベル: LogInformation  │
└──────────┬──────────────────┘
           │
           ├─ "[SendMessage] 開始: Conv={ConvId}, Message={Message}"
           │
           ▼
       ┌──────────────────────┐
       │ エスカレーション検出  │
       │ ログレベル: LogInfo  │
       └──────────┬───────────┘
                   │
                   ├─ "エスカレーション検出: keyword={Keyword}"
                   │  [オプション]
                   │
                   ▼
           ┌──────────────────────┐
           │ Slot-filling 処理    │
           │ ログレベル: LogInfo  │
           └──────────┬───────────┘
                       │
                       ├─ "🔍 Slot-fillingチェック: _slotFilling={Bool}"
                       ├─ "📋 アクティブシナリオ: {Scenario}"
                       ├─ "🎯 意図分類結果: Intent={Intent}"
                       ├─ "🔄 シナリオマップ: {Mapping}"
                       ├─ "🚀 Slot-filling新規セッション開始"
                       ├─ "Slot-filling: AIに状態メッセージを注入"
                       │
                       ▼
           ┌──────────────────────┐
           │ AI 応答生成          │
           │ ログレベル: LogInfo  │
           └──────────┬───────────┘
                       │
                       ├─ "AI 応答生成開始：provider={Provider}"
                       ├─ "AI 応答取得完了：responseLength={Length}"
                       │
                       ▼
           ┌──────────────────────┐
           │ DB 更新              │
           │ ログレベル: LogInfo  │
           └──────────┬───────────┘
                       │
                       ├─ "ai_conversations 更新"
                       ├─ "chat_history 保存"
                       │
                       ▼
           ┌──────────────────────┐
           │ 完了                 │
           │ Stopwatch.Stop()     │
           │ processingTimeMs 計測
           └──────────────────────┘

ログレベル別使い分け:
  [LogInformation] ← 正常系・進行状況
  [LogWarning]     ← エッジケース・条件付き
  [LogError]       ← 例外・失敗
```

---

*このドキュメントはビジュアルフロー図集です。ダイアグラムは ASCII アート形式で表現されています。実装時の参考資料としてください。*