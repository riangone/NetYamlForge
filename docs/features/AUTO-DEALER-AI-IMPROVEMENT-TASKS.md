# auto-dealer-demo AI チャット 改善タスク一覧

> **作成日**: 2026-04-08  
> **対象ブランチ**: `feature/jpiere-erp-subproject`  
> **ドキュメント目的**: 他のAIエージェントがそのまま実装できるよう、問題・対象ファイル・期待する修正内容を詳細に記述する。

---

## 前提知識

### プロジェクト構造

```
NetYamlForge/
├── Services/AI/
│   ├── AutoDealerChatService.cs     # auto-dealer専用チャットサービス（メイン）
│   ├── BaseChatService.cs           # 共通ベースクラス
│   ├── SlotFillingManager.cs        # スロット収集管理
│   ├── HybridIntentClassifier.cs    # インテント分類（ルール + LLM）
│   ├── ConversationManager.cs       # 会話セッション管理
│   └── ChatHistoryService.cs        # チャット履歴永続化
├── projects/auto-dealer-demo/
│   ├── entities/ai_conversations.yml
│   ├── entities/service_appointments.yml
│   ├── entities/sales_leads.yml
│   └── Hooks/AutoDealerHooks.cs
└── skills/auto-dealer/
    ├── _system-prompt-customer.md
    ├── _system-prompt-staff.md
    └── _tools-definition.md
```

### 現在のフロー概要（顧客チャット）

```
SendMessageAsync(conversationId, message)
  ├─ [0] アクティブSlot-fillingセッション確認 → 継続（2026-04-08修正済み）
  ├─ [1] HybridIntentClassifier.ClassifyAsync() → インテント判定
  │    ├─ test_drive_booking → ProcessTestDriveSlotFillingAsync()
  │    └─ その他 → LLMフロー
  └─ [2] GenerateAiResponseAsync() → LLM応答生成
```

---

## タスク一覧

| ID | 優先度 | タイトル | 推定難易度 |
|----|--------|----------|-----------|
| T-01 | 🔴 最優先 | estimate/appointment_service/trade_in Slot-fillingフロー実装 | 高 |
| T-02 | 🔴 最優先 | feedback_rating/comment カラムのDBスキーマ追加 | 低 |
| T-03 | 🔴 最優先 | チャットからのリード自動生成実装 | 中 |
| T-04 | 🟡 高優先 | インテント分類にestimate/trade_inルール追加 | 中 |
| T-05 | 🟡 高優先 | Slot-fillingセッションのDB永続化 | 高 |
| T-06 | 🟡 高優先 | スタッフチャットの分析レポート形式強制 | 中 |
| T-07 | 🟡 高優先 | service_appointments INSERTのスキーマ修正 | 低 |
| T-08 | 🟢 中優先 | エスカレーション検出精度向上 | 中 |
| T-09 | 🟢 中優先 | クイックリプライの全インテント対応 | 低 |
| T-10 | 🟢 中優先 | 会話終了時の業務後処理実装 | 中 |

---

## T-01: estimate / appointment_service / trade_in Slot-filling フロー実装

### 問題

`SlotFillingManager.cs` にはシナリオ定義（必要スロット一覧）が存在するが、以下が未実装：

1. これらのシナリオを開始させるルーティングがない（`test_drive_booking` のみが Slot-filling に入る）
2. シナリオ完了時のDB保存処理が存在しない（`CompleteTestDriveBookingAsync` のみ実装済み）
3. `ExtractSlotValuesFromMessageAsync` がこれらシナリオ固有スロット（grade, vehicle_year 等）を抽出できない

### 対象ファイル

- `NetYamlForge/Services/AI/AutoDealerChatService.cs`
- `NetYamlForge/Services/AI/SlotFillingManager.cs`

### 現状コード

**AutoDealerChatService.cs:259**（インテント分岐）:
```csharp
// test_drive_booking のみ Slot-filling に入る
if (resolvedIntent == "test_drive_booking" && _slotFilling != null)
{
    var slotResult = await ProcessTestDriveSlotFillingAsync(conversationId, customerMessage);
    ...
}
```

**SlotFillingManager.cs:134** には `estimate`, `appointment_service`, `trade_in` のシナリオ定義は存在するが、呼び出し元がない。

### 期待する修正内容

#### 1. SendMessageAsync のインテント分岐を拡張

`AutoDealerChatService.cs:259` の条件を以下のように変更：

```csharp
// インテントとシナリオのマッピング
var slotScenario = resolvedIntent switch
{
    "test_drive_booking" => "test_drive",
    "estimate_request"   => "estimate",
    "service_booking"    => "appointment_service",
    "trade_inquiry"      => "trade_in",
    _                    => null
};

if (slotScenario != null && _slotFilling != null)
{
    var slotResult = await ProcessSlotFillingAsync(conversationId, customerMessage, slotScenario);
    ...
}
```

#### 2. `ProcessSlotFillingAsync` の汎用化

現在の `ProcessTestDriveSlotFillingAsync` をシナリオ引数を受け取る汎用メソッドに変更する。

#### 3. シナリオ別完了処理メソッドの追加

`CompleteTestDriveBookingAsync` と同様に以下を追加：

**`CompleteEstimateRequestAsync(conversationId, slots)`**
- DBに `sales_leads` テーブルへINSERT
- `vehicle_interest = slots["vehicle_model"]`、`status = 'new'`、`source_conversation_id = conversationId`
- 必要スロット: `vehicle_model`, `grade`（任意）, `customer_name`, `customer_phone`
- 返却メッセージ例: 「見積もりリクエストを承りました。担当者より〇〇様にご連絡します。」

**`CompleteServiceBookingAsync(conversationId, slots)`**
- DBに `service_appointments` テーブルへINSERT
- `appointment_type = 'service'`
- 必要スロット: `service_type`, `vehicle_model`, `preferred_date`, `preferred_time`, `customer_name`, `customer_phone`
- スキーマ注意: `customer_id` は NOT NULLのため、会話から `customer_id` を引いてセットすること（T-07参照）

**`CompleteTradeInRequestAsync(conversationId, slots)`**
- DBに `sales_leads` テーブルへINSERT
- `status = 'new'`、`vehicle_interest = slots["vehicle_model"]`
- 必要スロット: `vehicle_brand`, `vehicle_model`, `vehicle_year`, `mileage`, `customer_name`, `customer_phone`

#### 4. ExtractSlotValuesFromMessageAsync の拡張

以下スロットの抽出ロジックを追加（`AutoDealerChatService.cs:401`）：

```csharp
// グレード抽出（estimate シナリオ）
var gradePatterns = new[] { "G", "Z", "X", "S", "プレミアム", "スタンダード", "エグゼクティブ" };
foreach (var grade in gradePatterns)
{
    if (message.Contains(grade, StringComparison.OrdinalIgnoreCase))
    {
        await _slotFilling.UpdateSlotAsync(conversationId, "grade", grade, _projectName);
        break;
    }
}

// 年式抽出（trade_in シナリオ）
var yearMatch = Regex.Match(message, @"(20\d{2}|平成\d+|令和\d+)年");
if (yearMatch.Success)
    await _slotFilling.UpdateSlotAsync(conversationId, "vehicle_year", yearMatch.Value, _projectName);

// 走行距離抽出（trade_in シナリオ）
var mileageMatch = Regex.Match(message, @"(\d+(?:\.\d+)?)\s*万\s*km");
if (mileageMatch.Success)
    await _slotFilling.UpdateSlotAsync(conversationId, "mileage", mileageMatch.Value, _projectName);

// サービス種別抽出（appointment_service シナリオ）
var serviceKeywords = new Dictionary<string, string>
{
    { "車検" , "車検" }, { "点検" , "定期点検" }, { "オイル" , "オイル交換" },
    { "タイヤ" , "タイヤ交換" }, { "修理" , "修理" }, { "板金" , "板金塗装" }
};
foreach (var (keyword, serviceType) in serviceKeywords)
{
    if (lowerMessage.Contains(keyword))
    {
        await _slotFilling.UpdateSlotAsync(conversationId, "service_type", serviceType, _projectName);
        break;
    }
}
```

---

## T-02: feedback_rating / feedback_comment カラムの DB スキーマ追加

### 問題

`BaseChatService.cs:326` に以下のUPDATE文があるが、`ai_conversations` テーブルにカラムが存在しない：

```sql
SET feedback_rating = @Rating, feedback_comment = @Comment, updated_at = @Now
```

実行時エラーが発生する（`SQLiteException: table ai_conversations has no column named feedback_rating`）。

### 対象ファイル

- `NetYamlForge/projects/auto-dealer-demo/entities/ai_conversations.yml`
- `NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db`（マイグレーション）

### 期待する修正内容

#### 1. `ai_conversations.yml` にカラム追加

`columns` セクションに以下を追加（既存の `updated_at` の前に挿入）：

```yaml
- name: feedback_rating
  type: integer
  label: 評価（1〜5）
  required: false

- name: feedback_comment
  type: text
  label: フィードバックコメント
  required: false
```

#### 2. SQLite マイグレーション（直接 ALTER TABLE）

```sql
ALTER TABLE ai_conversations ADD COLUMN feedback_rating INTEGER;
ALTER TABLE ai_conversations ADD COLUMN feedback_comment TEXT;
```

このSQLを `NetYamlForge/projects/auto-dealer-demo/database/` に `migration_feedback_columns.sql` として保存するか、直接 sqlite3 コマンドで実行する。

---

## T-03: チャットからのリード自動生成実装

### 問題

`ai_conversations.yml:159-166` に以下のフック定義があるが、対応する C# フック実装が存在しない：

```yaml
afterCreate:
  - auto_create_lead_from_conversation  # 完了時に sales_leads を自動生成
```

チャット会話が完了しても `sales_leads` テーブルにレコードが生成されない。

### 対象ファイル

- `NetYamlForge/projects/auto-dealer-demo/Hooks/AutoDealerHooks.cs`
- `NetYamlForge/projects/auto-dealer-demo/entities/ai_conversations.yml`

### DBスキーマ（参考）

```sql
CREATE TABLE sales_leads (
    lead_id VARCHAR(50) NOT NULL PRIMARY KEY,
    customer_id VARCHAR(50) NOT NULL,
    vehicle_interest VARCHAR(100),
    budget DECIMAL(12,2),
    lead_score INTEGER NOT NULL DEFAULT 50,
    status VARCHAR(20) NOT NULL DEFAULT 'new',
    source_conversation_id VARCHAR(64),
    assigned_to_user_id VARCHAR(50),
    last_contact_at DATETIME,
    lead_source VARCHAR(30) DEFAULT 'ai_conversation',
    created_at DATETIME,
    updated_at DATETIME
);
```

### 期待する修正内容

#### 1. `AutoDealerHooks.cs` にフック実装を追加

```csharp
public class AutoCreateLeadFromConversationHook : IEntityHook
{
    public string HookName => "auto_create_lead_from_conversation";

    public Task<HookResult> BeforeAsync(HookContext ctx) => Task.FromResult(HookResult.Success());

    public async Task<HookResult> AfterAsync(HookContext ctx)
    {
        // トリガー: ai_conversations の status が 'completed' に更新されたとき
        var status = ctx.NewValues.GetValueOrDefault("status")?.ToString();
        if (status != "completed") return HookResult.Success();

        var customerId = ctx.NewValues.GetValueOrDefault("customer_id")?.ToString();
        var conversationId = ctx.NewValues.GetValueOrDefault("conversation_id")?.ToString();
        var lastIntent = ctx.NewValues.GetValueOrDefault("last_intent")?.ToString();

        if (string.IsNullOrEmpty(customerId) || customerId.StartsWith("guest_"))
            return HookResult.Success(); // ゲストは対象外

        var leadId = $"LEAD-{Guid.NewGuid():N}"[..16];
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // インテントからリードスコアを推定
        var leadScore = lastIntent switch
        {
            "test_drive_booking" => 80,
            "estimate_request"   => 70,
            "trade_inquiry"      => 60,
            _                    => 50
        };

        await ctx.Db.ExecuteAsync(@"
INSERT OR IGNORE INTO sales_leads
  (lead_id, customer_id, lead_score, status, source_conversation_id, lead_source, created_at, updated_at)
VALUES
  (@LeadId, @CustomerId, @LeadScore, 'new', @ConversationId, 'ai_conversation', @Now, @Now)",
            new { LeadId = leadId, CustomerId = customerId, LeadScore = leadScore,
                  ConversationId = conversationId, Now = now });

        return HookResult.Success();
    }
}
```

#### 2. フックを登録する

`AutoDealerHooks.cs` の既存フック登録箇所（クラス一覧）に `AutoCreateLeadFromConversationHook` を追加する。

---

## T-04: インテント分類に estimate / trade_in / service_booking ルール追加

### 問題

`HybridIntentClassifier.cs:511-599` のルールベース分類に、以下のインテントが定義されていない：

- `estimate_request`（見積もり依頼）
- `service_booking`（サービス・車検予約）

その結果、「見積もりを出してほしい」「車検の予約をしたい」が `general_inquiry` に落ちてSlot-fillingが開始されない。

なお `trade_inquiry` は一部定義済み（L623）だが `vehicle_inquiry` との重複あり。

### 対象ファイル

- `NetYamlForge/Services/AI/HybridIntentClassifier.cs`

### 現状コード（参考）

`HybridIntentClassifier.cs` の末尾付近（約 L556-640）に `IntentRule` のリストが定義されている。`test_drive_booking` と `vehicle_inquiry` は存在する。

### 期待する修正内容

以下のルールを `IntentRules` クラスの `Rules` リストに追加：

```csharp
// 見積もり依頼
new IntentRule
{
    Id = "estimate_request",
    Intent = "estimate_request",
    Keywords = new[] { "見積もり", "見積", "価格を知りたい", "いくら", "費用", "金額",
                       "予算", "価格表", "値段", "ローン", "月々" },
    NegativeKeywords = new[] { "試乗" },
    MinKeywordMatches = 1,
    Confidence = 0.85,
    Responses = new List<IntentResponse>
    {
        new() { Label = "見積もり依頼", ActionType = "slot_filling", ActionValue = "estimate" }
    }
},

// サービス・整備予約
new IntentRule
{
    Id = "service_booking",
    Intent = "service_booking",
    Keywords = new[] { "車検", "点検", "オイル交換", "タイヤ", "修理", "整備", "板金",
                       "サービス予約", "メンテナンス", "故障", "部品交換" },
    MinKeywordMatches = 1,
    Confidence = 0.88,
    Responses = new List<IntentResponse>
    {
        new() { Label = "サービス予約", ActionType = "slot_filling", ActionValue = "appointment_service" }
    }
},
```

**ルール優先順位**（重要）: `test_drive_booking` > `service_booking` > `estimate_request` > `trade_inquiry` > `vehicle_inquiry` の順になるよう、リスト内の位置を調整すること。

---

## T-05: Slot-filling セッションの DB 永続化

### 問題

`SlotFillingManager.cs:109` のセッションストレージはインメモリ（`ConcurrentDictionary`）のみ：

```csharp
private readonly ConcurrentDictionary<string, SlotSession> _sessions = new();
```

アプリ再起動や複数インスタンス展開時にセッションが消失する。また `ai_conversations` テーブルに `context_data TEXT` カラムが存在するが未使用。

### 対象ファイル

- `NetYamlForge/Services/AI/SlotFillingManager.cs`
- `NetYamlForge/Services/AI/AutoDealerChatService.cs`

### 期待する修正内容

#### 方針: `ai_conversations.context_data` を JSON ストレージとして活用

`SlotFillingManager` に `IDbConnection` を注入し、`GetSessionAsync` / `UpdateSlotAsync` で `context_data` を読み書きするよう変更する。

**context_data の JSON 形式**:
```json
{
  "slot_sessions": {
    "test_drive": {
      "scenario": "test_drive",
      "slots": {
        "vehicle_model": { "value": "プリウス PHV", "filled": true },
        "preferred_date": { "value": null, "filled": false },
        "preferred_time": { "value": null, "filled": false },
        "customer_name": { "value": null, "filled": false },
        "customer_phone": { "value": null, "filled": false }
      },
      "created_at": "2026-04-08T09:16:12Z",
      "updated_at": "2026-04-08T09:18:16Z"
    }
  }
}
```

**変更方針**:
- `GetSessionAsync`: `context_data` から読み込み、なければ新規作成してDBに保存
- `UpdateSlotAsync`: スロット値更新後、`context_data` をDBにUPDATE
- インメモリキャッシュ（`_sessions`）は維持し、DB読み込みのフォールバックとして使う

**DB更新SQL**:
```sql
UPDATE ai_conversations
SET context_data = @ContextData, updated_at = @Now
WHERE conversation_id = @ConversationId
```

---

## T-06: スタッフチャットの分析レポート形式強制

### 問題

`skills/auto-dealer/_system-prompt-staff.md` には「必ず分析レポート形式で回答」と指定されているが、`AutoDealerChatService.SendStaffMessageAsync()` では通常の LLM フローを呼び出すだけで、分析レポート形式を強制する仕組みがない。

具体的には、スタッフが「今日フォローすべき顧客は？」と尋ねた際：
- 現状: LLM が自由形式で回答（分析なし）
- 期待: 優先度別リスト + 推奨アクション付きの構造化レポート

### 対象ファイル

- `NetYamlForge/Services/AI/AutoDealerChatService.cs:570`（`SendStaffMessageAsync`）
- `NetYamlForge/Services/AI/BaseChatService.cs`（`GenerateAiResponseAsync`）

### 期待する修正内容

#### 1. `SendStaffMessageAsync` にスタッフ専用分析インテント検出を追加

```csharp
// スタッフ向けインテント分類
var staffIntent = DetectStaffAnalysisIntent(staffMessage);

if (staffIntent == "priority_leads")
{
    var report = await GenerateLeadPriorityReportAsync();
    return BuildStaffResponse(report, "priority_leads", sw);
}
if (staffIntent == "today_followup")
{
    var report = await GenerateTodayFollowupReportAsync();
    return BuildStaffResponse(report, "today_followup", sw);
}
if (staffIntent == "appointment_summary")
{
    var report = await GenerateAppointmentSummaryAsync();
    return BuildStaffResponse(report, "appointment_summary", sw);
}
```

#### 2. `DetectStaffAnalysisIntent` メソッドの実装

```csharp
private static string DetectStaffAnalysisIntent(string message) => message switch
{
    var m when m.Contains("フォロー") || m.Contains("連絡") => "today_followup",
    var m when m.Contains("リード") && (m.Contains("優先") || m.Contains("今日")) => "priority_leads",
    var m when m.Contains("予約") && (m.Contains("今日") || m.Contains("明日")) => "appointment_summary",
    _ => "general"
};
```

#### 3. 分析レポート生成メソッドの追加

**`GenerateLeadPriorityReportAsync`**: `sales_leads` を `lead_score DESC` で取得し、Markdownテーブルで返す。

**`GenerateTodayFollowupReportAsync`**: `last_contact_at` が7日以上前の顧客リストを取得し優先度付きで返す。

**`GenerateAppointmentSummaryAsync`**: 今日〜明日の `service_appointments` を取得してサマリーを返す。

---

## T-07: service_appointments INSERT のスキーマ整合性修正

### 問題

`AutoDealerChatService.CompleteTestDriveBookingAsync()` の INSERT 文が実際のDBスキーマと不一致のため、実行時エラーが発生する。

**現在のINSERT文（コード L524-537）**:
```sql
INSERT INTO service_appointments
  (appointment_id, appointment_type, preferred_date, preferred_time,
   customer_name, phone, vehicle_id, status, created_at, updated_at)
```

**実際のDBスキーマ（必須カラム）**:
```sql
customer_id VARCHAR(50) NOT NULL,   -- ← NOT NULL なのに INSERT に含まれていない
preferred_date DATETIME NOT NULL,   -- ← preferred_time カラムは存在しない
-- phone カラムも存在しない（customer_nameも存在しない）
```

`service_appointments` テーブルには `customer_name`, `phone`, `preferred_time` カラムが存在しない。

### 対象ファイル

- `NetYamlForge/Services/AI/AutoDealerChatService.cs:524`

### 期待する修正内容

INSERT文を実際のスキーマに合わせて修正：

```csharp
// 会話から customer_id を取得
var customerId = await _db.QueryFirstOrDefaultAsync<string>(
    "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
    new { Id = conversationId });

// preferred_date に日時を結合してセット
var dateTimeStr = $"{preferredDate} {preferredTime}";

await _db.ExecuteAsync(@"
INSERT INTO service_appointments
  (appointment_id, customer_id, appointment_type, preferred_date,
   customer_request, status, created_at, updated_at)
VALUES
  (@AppointmentId, @CustomerId, 'test_drive', @PreferredDate,
   @CustomerRequest, 'pending', @Now, @Now)",
    new
    {
        AppointmentId = appointmentId,
        CustomerId = customerId ?? "CUST-UNKNOWN",
        PreferredDate = dateTimeStr,
        CustomerRequest = $"お名前: {customerName} / 電話: {customerPhone} / 希望車種: {vehicleName}",
        Now = now
    });
```

---

## T-08: エスカレーション検出の精度向上

### 問題

`BaseChatService.cs:256-281` のエスカレーション検出が固定キーワード8個のマッチのみで、暗黙的な不満表現を検出できない：

```csharp
var escalationKeywords = new[] { "苦情", "クレーム", "怒り", "不満", "訴える",
                                  "返金", "責任者", "解約" };
```

「何度も言ってるのに」「いつになったら」「前回も同じ問題」などが検出されない。

### 対象ファイル

- `NetYamlForge/Services/AI/BaseChatService.cs:256`

### 期待する修正内容

#### 1. エスカレーションキーワードの拡充

```csharp
private static readonly string[] EscalationKeywords =
{
    // 直接的な不満
    "苦情", "クレーム", "怒り", "不満", "訴える", "返金", "責任者", "解約",
    // 繰り返し・放置
    "何度も", "また同じ", "前回も", "ずっと待って", "いつになったら", "まだですか",
    // 強い否定
    "最悪", "ひどい", "あり得ない", "絶対おかしい", "誠意がない",
    // 脅し
    "弁護士", "消費者センター", "SNS", "口コミ", "評判", "炎上"
};
```

#### 2. センチメントスコア計算の改善

重み付けスコアに変更し、複数の不満語が重なるほどスコアが下がる仕組みにする：

```csharp
protected float EstimateSentiment(string message)
{
    var score = 0.0f;
    var lowerMsg = message.ToLowerInvariant();

    var negativeWeights = new Dictionary<string, float>
    {
        { "最悪" , -0.6f }, { "ひどい" , -0.5f }, { "怒り" , -0.5f },
        { "不満" , -0.4f }, { "何度も" , -0.35f }, { "まだですか" , -0.3f },
        { "クレーム" , -0.5f }, { "あり得ない" , -0.5f }
    };
    var positiveWeights = new Dictionary<string, float>
    {
        { "ありがとう" , 0.4f }, { "良かった" , 0.3f }, { "助かりました" , 0.4f },
        { "素晴らしい" , 0.5f }, { "丁寧" , 0.3f }
    };

    foreach (var (word, weight) in negativeWeights)
        if (lowerMsg.Contains(word)) score += weight;
    foreach (var (word, weight) in positiveWeights)
        if (lowerMsg.Contains(word)) score += weight;

    return Math.Clamp(score, -1.0f, 1.0f);
}
```

---

## T-09: クイックリプライの全インテント対応

### 問題

`AutoDealerChatService.cs:826-831` のクイックリプライが `vehicle_inquiry` と `appointment` の2インテントしか対応していない：

```csharp
private List<string> GetCustomerQuickReplies(string intent) => intent switch
{
    "vehicle_inquiry" => new List<string> { "在庫を確認", "試乗を予約", "価格を聞く" },
    "appointment" => new List<string> { "予約を変更", "予約をキャンセル", "新しい予約" },
    _ => new List<string> { "車両を探す", "試乗を予約する", "お問い合わせ" }
};
```

### 対象ファイル

- `NetYamlForge/Services/AI/AutoDealerChatService.cs:826`

### 期待する修正内容

```csharp
private List<string> GetCustomerQuickReplies(string intent) => intent switch
{
    "vehicle_inquiry"    => ["在庫を確認", "試乗を予約", "見積もりを依頼"],
    "test_drive_booking" => ["別の車種に変更", "日時を変更", "キャンセル"],
    "estimate_request"   => ["ローンで計算", "現金購入で計算", "下取り査定も依頼"],
    "service_booking"    => ["予約を変更", "他のサービスを追加", "費用の目安を確認"],
    "trade_inquiry"      => ["査定を依頼", "新車への乗り換えを検討", "現金で売却"],
    "appointment"        => ["予約を変更", "キャンセル", "新しい予約"],
    "escalation"         => ["担当者に繋ぐ", "折り返し連絡を希望"],
    _                    => ["車両を探す", "試乗を予約", "見積もりを依頼"]
};

private List<string> GetStaffQuickReplies(string intent) => intent switch
{
    "priority_leads"     => ["全リードを見る", "未対応のみ表示", "本日の予約確認"],
    "today_followup"     => ["フォローアップ完了にする", "全顧客リスト"],
    "appointment_summary"=> ["予約詳細を見る", "スタッフ割り当て"],
    "sales_leads"        => ["新規リード", "フォローアップ必要", "成約済み"],
    "customers"          => ["VIP顧客", "未連絡顧客", "購入履歴"],
    _                    => ["顧客を検索", "リードを確認", "予約を確認"]
};
```

---

## T-10: 会話終了時の業務後処理実装

### 問題

`ConversationManager.cs` の `CloseConversationAsync` は `status = 'completed'` に更新するだけで、業務上必要な後処理が何もない。

### 対象ファイル

- `NetYamlForge/Services/AI/ConversationManager.cs`
- `NetYamlForge/Services/AI/AutoDealerChatService.cs`（または新規サービスクラス）

### 期待する修正内容

`AutoDealerChatService` に `CloseConversationAsync` をオーバーライドまたは別メソッドとして実装し、以下を実行：

```csharp
public async Task CloseConversationAsync(string conversationId)
{
    // 1. ステータス更新（既存処理）
    var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    await _db.ExecuteAsync(@"
UPDATE ai_conversations
SET status = 'completed', ended_at = @Now, updated_at = @Now
WHERE conversation_id = @ConversationId",
        new { ConversationId = conversationId, Now = now });

    // 2. Slot-fillingセッションのクリーンアップ
    if (_slotFilling != null)
        await _slotFilling.ResetAsync(conversationId);

    // 3. 会話サマリーをcontext_dataに保存（LLMによる要約）
    var messages = await GetRecentMessagesAsync(conversationId, 20);
    if (messages.Count > 2)
    {
        // インテントとスロット情報をサマリーとしてcontext_dataに保存
        var summary = new
        {
            closed_at = now,
            message_count = messages.Count,
            // 最終インテントはDBから取得済みのlast_intentを使う
        };
        // context_data の slot_sessions を削除しsummaryを追加
    }
}
```

---

## 実装上の注意事項

### ビルド確認コマンド

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~AutoDealer"
```

### 既存テストへの影響

以下のテストファイルが影響を受ける可能性がある：

- `NetYamlForge.Tests/Services/AI/AutoDealerChatHistoryFixTests.cs`
- `NetYamlForge.Tests/Services/AI/SlotFillingTests.cs`（存在すれば）

`ISlotFillingManager` インターフェースに `GetActiveScenarioAsync` メソッドが既に追加済み（2026-04-08）。モックを使ったテストでは `GetActiveScenarioAsync` のセットアップが必要。

### SQLの安全性

- テーブル名・カラム名は絶対に文字列補間しない（`SqlSafetyGuard` が検知する）
- 値はすべてDapperのパラメータで渡す
- `DCS001` コンパイラエラーに注意

### YAML スキーマ変更後の注意

`ai_conversations.yml` のカラムを追加した場合、`EntityDbSchemaConsistencyValidator` がDB未作成カラムを検出して起動エラーになる。必ずDBマイグレーション（`ALTER TABLE`）をYAML変更とセットで行うこと。

---

## 完了チェックリスト

- [ ] T-01: estimate/appointment_service/trade_in Slot-fillingフロー実装
- [ ] T-02: feedback_rating/comment カラム追加（YAML + DB）
- [ ] T-03: チャットからのリード自動生成フック実装
- [ ] T-04: インテント分類ルール追加（estimate_request, service_booking）
- [ ] T-05: Slot-fillingセッションのDB永続化
- [ ] T-06: スタッフチャット分析レポート形式強制
- [ ] T-07: service_appointments INSERT文のスキーマ修正
- [ ] T-08: エスカレーション検出の拡充
- [ ] T-09: クイックリプライの全インテント対応
- [ ] T-10: 会話終了時の業務後処理実装
