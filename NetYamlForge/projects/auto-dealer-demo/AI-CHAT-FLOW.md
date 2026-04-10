# 自動車販売子システム AI チャット流程详细说明

> **版本**: 1.0  
> **最后更新**: 2026 年 4 月 9 日  
> **作成者**: Claude AI Assistant

---

## 📋 目次

1. [全体アーキテクチャ](#全体アーキテクチャ)
2. [HTTP エンドポイント構成](#httpエンドポイント構成)
3. [ユーザーメッセージ受信から応答返信までの詳細フロー](#ユーザーメッセージ受信から応答返信までの詳細フロー)
4. [各処理ステップの詳細](#各処理ステップの詳細)
5. [エスカレーション・ハンドオーバーフロー](#エスカレーションハンドオーバーフロー)
6. [Slot-filling（槽位充填）フロー](#slot-filling槽位充填フロー)
7. [データベース操作と永続化](#データベース操作と永続化)
8. [エラーハンドリング・タイムアウト](#エラーハンドリングタイムアウト)

---

## 全体アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│  フロントエンド（Web ウィジェット / モバイル）              │
└────────────────────┬────────────────────────────────────────┘
                     │ HTTP API リクエスト
                     ▼
┌──────────────────────────────────────────────────────────────┐
│  AutoDealerChatController (API レイヤー)                     │
│  - POST /api/ai/chat/session/{id}/message                   │
│  - POST /api/ai/chat/session                                │
│  - GET  /api/ai/chat/session/{id}/messages                  │
└────────────────────┬─────────────────────────────────────────┘
                     │ 
                     ▼
┌──────────────────────────────────────────────────────────────┐
│  AutoDealerChatService (ビジネスロジック)                    │
│  - SendMessageAsync()  [顧客メッセージ処理]                  │
│  - SendStaffMessageAsync()  [従業員メッセージ処理]          │
│  - OperatorReplyAsync()  [オペレーター返信]                  │
└────────────────────┬─────────────────────────────────────────┘
                     │
        ┌────────────┼────────────┐
        │            │            │
        ▼            ▼            ▼
   ┌────────┐  ┌──────────┐  ┌─────────────┐
   │AI      │  │Slot-     │  │Intent       │
   │プロバ  │  │filling   │  │Classifier   │
   │イダー  │  │(Optional)│  │(Optional)   │
   └────────┘  └──────────┘  └─────────────┘
        │            │            │
        └────────────┼────────────┘
                     │
                     ▼
         ┌──────────────────────┐
         │ Database操作         │
         │ - ai_conversations   │
         │ - ai_messages        │
         │ - ai_handovers       │
         │ - sales_leads        │
         └──────────────────────┘
                     │
                     ▼
         ┌──────────────────────┐
         │ HTTP Response(JSON)  │
         └──────────────────────┘
```

---

## HTTP エンドポイント構成

### 顧客向けエンドポイント（認証不要）

| HTTP メソッド | エンドポイント | 説明 | リクエスト | レスポンス |
|---|---|---|---|---|
| **POST** | `/api/ai/chat/session` | セッション開始 | `{channel, guestSessionId}` | `{conversationId, welcomeMessage}` |
| **POST** | `/api/ai/chat/session/{id}/message` | メッセージ送信 | `{message, provider?}` | `{responseText, intent, ...}` |
| **GET** | `/api/ai/chat/session/{id}/messages` | 会話履歴取得 | - | `[{sender, content, ...}]` |
| **GET** | `/api/ai/chat/session/{id}/updates` | ポーリング更新 | `since?` | `[{message}]` |
| **POST** | `/api/ai/chat/session/{id}/feedback` | フィードバック送信 | `{rating, comment?}` | `{success}` |
| **POST** | `/api/ai/chat/session/{id}/close` | セッション終了 | - | `{message}` |

### 従業員向けエンドポイント（`@Authorize` 必須）

| HTTP メソッド | エンドポイント | 説明 |
|---|---|---|
| **POST** | `/api/ai/chat/staff/session` | 従業員セッション開始 |
| **POST** | `/api/ai/chat/staff/{id}/message` | 従業員メッセージ送信 |
| **GET** | `/api/ai/chat/staff/conversations` | 従業員会話一覧（TODO） |

### オペレーター向けエンドポイント（`@Authorize` 必須）

| HTTP メソッド | エンドポイント | 説明 |
|---|---|---|
| **POST** | `/api/ai/chat/session/{id}/operator-reply` | 顧客への返信 |
| **POST** | `/api/ai/chat/session/{id}/accept` | エスカレーション担当 |
| **POST** | `/api/ai/chat/session/{id}/resolve` | エスカレーション解決 |
| **GET** | `/api/ai/chat/handover/{id}` | エスカレーション詳細 |
| **GET** | `/api/ai/chat/session/{id}/history` | 会話履歴（オペレーター用） |

---

## ユーザーメッセージ受信から応答返信までの詳細フロー

### [1] HTTP リクエスト受信

```
POST /{project}/api/ai/chat/session/{conversationId}/message
Content-Type: application/json

{
  "message": "試乗予約をしたいのですが...",
  "provider": "claude"  // オプション
}
```

### [2] AutoDealerChatController.SendMessage()

**役割**: HTTP 入力の検証、タイムアウト管理

```csharp
// 入力検証
if (string.IsNullOrWhiteSpace(req.Message))
    return BadRequest(new { error = "メッセージが空です。" });

// タイムアウト処理（60分）
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3600));
var task = _chat.SendMessageAsync(conversationId, req.Message, req.Provider);

// タイムアウトチェック
var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
if (completedTask != task)
    return StatusCode(504, new { error = "応答がタイムアウトしました（3600秒）。" });

return Ok(await task);
```

### [3] AutoDealerChatService.SendMessageAsync()

**役割**: メインビジネスロジック（10 段階プロセス）

#### **ステップ 3-0: エスカレーション・感情判定**

```csharp
var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

// エスカレーション検出（キーワード分析）
var (escalationIntent, needsHandover, priority) = DetectEscalation(customerMessage);

// 感情スコア推定（テキスト分析）
var sentimentScore = EstimateSentiment(customerMessage);

// 早期エスカレーション判定
if (needsHandover || sentimentScore < -0.5)
    return await HandleEscalationAsync(...);
```

**判定ロジック**:
- `needsHandover = true` → オペレーター接続必須
- `sentimentScore < -0.5` → 顧客不満あり → エスカレーション推奨

#### **ステップ 3-1: メッセージ保存（受信側）**

```csharp
var customerId = await _db.QueryFirstOrDefaultAsync<string>(
    "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
    new { Id = conversationId });

await SaveMessageAsync(
    messageId: $"MSG-{Guid.NewGuid():N}"[..32],
    conversationId: conversationId,
    sender: "customer",
    content: customerMessage,
    timestamp: now
);
```

**挿入先**: `ai_messages` テーブル

```sql
INSERT INTO ai_messages 
  (message_id, conversation_id, sender, message_type, content, timestamp)
VALUES 
  ('MSG-xxxx', 'CONV-xxxx', 'customer', 'text', '試乗予約...', '2026-04-09 14:30:00')
```

#### **ステップ 3-2: Slot-filling セッション確認（オプション）**

```csharp
if (_slotFilling != null)
{
    // アクティブなシナリオを確認
    var activeScenario = await _slotFilling.GetActiveScenarioAsync(conversationId);
    
    if (_intentClassifier != null)
    {
        // インテント分類
        var intentCheck = await _intentClassifier.ClassifyAsync(
            customerMessage, projectId: _projectName);
        
        // シナリオマッピング（"test_drive_booking" → "test_drive"）
        var newScenario = MapIntentToScenario(intentCheck.Intent);
        
        // 新しいセッション開始
        if (newScenario != null && activeScenario == null)
            await _slotFilling.GetSessionAsync(conversationId, newScenario, _projectName);
    }
}
```

**シナリオマッピング**:

| インテント | シナリオ | 説明 |
|---|---|---|
| `test_drive_booking` | `test_drive` | 試乗予約 |
| `estimate_request` | `estimate` | 見積もり依頼 |
| `service_booking` | `appointment_service` | サービス予約 |
| `trade_inquiry` | `trade_in` | 下取り査定 |

#### **ステップ 3-3: スロット値抽出（Slot-filling 対象の場合）**

```csharp
if (activeScenario != null)
{
    // ユーザーメッセージからスロット値を抽出
    await ExtractSlotValuesFromMessageAsync(
        conversationId, customerMessage, activeScenario);
    
    // セッション情報を更新・確認
    var activeSession = await _slotFilling.GetSessionAsync(
        conversationId, activeScenario, _projectName);
    
    // 全スロット埋まったかチェック
    if (activeSession.IsComplete)
    {
        // → ステップ 3-4a へ（予約確定処理）
    }
    else
    {
        // → ステップ 3-4b へ（AI に会話委譲）
    }
}
```

**Slot-filling 対象フィールド例**:

```
test_drive シナリオ:
  - vehicle_model（車種）
  - preferred_date（希望日）
  - preferred_time（希望時間）
  - customer_name（お名前）
  - customer_phone（電話番号）
```

#### **ステップ 3-4a: Slot-filling 完了 → 予約確定**

```csharp
// 全スロット埋まり
if (activeSession.IsComplete)
{
    var slots = activeSession.GetCollectedValues();
    
    // シナリオ別の確定処理を実行
    var (completionText, navUrl, navLabel) = 
        await CompleteScenarioAsync(conversationId, activeScenario, slots);
    
    // 例：試乗予約確定
    // → service_appointments テーブルに INSERT
    // → メール送信
    // → リード生成（sales_leads）
}
```

**出力例**:
```
completionText: "試乗予約をお受けいたしました。
  2026年4月15日 14:00 にお越しください。
  確認メールをお送りしました。"
  
navUrl: "/auto-dealer-demo/DynamicEntity/Index?entity=service_appointments"
navLabel: "予約確認"
```

#### **ステップ 3-4b: Slot-filling 未完成 → AI に委譲**

```csharp
// スロット状態をメッセージに注入
var collectedSlots = await _slotFilling.GetCollectedSlotsAsync(conversationId, _projectName);
var nextSlot = await _slotFilling.GetNextRequiredSlotAsync(
    conversationId, activeScenario, _projectName);

// システムメッセージを構築
var slotStatusMessage = BuildSlotStatusMessage(collectedSlots, nextSlot, activeScenario);

// 会話履歴に注入（AI が認識できるように）
historyForAI.Insert(0, ("system", slotStatusMessage));
```

**注入メッセージ例**:
```markdown
## 📋 試乗予約 - 情報収集状況（システム指示）

✅ **既に収集済みの情報:**
- 車種: トヨタ カムリ
- グレード: LE

🎯 **次のアクション（必須）:**
ユーザーに以下の質問をそのまま伝えてください：

> **試乗希望日をお知らせください（例: 4月15日, 明日など）**

**重要ルール:**
1. 上記の質問だけをユーザーに伝えてください
2. 他の情報を一緒に聞かないでください
3. まだ収集していない情報: 希望日、希望時間、お名前、電話番号
4. ユーザーが他のことを聞いても、まずはこの質問に答えてもらってください
```

#### **ステップ 3-5: AI 応答生成**

```csharp
// システムプロンプト構築
var systemPrompt = BuildSystemPrompt("customer", dbContextMarkdown: null);

// AI に送信
var prompt = BuildPromptWithHistory(customerMessage, historyForAI);
var response = await GenerateAiResponseAsync(
    message: customerMessage,
    context: "customer",
    history: historyForAI,
    providerOverride: providerOverride
);
```

**構築されるプロンプト**:
```
=== SYSTEM PROMPT ===
あなたは AI 自動車販売サポートです。
顧客向けチャットシステムのプロンプトを使用します。

<以下、auto-dealer/_system-prompt-customer.md の内容>

=== CONVERSATION HISTORY ===
[会話履歴を時系列に追加]

User: "試乗予約をしたいのですが..."
Assistant: "かしこまりました。試乗予約のお手伝いをさせます。..."
User: "試乗予約をしたいのですが..."
```

**プロバイダー チェーン**:

1. `providerOverride` が指定 → そのプロバイダーを使用
2. 指定なし → `_cliFactory` で利用可能なプロバイダーを試行
   - 優先順位: `claude` → `qwen` → `gemini` → `ollama`

#### **ステップ 3-6: AI 応答の処理**

```csharp
var (aiResponseText, aiIntent, aiDataRows, aiNavUrl, aiNavLabel) =
    await GenerateAiResponseAsync(customerMessage, "customer", historyForAI, providerOverride);

// ProcessAiResponseAsync で応答をパース
// - インテント抽出
// - データ行（テーブル）抽出
// - ナビゲーション URL 抽出
```

**返却構造**:
```csharp
(
    ResponseText: "試乗予約いただき...",          // AI のテキスト応答
    Intent: "test_drive_booking",               // 推定インテント
    DataRows: null,                             // テーブルデータ（該当なし）
    NavUrl: "/auto-dealer-demo/DynamicEntity/Index?entity=vehicles",
    NavLabel: "車一覧へ"
)
```

#### **ステップ 3-7: AI 応答を保存**

```csharp
var aiResponseTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
var usedProvider = providerOverride ?? _defaultProvider;

// AI メッセージを ai_messages に保存
await SaveMessageAsync(
    messageId: $"MSG-{Guid.NewGuid():N}"[..32],
    conversationId: conversationId,
    sender: "ai",
    content: aiResponseText,
    timestamp: aiResponseTime,
    intent: aiIntent,
    confidenceScore: 0.9,
    sentimentScore: sentimentScore
);

// 会話メタデータを ai_conversations に保存
await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET last_intent = @Intent, 
    last_confidence = 0.9, 
    sentiment_score = @Sentiment, 
    updated_at = @Now
WHERE conversation_id = @Id",
    new { Intent = aiIntent, Sentiment = sentimentScore, Now = now, Id = conversationId });
```

**更新内容**:
```
ai_conversations テーブル:
  last_intent: "test_drive_booking"
  last_confidence: 0.9
  sentiment_score: 0.3（中立的）
  updated_at: "2026-04-09 14:30:15"
```

#### **ステップ 3-8: チャット履歴に保存**

```csharp
await _chatHistory.SaveMessageAsync(
    userId: customerId ?? _projectName,
    messageContent: customerMessage,
    role: "user",
    provider: usedProvider,
    chatContext: "dealer-customer",
    projectName: _projectName
);

await _chatHistory.SaveMessageAsync(
    userId: customerId ?? _projectName,
    messageContent: aiResponseText,
    role: "assistant",
    provider: usedProvider,
    chatContext: "dealer-customer",
    projectName: _projectName
);
```

**保存先**: `chat_history` テーブル（長期分析用）

#### **ステップ 3-9: クイックリプライの生成**

```csharp
var quickReplies = GetQuickReplies("customer", aiIntent);
// → ["確認する", "キャンセル", "別の車を見る", "その他のご質問"]
```

#### **ステップ 3-10: HTTP レスポンス構築**

```csharp
var stopwatch = System.Diagnostics.Stopwatch.StartNew();
stopwatch.Stop();

return new ChatMessageResult
{
    ResponseText = aiResponseText,
    Intent = aiIntent,
    SuggestHandover = false,
    QuickReplies = quickReplies,
    ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds,
    DataRows = aiDataRows,
    NavigationUrl = aiNavUrl,
    NavigationLabel = aiNavLabel,
    AiProvider = usedProvider,
    MessageTimestamp = aiResponseTime,
    Components = BuildComponents(aiIntent, aiDataRows, "customer")
};
```

**返却 JSON**:
```json
{
  "responseText": "試乗予約をお承りいたします。ご希望の日時をお選びください。",
  "intent": "test_drive_booking",
  "suggestHandover": false,
  "quickReplies": ["確認する", "キャンセル", "別の車を見る"],
  "processingTimeMs": 1250,
  "dataRows": null,
  "navigationUrl": "/auto-dealer-demo/DynamicEntity/Index?entity=vehicles",
  "navigationLabel": "車一覧へ",
  "aiProvider": "claude",
  "messageTimestamp": "2026-04-09T14:30:15Z",
  "components": [...]
}
```

---

## 各処理ステップの詳細

### A. エスカレーション検出（DetectEscalation）

**目的**: キーワード分析で「オペレーター対応が必要」かどうかを判定

```csharp
private (string Intent, bool NeedsHandover, string Priority) DetectEscalation(string message)
{
    var lowerMsg = message.ToLower();
    
    // エスカレーションキーワード
    var criticalKeywords = new[] { "苦情", "返金", "詐欺", "警察", "弁護士" };
    var urgentKeywords = new[] { "至急", "緊急", "困っている", "すぐに" };
    
    var hasCritical = criticalKeywords.Any(k => lowerMsg.Contains(k));
    var hasUrgent = urgentKeywords.Any(k => lowerMsg.Contains(k));
    
    return (
        Intent: hasCritical ? "escalation_critical" : hasUrgent ? "escalation_urgent" : "normal",
        NeedsHandover: hasCritical || hasUrgent,
        Priority: hasCritical ? "critical" : hasUrgent ? "urgent" : "normal"
    );
}
```

### B. 感情スコア推定（EstimateSentiment）

**目的**: テキスト感情分析で、顧客の満足度を -1.0 ～ +1.0 で数値化

```csharp
private double EstimateSentiment(string message)
{
    // 簡易的な感情分析（実装は sentiment-analysis ライブラリ等を使用）
    var positiveWords = new[] { "素晴らしい", "最高", "感謝", "ありがとう" };
    var negativeWords = new[] { "つまらない", "最悪", "怒り", "困る", "不満" };
    
    var positive = positiveWords.Count(w => message.Contains(w));
    var negative = negativeWords.Count(w => message.Contains(w));
    
    return (positive - negative) / (double)(positive + negative + 1);
}
```

**判定基準**:
- `score >= 0.5`: ポジティブ
- `0.5 > score >= -0.5`: ニュートラル
- `score < -0.5`: ネガティブ → エスカレーション推奨

### C. インテント分類（IntentClassifier）

**目的**: ユーザーの意図を自動分類（オプション機能）

```csharp
public interface IIntentClassifier
{
    Task<IntentResult> ClassifyAsync(string message, string projectId);
}

public class IntentResult
{
    public string Intent { get; set; }           // "test_drive_booking", "vehicle_inquiry", etc.
    public double Confidence { get; set; }       // 0.0 ~ 1.0
    public string Method { get; set; }          // "rule_based", "ml_model", "fallback"
    public List<string> AlternativeIntents { get; set; }
}
```

**統合例**:
```
Rule-based (ルールベース) → ML Model (機械学習) → Fallback (デフォルト)
```

### D. Slot-filling セッション管理

**目的**: 複数ターンでユーザー情報を段階的に収集

```csharp
public interface ISlotFillingManager
{
    Task<SlotFillingSession> GetSessionAsync(string conversationId, string scenario, string projectId);
    Task<string?> GetActiveScenarioAsync(string conversationId);
    Task<Dictionary<string, string>> GetCollectedSlotsAsync(string conversationId, string projectId);
    Task<SlotRequest?> GetNextRequiredSlotAsync(string conversationId, string scenario, string projectId);
}

public class SlotFillingSession
{
    public bool IsComplete { get; set; }
    public Dictionary<string, string> GetCollectedValues() { ... }
}
```

**ライフサイクル**:

```
1. 新規セッション開始
   GetSessionAsync(convId, "test_drive", "auto-dealer-demo")
   
2. スロット値抽出
   ExtractSlotValuesFromMessageAsync(convId, message, "test_drive")
   
3. セッション確認
   session = GetSessionAsync(...)
   if (session.IsComplete) → 予約確定
   else → AI に会話委譲
   
4. セッションリセット（会話終了時）
   ResetAsync(convId, "auto-dealer-demo")
```

---

## エスカレーション・ハンドオーバーフロー

### エスカレーション検出後の処理

```csharp
if (needsHandover || sentimentScore < -0.5)
    return await HandleEscalationAsync(
        conversationId, 
        customerMessage, 
        escalationIntent, 
        priority, 
        sentimentScore, 
        now, 
        sw
    );
```

### HandleEscalationAsync の処理

```csharp
private async Task<ChatMessageResult> HandleEscalationAsync(...)
{
    // 1. ai_handovers レコード作成
    var handoverId = $"HAND-{Guid.NewGuid():N}"[..32];
    
    await _db.ExecuteAsync(@"
    INSERT INTO ai_handovers 
      (handover_id, conversation_id, reason, priority, status, created_at)
    VALUES
      (@HandoverId, @ConvId, @Reason, @Priority, 'pending', @Now)",
        new { HandoverId = handoverId, ConvId = conversationId, Reason = escalationIntent, Priority = priority, Now = now });
    
    // 2. Slot-filling をクリア
    if (_slotFilling != null)
        await _slotFilling.ResetAsync(conversationId, _projectName);
    
    // 3. ai_conversations を更新
    await _db.ExecuteAsync(@"
    UPDATE ai_conversations
    SET status = 'escalated', assigned_to_user_id = NULL, updated_at = @Now
    WHERE conversation_id = @ConversationId",
        new { ConversationId = conversationId, Now = now });
    
    // 4. オペレーター用メッセージを作成
    var escalationMsg = $"申し訳ございません。お客様の内容について、詳しいスタッフが確認させていただきます。";
    
    return new ChatMessageResult
    {
        ResponseText = escalationMsg,
        SuggestHandover = true,
        ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
        MessageTimestamp = now
    };
}
```

**ai_handovers テーブル構造**:

```
CREATE TABLE ai_handovers (
    handover_id         VARCHAR(64) PRIMARY KEY,
    conversation_id     VARCHAR(64) NOT NULL,
    reason              VARCHAR(100),          -- escalation_critical, escalation_urgent, etc.
    priority            VARCHAR(20),           -- critical, urgent, normal
    status              VARCHAR(20),           -- pending, accepted, resolved
    assigned_to_user_id VARCHAR(50),
    resolution_notes    TEXT,
    created_at          DATETIME NOT NULL,
    resolved_at         DATETIME
);
```

---

## Slot-filling（槽位充填）フロー

### 試乗予約シナリオの例

```
ユーザー: "試乗したいです"
  ↓ [インテント分類] → "test_drive_booking"
  ↓ [Slot-filling 開始] → "test_drive" シナリオ
  
AI: "どの車種をお考えですか？"
ユーザー: "トヨタ カムリ"
  ↓ [スロット抽出] vehicle_model = "トヨタ カムリ"
  ↓ [次のスロット] → "preferred_date"
  
AI: "試乗希望日はいつですか？"
ユーザー: "明日"
  ↓ [スロット抽出] preferred_date = "2026-04-10"
  ↓ [次のスロット] → "preferred_time"
  
AI: "何時にお越しですか？"
ユーザー: "14:00"
  ↓ [スロット抽出] preferred_time = "14:00"
  ↓ [次のスロット] → "customer_name"
  
AI: "お名前をお伺いします"
ユーザー: "田中太郎"
  ↓ [スロット抽出] customer_name = "田中太郎"
  ↓ [次のスロット] → "customer_phone"
  
AI: "電話番号をお伺いします"
ユーザー: "090-1234-5678"
  ↓ [スロット抽出] customer_phone = "090-1234-5678"
  ↓ [全スロット完成] → CompleteScenarioAsync へ
  
AI: "試乗予約をお受けいたしました！"
     [service_appointments に INSERT]
     [メール送信]
     [sales_leads に INSERT]
```

### スロット抽出の仕組み（ExtractSlotValuesFromMessageAsync）

```csharp
private async Task ExtractSlotValuesFromMessageAsync(
    string conversationId, string message, string scenario)
{
    // 1. 規則ベース抽出（電話番号、日付パターン等）
    var ruleBasedSlots = ExtractByRegex(message, scenario);
    
    foreach (var (slotName, value) in ruleBasedSlots)
    {
        await _slotFilling.SetSlotValueAsync(conversationId, scenario, slotName, value, _projectName);
    }
    
    // 2. AI ベース抽出（複雑な値）
    var aiExtractedSlots = await ExtractByAiAsync(message, scenario);
    
    foreach (var (slotName, value) in aiExtractedSlots)
    {
        await _slotFilling.SetSlotValueAsync(conversationId, scenario, slotName, value, _projectName);
    }
}
```

**規則ベース抽出の例**:
```
Phone: /\d{3}-\d{4}-\d{4}/
Date:  /(明日|あさって|\d{1,2}月\d{1,2}日)/
Time:  /(\d{1,2}):(\d{2})/
```

---

## データベース操作と永続化

### テーブル構造概要

#### 1. `ai_conversations` - 会話セッション

```sql
CREATE TABLE ai_conversations (
    conversation_id     VARCHAR(64) PRIMARY KEY,
    customer_id         VARCHAR(50),            -- 認証済みユーザー
    guest_session_id    VARCHAR(64),            -- ゲストセッション ID
    channel             VARCHAR(20),            -- web, voice, line, email, sms, tablet
    status              VARCHAR(30),            -- active, completed, escalated, abandoned
    last_intent         VARCHAR(100),
    last_confidence     DECIMAL(10,4),
    sentiment_score     DECIMAL(10,4),
    context_data        TEXT,                   -- JSON（Slot状態等）
    feedback_rating     INT,                    -- 1-5
    feedback_comment    TEXT,
    assigned_to_user_id VARCHAR(50),            -- エスカレーション担当者
    started_at          DATETIME NOT NULL,
    ended_at            DATETIME,
    created_at          DATETIME NOT NULL,
    updated_at          DATETIME NOT NULL
);
```

#### 2. `ai_messages` - メッセージログ

```sql
CREATE TABLE ai_messages (
    message_id          VARCHAR(64) PRIMARY KEY,
    conversation_id     VARCHAR(64) NOT NULL,
    sender              VARCHAR(20),            -- customer, ai, agent
    message_type        VARCHAR(20),            -- text, voice_transcript, image, etc.
    content             TEXT NOT NULL,
    intent              VARCHAR(100),
    entities_json       TEXT,
    confidence_score    DECIMAL(10,4),
    sentiment_score     DECIMAL(10,4),
    metadata_json       TEXT,
    timestamp           DATETIME NOT NULL,
    FOREIGN KEY (conversation_id) REFERENCES ai_conversations(conversation_id)
);
```

#### 3. `ai_handovers` - エスカレーション

```sql
CREATE TABLE ai_handovers (
    handover_id         VARCHAR(64) PRIMARY KEY,
    conversation_id     VARCHAR(64) NOT NULL,
    reason              VARCHAR(100),
    priority            VARCHAR(20),
    status              VARCHAR(20),            -- pending, accepted, resolved
    assigned_to_user_id VARCHAR(50),
    resolution_notes    TEXT,
    created_at          DATETIME NOT NULL,
    resolved_at         DATETIME,
    FOREIGN KEY (conversation_id) REFERENCES ai_conversations(conversation_id)
);
```

#### 4. `ai_knowledge` - ナレッジベース

```sql
CREATE TABLE ai_knowledge (
    knowledge_id        VARCHAR(64) PRIMARY KEY,
    category            VARCHAR(50),
    title               VARCHAR(255),
    content             TEXT,
    source_url          VARCHAR(255),
    updated_at          DATETIME
);
```

### トランザクション管理

**重要**: すべてのメッセージ保存操作は**同じ DB トランザクション内**で実行

```csharp
using var transaction = _db.BeginTransaction();
try
{
    // 1. ai_messages に INSERT
    await SaveMessageAsync(...);
    
    // 2. ai_conversations を UPDATE
    await _db.ExecuteAsync(@"UPDATE ai_conversations SET ...", ...);
    
    // 3. Slot-filling セッションを更新
    if (_slotFilling != null)
        await _slotFilling.GetSessionAsync(...);
    
    transaction.Commit();
}
catch (Exception ex)
{
    transaction.Rollback();
    throw;
}
```

---

## エラーハンドリング・タイムアウト

### コントローラーレベルでのタイムアウト管理

```csharp
// 最大 3600 秒（60 分）
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3600));

try
{
    var task = _chat.SendMessageAsync(conversationId, req.Message, req.Provider);
    var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
    
    if (completedTask != task)
    {
        // タイムアウト
        return StatusCode(504, new { error = "応答がタイムアウトしました（3600秒）。" });
    }
    
    return Ok(await task);
}
catch (OperationCanceledException)
{
    return StatusCode(504, new { error = "リクエストがキャンセルされました。" });
}
catch (Exception ex)
{
    _logger.LogError(ex, "メッセージ処理エラー conv={Id}", conversationId);
    return StatusCode(500, new { error = "メッセージの処理に失敗しました。" });
}
```

### AI 応答生成のタイムアウト

```csharp
// AI 応答タイムアウト（設定可能、デフォルト 3600 秒）
_chatResponseTimeoutSeconds = int.TryParse(
    config["AiWindow:ChatResponseTimeoutSeconds"], out var timeout) && timeout > 0
    ? timeout
    : 3600;

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_chatResponseTimeoutSeconds));

try
{
    var response = await ExecuteWithSystemPromptOverrideAsync(prompt, systemPrompt, cts.Token, providerOverride);
    return (response, "success", null, null, null);
}
catch (OperationCanceledException)
{
    _logger.LogWarning("AI 応答生成がタイムアウトしました（{Timeout}秒）", _chatResponseTimeoutSeconds);
    return ("申し訳ございませんが、応答生成に時間がかかりすぎています。", "timeout", null, null, null);
}
```

### リトライ戦略

**プロバイダー フォールバック** （自動リトライ）

```csharp
// 1. Claude が失敗 → Qwen へ自動切り替え
// 2. Qwen が失敗 → Gemini へ自動切り替え
// 3. Gemini が失敗 → Ollama へ自動切り替え
// 4. すべて失敗 → エラーレスポンス

var providerChain = new[] { "claude", "qwen", "gemini", "ollama" };

foreach (var provider in providerChain)
{
    try
    {
        return await ExecuteProviderAsync(provider, prompt, cts.Token);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "プロバイダー {Provider} が失敗、次を試行", provider);
    }
}

throw new InvalidOperationException("すべての CLI プロバイダーが失敗しました");
```

---

## ロギング・デバッグ情報

### ログレベル別出力

```
[LogInformation] - ビジネスロジック進行状況
  [SendMessage] 開始: Conv={ConvId}, Message={Message}
  StartSessionAsync 開始。channel: {Channel}, customerId: {CustomerId}
  
[LogWarning] - 条件付きエラー
  メッセージ処理タイムアウト conv={Id} ({Timeout}秒)
  ⚠️ _intentClassifier が null です。意図分類をスキップします。
  
[LogError] - 例外・失敗
  メッセージ処理エラー conv={Id}
  すべての CLI プロバイダーが失敗しました
```

### スローダウン時のデバッグ

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();

// ... 処理 ...

sw.Stop();
_logger.LogInformation("処理完了: {Duration}ms", sw.ElapsedMilliseconds);

// processingTimeMs をレスポンスに含める
return new ChatMessageResult
{
    ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
    ...
};
```

---

## 実装チェックリスト

- [ ] `AutoDealerChatController` - HTTP 入力検証、タイムアウト管理
- [ ] `AutoDealerChatService.SendMessageAsync()` - メインロジック実装
- [ ] `BaseChatService.GenerateAiResponseAsync()` - AI 統合
- [ ] `IntentClassifier` - インテント分類（オプション）
- [ ] `ISlotFillingManager` - Slot-filling 管理
- [ ] データベーススキーマ - テーブル作成・初期データ
- [ ] エスカレーション検出ロジック - キーワード・感情分析
- [ ] エラーハンドリング - タイムアウト・リトライ
- [ ] ロギング - デバッグ情報出力

---

*このドキュメントは `auto-dealer-demo` プロジェクトの AI チャット機能の技術仕様です。実装時の参考資料として使用してください。最後に更新されたのは 2026 年 4 月 9 日です。*