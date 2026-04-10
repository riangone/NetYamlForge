# 自動車販売 AI チャット - 実装ガイド

> **版本**: 1.0  
> **最後更新**: 2026 年 4 月 9 日

---

## 📋 目次

1. [API レスポンス仕様](#apiレスポンス仕様)
2. [エラーコード・メッセージ](#エラーコードメッセージ)
3. [実装上の重要な注意点](#実装上の重要な注意点)
4. [パフォーマンス最適化](#パフォーマンス最適化)
5. [セキュリティ考慮事項](#セキュリティ考慮事項)
6. [テスト戦略](#テスト戦略)

---

## API レスポンス仕様

### 1. ChatSessionResult (セッション開始レスポンス)

```json
{
  "conversationId": "CONV-20260409-143000-a1b2c3d4e5f6g7h8",
  "welcomeMessage": "こんにちは！AI 自動車販売です。🚗\n試乗・ご購入・サービスのご相談は何でもどうぞ！"
}
```

**フィールド説明**:
| フィールド | 型 | 説明 | 例 |
|---|---|---|---|
| conversationId | string | 一意の会話セッション ID | CONV-20260409-143000-xxxx |
| welcomeMessage | string | AI のウェルカムメッセージ | "こんにちは!..." |

---

### 2. ChatMessageResult (メッセージ送信レスポンス)

```json
{
  "responseText": "試乗予約をお承りいたします。\nご希望の日時をお選びください。",
  "intent": "test_drive_booking",
  "suggestHandover": false,
  "quickReplies": [
    "確認する",
    "キャンセル",
    "別の車を見る",
    "その他のご質問"
  ],
  "processingTimeMs": 1450,
  "dataRows": null,
  "navigationUrl": "/auto-dealer-demo/DynamicEntity/Index?entity=vehicles",
  "navigationLabel": "車一覧へ",
  "aiProvider": "claude",
  "messageTimestamp": "2026-04-09T14:30:15Z",
  "components": [
    {
      "type": "quick_replies",
      "data": {
        "options": [
          "確認する",
          "キャンセル",
          "別の車を見る"
        ]
      }
    }
  ]
}
```

**フィールド説明**:

| フィールド | 型 | 説明 | 備考 |
|---|---|---|---|
| responseText | string | AI からのテキスト応答 | 必須 |
| intent | string | 推定インテント | test_drive_booking, vehicle_inquiry, etc. |
| suggestHandover | boolean | エスカレーション推奨フラグ | true = オペレーター接続推奨 |
| quickReplies | string[] | クイックリプライ一覧 | フロントエンド用 UI ボタン |
| processingTimeMs | int | 処理時間（ミリ秒） | デバッグ用 |
| dataRows | object[] | テーブルデータ | 空の場合は null |
| navigationUrl | string | 詳細ページへのナビゲーション URL | オプション |
| navigationLabel | string | ナビゲーション表示ラベル | navigationUrl 用 |
| aiProvider | string | 使用した AI プロバイダー名 | claude, qwen, gemini, ollama |
| messageTimestamp | string | AI 応答のタイムスタンプ | ISO 8601 形式 |
| components | object[] | UI コンポーネント定義 | オプション |

---

### 3. DataRows (テーブルデータ)

**車両検索結果の例**:
```json
{
  "dataRows": [
    {
      "vehicle_id": "VEH-001",
      "brand": "トヨタ",
      "model": "カムリ",
      "year": "2025",
      "price": "2,800,000",
      "status": "在庫あり",
      "_nav_url": "/auto-dealer-demo/DynamicEntity/Edit/VEH-001"
    },
    {
      "vehicle_id": "VEH-002",
      "brand": "ホンダ",
      "model": "アコード",
      "year": "2025",
      "price": "2,650,000",
      "status": "在庫あり",
      "_nav_url": "/auto-dealer-demo/DynamicEntity/Edit/VEH-002"
    }
  ]
}
```

**注意**: `_nav_url` は内部フィールド。レスポンスに含めない場合が多い。

---

### 4. Components (UI コンポーネント定義)

#### Quick Replies

```json
{
  "type": "quick_replies",
  "data": {
    "options": [
      "確認する",
      "キャンセル",
      "別の車を見る"
    ]
  }
}
```

#### Table

```json
{
  "type": "table",
  "data": {
    "columns": [
      { "label": "車種", "key": "model" },
      { "label": "価格", "key": "price" },
      { "label": "ステータス", "key": "status" }
    ],
    "rows": [
      { "model": "カムリ", "price": "2,800,000", "status": "在庫あり" }
    ]
  }
}
```

#### Navigation

```json
{
  "type": "navigation",
  "data": {
    "url": "/auto-dealer-demo/DynamicEntity/Index?entity=vehicles",
    "label": "車一覧へ",
    "target": "_self"
  }
}
```

---

### 5. エスカレーション時のレスポンス

```json
{
  "responseText": "申し訳ございません。お客様の内容について、詳しいスタッフが確認させていただきます。",
  "intent": "escalation_critical",
  "suggestHandover": true,
  "quickReplies": ["了解しました", "キャンセル"],
  "processingTimeMs": 250,
  "dataRows": null,
  "messageTimestamp": "2026-04-09T14:35:00Z"
}
```

---

## エラーコード・メッセージ

### HTTP ステータスコード

| コード | 説明 | 例 |
|---|---|---|
| 200 | OK - 成功 | `{"responseText": "..."}` |
| 400 | Bad Request - 入力エラー | メッセージが空など |
| 401 | Unauthorized - 認証エラー | 従業員 API へ非認証でアクセス |
| 404 | Not Found - リソース不在 | conversation_id が見つからない |
| 500 | Internal Server Error - サーバーエラー | DB エラー、予期しない例外 |
| 504 | Gateway Timeout - タイムアウト | AI 応答が 3600 秒を超過 |

---

### エラーレスポンスの形式

```json
{
  "error": "メッセージが空です。",
  "detail": "オプション: 詳細情報",
  "code": "INVALID_MESSAGE_FORMAT"
}
```

---

### よくあるエラーケース

#### Case 1: メッセージ空

```bash
curl -X POST http://localhost:5000/auto-dealer-demo/api/ai/chat/session/CONV-xxx/message \
  -H "Content-Type: application/json" \
  -d '{"message": ""}'
```

**レスポンス**:
```json
{
  "error": "メッセージが空です。"
}
```

**HTTP ステータス**: 400 Bad Request

---

#### Case 2: セッション ID が見つからない

**レスポンス**:
```json
{
  "error": "指定されたセッションが見つかりません。"
}
```

**HTTP ステータス**: 404 Not Found

---

#### Case 3: AI プロバイダーがすべて失敗

**レスポンス**:
```json
{
  "error": "すべての AI プロバイダーが失敗しました。",
  "detail": "Claude API: connection timeout, Qwen API: rate limit exceeded, Gemini API: unauthorized",
  "code": "ALL_PROVIDERS_FAILED"
}
```

**HTTP ステータス**: 500 Internal Server Error

---

#### Case 4: タイムアウト（3600 秒超過）

**レスポンス**:
```json
{
  "error": "応答がタイムアウトしました（3600秒）。もう一度お試しください。"
}
```

**HTTP ステータス**: 504 Gateway Timeout

---

## 実装上の重要な注意点

### ⚠️ 1. メッセージ ID の生成

```csharp
// ✅ 正しい: ランダム 32 文字
var messageId = $"MSG-{Guid.NewGuid():N}"[..32];

// ❌ 間違い: シーケンシャルまたは予測可能
var messageId = $"MSG-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
```

**理由**: セキュリティ（予測攻撃防止）・並行性（タイムスタンプ衝突）

---

### ⚠️ 2. タイムスタンプの一貫性

```csharp
// ✅ 正しい: UTC で統一
var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

// ❌ 間違い: ローカルタイムを使用（タイムゾーン混在）
var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
```

**理由**: データベースでタイムゾーン解釈のズレを防ぐ

---

### ⚠️ 3. Slot-filling セッションのクリア

```csharp
// ✅ 正しい: エスカレーション時にリセット
if (needsHandover)
{
    if (_slotFilling != null)
        await _slotFilling.ResetAsync(conversationId, _projectName);
}

// ❌ 間違い: Slot 状態を残したままエスカレーション
// → 後で Slot-filling が再開される可能性
```

**理由**: エスカレーション後のオペレーター対応がスムーズに

---

### ⚠️ 4. DB トランザクション

```csharp
// ✅ 正しい: トランザクション内で複数操作
using var transaction = _db.BeginTransaction();
try
{
    await SaveMessageAsync(...);
    await _db.ExecuteAsync("UPDATE ai_conversations SET ...", ...);
    transaction.Commit();
}
catch
{
    transaction.Rollback();
}

// ❌ 間違い: 個別のコネクション呼び出し
// → メッセージは保存されたが conversations は更新されない
```

**理由**: データベース整合性保証

---

### ⚠️ 5. 非同期タイムアウト

```csharp
// ✅ 正しい: CancellationToken をタイムアウト付きで作成
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3600));
var task = _chat.SendMessageAsync(conversationId, req.Message);
var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));

if (completedTask != task)
    return StatusCode(504, new { error = "タイムアウト" });

// ❌ 間違い: タイムアウト管理なし
// var response = await _chat.SendMessageAsync(...);  // 無限待機の可能性
```

**理由**: リソースリークとハング防止

---

### ⚠️ 6. NULL チェック

```csharp
// ✅ 正しい: オプション機能の NULL チェック
if (_slotFilling != null && _intentClassifier != null)
{
    // Slot-filling 処理
}

// ❌ 間違い: NULL チェックなし
// var scenario = await _slotFilling.GetActiveScenarioAsync(...);
// → NullReferenceException
```

**理由**: オプション機能の DI 初期化失敗への対応

---

### ⚠️ 7. エスカレーション判定の順序

```csharp
// ✅ 正しい: エスカレーションを**最初に**判定
if (needsHandover || sentimentScore < -0.5)
    return await HandleEscalationAsync(...);

// 以下の処理は実行されない

// ❌ 間違い: Slot-filling 後にエスカレーション
await ExtractSlotValuesFromMessageAsync(...);
if (needsHandover)  // この時点では遅い
    return await HandleEscalationAsync(...);
// → スロット値が無駄に抽出される
```

**理由**: エスカレーション優先度・効率

---

### ⚠️ 8. AI 応答の validate

```csharp
// ✅ 正しい: 空応答チェック
if (string.IsNullOrWhiteSpace(response))
{
    _logger.LogWarning("AI 応答が空です");
    return (GetTemplateResponse("error"), "error", null, null, null);
}

// ❌ 間違い: null チェックのみ
if (response == null)
    throw new InvalidOperationException("AI response is null");
```

**理由**: 空白文字のみの応答もエラーとして扱う

---

## パフォーマンス最適化

### 1. 会話履歴の制限

```csharp
// メッセージ取得時に最新 10 件のみ取得
var history = await GetRecentMessagesAsync(conversationId, limit: 10);

// ❌ 履歴が長い場合は AI プロンプトが巨大化
```

**推奨設定**:
- `limit = 10`: ほとんどのユースケース
- `limit = 20`: 複雑な対話が必要な場合
- `limit = 50`: 制限なし相当（ただし遅くなる）

---

### 2. DB クエリの最適化

```csharp
// ✅ 必要なカラムのみ選択
var customerId = await _db.QueryFirstOrDefaultAsync<string>(
    "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
    new { Id = conversationId });

// ❌ 全カラム取得（不要）
var conv = await _db.QueryFirstOrDefaultAsync<AIConversation>(
    "SELECT * FROM ai_conversations WHERE conversation_id = @Id",
    new { Id = conversationId });
```

---

### 3. 並行クエリ

```csharp
// ✅ 独立した複数クエリは並行実行
var customerId = _db.QueryFirstOrDefaultAsync<string>(
    "SELECT customer_id FROM ai_conversations WHERE conversation_id = @Id",
    new { Id = conversationId });

var lastIntent = _db.QueryFirstOrDefaultAsync<string>(
    "SELECT last_intent FROM ai_conversations WHERE conversation_id = @Id",
    new { Id = conversationId });

await Task.WhenAll(customerId, lastIntent);
```

---

### 4. キャッシング

```csharp
// Redis キャッシュ（オプション）
var cacheKey = $"session:{conversationId}";
var cachedSession = await _cache.GetAsync(cacheKey);

if (cachedSession == null)
{
    // DB から取得
    cachedSession = await _db.QueryFirstOrDefaultAsync(...);
    await _cache.SetAsync(cacheKey, cachedSession, TimeSpan.FromMinutes(5));
}
```

---

## セキュリティ考慮事項

### 1. SQL インジェクション防止

```csharp
// ✅ 正しい: パラメータ化クエリ
await _db.ExecuteAsync(
    "SELECT * FROM customers WHERE id = @Id",
    new { Id = customerId });

// ❌ 間違い: 文字列挿入
var query = $"SELECT * FROM customers WHERE id = '{customerId}'";
await _db.ExecuteAsync(query);  // SQL インジェクションリスク
```

---

### 2. 認証・認可

```csharp
// ✅ 正しい: Authorize が必要な API にはデコレータ
[Authorize]
[HttpPost("staff/{conversationId}/message")]
public async Task<IActionResult> SendStaffMessage(string conversationId, ...)
{
    var userId = User.FindFirst(ClaimTypes.Name)?.Value;
    if (string.IsNullOrEmpty(userId))
        return Unauthorized();
}

// ❌ 間違い: AllowAnonymous で従業員 API 公開
[AllowAnonymous]
[HttpPost("staff/{conversationId}/message")]
public async Task<IActionResult> SendStaffMessage(...)
```

---

### 3. レート制限

```csharp
// ✅ 推奨: IP/ユーザー別のレート制限
[RateLimit(5)]  // 1 分間に 5 リクエスト
[HttpPost("session/{conversationId}/message")]
public async Task<IActionResult> SendMessage(...)
```

---

### 4. ログ出力時の機密情報

```csharp
// ✅ 正しい: センシティブ情報をマスク
_logger.LogInformation("メッセージ受信: Conv={ConvId}, Length={Len}",
    conversationId, customerMessage.Length);

// ❌ 間違い: 全メッセージをログ出力
_logger.LogInformation("メッセージ受信: {Message}", customerMessage);
// → 個人情報・カード番号等が露出
```

---

## テスト戦略

### 1. ユニットテスト

```csharp
[Fact]
public async Task SendMessage_WithValidInput_ReturnsResponse()
{
    // Arrange
    var service = new AutoDealerChatService(...);
    var conversationId = "CONV-test-001";
    var message = "試乗したいです";
    
    // Act
    var result = await service.SendMessageAsync(conversationId, message);
    
    // Assert
    Assert.NotNull(result);
    Assert.NotEmpty(result.ResponseText);
    Assert.True(result.ProcessingTimeMs > 0);
}

[Fact]
public async Task SendMessage_WithEscalationKeyword_TriggersEscalation()
{
    // Arrange
    var service = new AutoDealerChatService(...);
    var message = "詐欺だ！警察に通報する！";
    
    // Act
    var result = await service.SendMessageAsync("CONV-xxx", message);
    
    // Assert
    Assert.True(result.SuggestHandover);
}
```

---

### 2. 統合テスト

```csharp
[Fact]
public async Task SendMessage_E2E_SavesMessagesToDB()
{
    // Arrange
    using var testDb = new TestDatabase();
    var service = new AutoDealerChatService(testDb.Connection, ...);
    
    // Act
    var conversationId = "CONV-e2e-001";
    var result = await service.SendMessageAsync(conversationId, "試乗したい");
    
    // Assert
    var messages = await testDb.QueryAsync<AIMessage>(
        "SELECT * FROM ai_messages WHERE conversation_id = @ConvId",
        new { ConvId = conversationId });
    
    Assert.Equal(2, messages.Count);  // customer + ai
}
```

---

### 3. パフォーマンステスト

```csharp
[Fact]
public async Task SendMessage_UnderLoad_CompletesWithin5Seconds()
{
    // Arrange
    var service = new AutoDealerChatService(...);
    var sw = Stopwatch.StartNew();
    
    // Act
    var result = await service.SendMessageAsync("CONV-perf-001", "test message");
    
    // Assert
    sw.Stop();
    Assert.True(result.ProcessingTimeMs < 5000);  // 5秒以内
}
```

---

### 4. タイムアウトテスト

```csharp
[Fact]
public async Task SendMessage_WithSlowProvider_ReturnsTimeoutError()
{
    // Arrange
    var slowProvider = new SlowAIProvider(delaySeconds: 10);
    var service = new AutoDealerChatService(
        ...,
        chatResponseTimeoutSeconds: 2  // 2 秒タイムアウト
    );
    
    // Act & Assert
    var ex = await Assert.ThrowsAsync<OperationCanceledException>(
        () => service.SendMessageAsync("CONV-xxx", "message"));
}
```

---

### 5. テストカバレッジ目標

| モジュール | 目標カバレッジ | 優先度 |
|---|---|---|
| `SendMessageAsync()` | >= 90% | **高** |
| `DetectEscalation()` | >= 85% | 高 |
| `Slot-filling` | >= 80% | 高 |
| `AI 応答生成` | >= 75% | 中 |
| エラーハンドリング | >= 85% | 高 |

---

### 6. Mock の使用例

```csharp
[Fact]
public async Task SendMessage_WithMockAIProvider_ReturnsMockedResponse()
{
    // Arrange
    var mockProvider = new Mock<ILlmProvider>();
    mockProvider.Setup(p => p.CallAsync(It.IsAny<string>()))
        .ReturnsAsync("試乗のご予約を承ります。");
    
    var service = new AutoDealerChatService(
        db: _testDb.Connection,
        llmProvider: mockProvider.Object,
        ...
    );
    
    // Act
    var result = await service.SendMessageAsync("CONV-mock-001", "試乗したい");
    
    // Assert
    Assert.Contains("予約を承ります", result.ResponseText);
    mockProvider.Verify(p => p.CallAsync(It.IsAny<string>()), Times.Once);
}
```

---

## デプロイメントチェックリスト

```
API エンドポイント
  [ ] POST /api/ai/chat/session → 正常動作
  [ ] POST /api/ai/chat/session/{id}/message → 正常動作
  [ ] GET  /api/ai/chat/session/{id}/messages → 正常動作

データベース
  [ ] ai_conversations テーブル作成
  [ ] ai_messages テーブル作成
  [ ] ai_handovers テーブル作成
  [ ] インデックス設定（conversation_id, created_at）
  [ ] バックアップ計画策定

環境設定
  [ ] appsettings.Production.json 編集
  [ ] AI プロバイダー API キー設定
  [ ] タイムアウト値確認（3600 秒）

セキュリティ
  [ ] 認証・認可テスト
  [ ] SQL インジェクションテスト
  [ ] レート制限設定
  [ ] HTTPS 有効化

モニタリング
  [ ] ログ設定（LogInformation/Warning/Error）
  [ ] パフォーマンス監視
  [ ] エラー率監視
  [ ] AI プロバイダー健全性チェック

テスト
  [ ] ユニットテスト実行
  [ ] 統合テスト実行
  [ ] E2E テスト実行
  [ ] ストレステスト実行

ドキュメント
  [ ] API ドキュメント（Swagger）
  [ ] 運用ガイド作成
  [ ] トラブルシューティングガイド作成
  [ ] ユーザーマニュアル作成
```

---

*このドキュメントはプロダクション環境への実装ガイドです。デプロイ前にすべてのチェックリスト項目を確認してください。*