# 修正計画：AI チャット応答と履歴保存の問題

## 問題の概要

1. **AI からの応答がない**：Slot-filling フローが正しく機能していない
2. **チャット履歴が保存されない**：ページリロード後に履歴が回復しない

## 根本原因分析

### 問題 1：Slot-filling フローの失敗

`ProcessTestDriveSlotFillingAsync` の呼び出し順序に問題がある：

```csharp
// 現在のコード (AutoDealerChatService.cs, 行 254-282)
await ExtractSlotValuesFromMessageAsync(conversationId, customerMessage, scenario);
var session = await _slotFilling.GetSessionAsync(conversationId, scenario, _projectName);
```

**問題点**：
1. `ExtractSlotValuesFromMessageAsync` は既存のセッションにスロットを更新しようとする
2. しかし、この時点ではまだセッションが存在しない
3. `GetSessionAsync` はその後で新しいセッションを作成する
4. 結果として、**最初の実行ではスロットが更新されない**

### 問題 2：チャット履歴の回復失敗

`startDealerSession` (dealer-chat-widget.js, 行 734-806) の問題：

1. `sessionStorage` に `dealer_conv_X` がない場合、すぐに新しいセッションを作成する
2. `restoreFromServer()` はその後で呼ばれるが、**新しいセッション ID では古い履歴は取得できない**
3. `user-history` API はあるが、`customer_id` が正しく設定されていない可能性がある

## 修正内容

### 修正 1：`AutoDealerChatService.cs`

**場所**: `/home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/AutoDealerChatService.cs`

#### 1.1 `ProcessTestDriveSlotFillingAsync` の順序修正

```csharp
private async Task<(string ResponseText, string? NavUrl, string? NavLabel)> ProcessTestDriveSlotFillingAsync(
    string conversationId, string customerMessage)
{
    try
    {
        var scenario = SlotFillingManager.DetectScenarioFromMessage(customerMessage, "test_drive_booking");
        if (scenario != "test_drive" || _slotFilling == null)
        {
            return ("試乗予約をご希望ですね。ご希望の車種・日時をお知らせください。", null, null);
        }

        // ✅ 修正：最初にセッションを取得（または作成）
        var session = await _slotFilling.GetSessionAsync(conversationId, scenario, _projectName);
        
        // ✅ 次に、メッセージからスロット値を抽出して更新
        await ExtractSlotValuesFromMessageAsync(conversationId, customerMessage, scenario);
        
        // ✅ 更新後のセッションを再取得
        session = await _slotFilling.GetSessionAsync(conversationId, scenario, _projectName);

        // ✅ デバッグログ：現在のslot状態を記録
        var collectedSlots = session.GetCollectedValues();
        _logger.LogInformation("試乗予約 Slot-filling: Conv={ConvId}, 収集済みSlots={Slots}",
            conversationId, string.Join(", ", collectedSlots.Select(kv => $"{kv.Key}={kv.Value}")));

        if (session.IsComplete)
        {
            var slots = session.GetCollectedValues();
            return await CompleteTestDriveBookingAsync(conversationId, slots);
        }

        var nextSlot = await _slotFilling.GetNextRequiredSlotAsync(conversationId, scenario, _projectName);
        if (nextSlot != null)
        {
            _logger.LogInformation("試乗予約: 次の質問スロット={Slot}", nextSlot.SlotName);
            return ($"{nextSlot.Prompt}", null, null);
        }

        return ("試乗予約のご連絡ありがとうございます。車種・ご希望日時・お名前・ご連絡先をお知らせください。", null, null);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "試乗予約Slot-fillingエラー");
        return ("試乗予約のご連絡ありがとうございます。車種・ご希望日時・お名前・ご連絡先をお知らせください。", null, null);
    }
}
```

#### 1.2 `ExtractSlotValuesFromMessageAsync` の改善

```csharp
private async Task ExtractSlotValuesFromMessageAsync(string conversationId, string message, string scenario)
{
    if (_slotFilling == null) return;

    var lowerMessage = message.ToLowerInvariant();

    // 日付の抽出
    var datePatterns = new Dictionary<string, string>
    {
        { "明日", "tomorrow" },
        { "明後日", "day_after_tomorrow" },
        { "今日", "today" },
        { "来週", "next_week" },
        { "今週", "this_week" }
    };

    foreach (var (pattern, value) in datePatterns)
    {
        if (lowerMessage.Contains(pattern))
        {
            await _slotFilling.UpdateSlotAsync(conversationId, "preferred_date", value, _projectName);
            break;
        }
    }

    var dateMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{4})[-/](\d{1,2})[-/](\d{1,2})");
    if (dateMatch.Success)
    {
        await _slotFilling.UpdateSlotAsync(conversationId, "preferred_date", dateMatch.Value, _projectName);
    }

    // 時間の抽出
    var timePatterns = new Dictionary<string, string>
    {
        { "午前", "morning" },
        { "午後", "afternoon" },
        { "朝", "morning" },
        { "昼", "afternoon" },
        { "夕方", "evening" },
        { "夜", "evening" },
        { "10時", "10:00" },
        { "14時", "14:00" },
        { "2時", "14:00" },
        { "15時", "15:00" },
        { "3時", "15:00" }
    };

    foreach (var (pattern, value) in timePatterns)
    {
        if (lowerMessage.Contains(pattern))
        {
            await _slotFilling.UpdateSlotAsync(conversationId, "preferred_time", value, _projectName);
            break;
        }
    }

    // 電話番号の抽出
    var phoneMatch = System.Text.RegularExpressions.Regex.Match(message, @"(\d{2,4}-\d{1,4}-\d{4})");
    if (phoneMatch.Success)
    {
        await _slotFilling.UpdateSlotAsync(conversationId, "customer_phone", phoneMatch.Value, _projectName);
    }

    // 車種の抽出
    var knownVehicles = new Dictionary<string, string>
    {
        { "プリウス", "プリウス PHV" },
        { "ランドクルーザー", "ランドクルーザー 300" },
        { "アルファード", "アルファード" },
        { "camry", "カムリ" },
        { "カローラ", "カローラ" },
        { "ヤリス", "ヤリス" },
        { "rav4", "RAV4" },
        { "ハイラックス", "ハイラックス" },
        { "クラウン", "クラウン" },
        { "スープラ", "スープラ" },
        { "gtr", "GT-R" },
        { "フィット", "フィット" },
        { "アクセラ", "アクセラ" },
        { "cx", "CXシリーズ" },
        { "インプレッサ", "インプレッサ" },
        { "レヴォーグ", "レヴォーグ" },
        // 制造商名称
        { "マツダ", "マツダ車" },
        { "トヨタ", "トヨタ車" },
        { "ホンダ", "ホンダ車" },
        { "日産", "日産車" },
        { "bmw", "BMW車" },
        { "メルセデス", "メルセデス・ベンツ" },
        { "ベンツ", "メルセデス・ベンツ" }
    };

    foreach (var (keyword, vehicleName) in knownVehicles)
    {
        if (lowerMessage.Contains(keyword))
        {
            await _slotFilling.UpdateSlotAsync(conversationId, "vehicle_model", vehicleName, _projectName);
            break;
        }
    }

    // 名前の抽出
    var namePatterns = new System.Text.RegularExpressions.Regex(@"(.+?)(?:です|と申します|でございます)");
    var nameMatch = namePatterns.Match(message);
    if (nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value.Trim()))
    {
        var candidateName = nameMatch.Groups[1].Value.Trim();
        if (candidateName.Length >= 2 && candidateName.Length <= 20)
        {
            await _slotFilling.UpdateSlotAsync(conversationId, "customer_name", candidateName, _projectName);
        }
    }
}
```

### 修正 2：`dealer-chat-widget.js`

**場所**: `/home/ubuntu/ws/NetYamlForge/NetYamlForge/wwwroot/js/dealer-chat-widget.js`

#### 2.1 `startDealerSession` の改善

```javascript
async function startDealerSession() {
  // 1️⃣ sessionStorage から復元
  const storedConvId = sessionStorage.getItem('dealer_conv_' + currentMode);
  if (storedConvId) {
    dealerConversationId = storedConvId;
    console.log('DealerChat: セッションを sessionStorage から復元しました', dealerConversationId);
    return;
  }

  // 2️⃣ ✅ 修复: 从服务器获取用户最近的会话（刷新后恢复）
  try {
    const userId = getUserId();
    if (userId) {
      const historyUrl = CONFIG.chatApiBase + '/user-history?userId=' + encodeURIComponent(userId) + '&limit=1';
      console.log('DealerChat: 从服务器获取用户历史会话', historyUrl);

      const resp = await fetch(historyUrl);
      if (resp.ok) {
        const data = await resp.json();
        if (data.conversationId) {
          dealerConversationId = data.conversationId;
          sessionStorage.setItem('dealer_conv_' + currentMode, dealerConversationId);
          console.log('DealerChat: 从服务器恢复会话', dealerConversationId);
          return; // ✅ 成功恢复，直接返回
        }
      }
    }
  } catch (e) {
    console.warn('DealerChat: 从服务器恢复会话失败，将创建新会话', e);
  }

  // 3️⃣ 创建新会话
  try {
    const sessionUrl = CONFIG.chatApiBase + '/' + currentTheme.apiPath;
    console.log('DealerChat: セッション開始 URL:', sessionUrl);

    const resp = await fetch(sessionUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ channel: currentMode })
    });

    if (resp.ok) {
      const data = await resp.json();
      dealerConversationId = data.conversationId;
      sessionStorage.setItem('dealer_conv_' + currentMode, dealerConversationId);
      console.log('DealerChat: セッションを開始しました', dealerConversationId);
    } else {
      const errText = await resp.text().catch(() => '');
      console.error('DealerChat: セッション開始失敗', resp.status, errText);
      if (resp.status === 401) {
        addMessage('ログインが必要です。ログインしてください。', 'system');
      } else if (resp.status === 500) {
        addMessage('サーバーエラーが発生しました。しばらくお待ちください。', 'system');
      } else {
        addMessage('セッションの開始に失敗しました：' + resp.status, 'system');
      }
    }
  } catch (e) {
    console.error('DealerChat: セッション開始エラー', e);
    let errorMsg = 'セッション開始エラー：' + e.message;
    if (e.message.includes('Failed to fetch')) {
      errorMsg = 'サーバーに接続できません。サーバーが起動しているか確認してください。';
    }
    addMessage(errorMsg, 'system');
  }
}
```

#### 2.2 `restoreFromServer` の改善

```javascript
async function restoreFromServer() {
  // ✅ 修复: 优先使用业务 API (ai_messages 表) 而不是 AI CLI 历史 (chat.db)
  // 如果有会话 ID, 从业务数据库获取消息
  if (dealerConversationId) {
    try {
      const resp = await fetch(CONFIG.chatApiBase + '/session/' + dealerConversationId + '/messages');
      if (resp.ok) {
        const messages = await resp.json();
        if (Array.isArray(messages) && messages.length > 0) {
          const container = document.getElementById('dc-messages-container');
          container.innerHTML = '';
          chatHistory = [];
          messages.forEach(function(m) {
            // sender: customer | ai | agent → user | assistant
            const type = (m.sender === 'customer') ? 'user' : 'assistant';
            const ts = m.timestamp || '';
            chatHistory.push({ content: m.content, type: type, timestamp: ts });
            addMessage(m.content, type, true, ts);
          });
          saveHistory();
          return; // ✅ 成功获取, 直接返回
        }
      }
    } catch (e) {
      console.warn('从业务 API 恢复失败, 尝试 AI CLI 历史 API:', e);
    }
  }

  // フォールバック: AI CLI 历史 API (chat.db)
  const chatContext = currentMode === 'customer' ? 'dealer-customer' : 'dealer-staff';
  try {
    const resp = await fetch(CONFIG.apiBaseUrl + '/history?limit=50&context=' + chatContext);
    if (!resp.ok) {
      // サーバーに履歴がない場合は sessionStorage から復元（フォールバック）
      restoreFromStorage();
      return;
    }
    const messages = await resp.json();
    if (!Array.isArray(messages) || messages.length === 0) {
      // サーバーに履歴がない場合は sessionStorage もクリア（整合性保持）
      const key = STORAGE_KEY_PREFIX + currentMode;
      sessionStorage.removeItem(key);
      restoreFromStorage();
      return;
    }
    // サーバーデータでローカルキャッシュを上書き
    chatHistory = messages.map(function(m) {
      return { content: m.content, type: m.type, timestamp: m.displayTime || m.createdAt || '' };
    });
    const container = document.getElementById('dc-messages-container');
    container.innerHTML = '';
    chatHistory.forEach(function(msg) {
      addMessage(msg.content, msg.type, true, msg.timestamp);
    });
    // sessionStorage もサーバーデータで更新
    saveHistory();
  } catch (e) {
    // サーバー取得失敗時は sessionStorage から復元（フォールバック）
    restoreFromStorage();
  }
}
```

## 测试计划

### 测试 1：Slot-filling フロー

1. "試乗を予約したい" と送信
   → 期待：AI が "どの車種の試乗をご希望ですか？" と返す
2. "マツダの車" と送信
   → 期待：AI が "ご希望の日付を教えてください" と返す
3. "明日の午前" と送信
   → 期待：AI が "お名前を教えてください" と返す
4. "山田です" と送信
   → 期待：AI が "ご連絡先電話番号を教えてください" と返す
5. "090-1234-5678" と送信
   → 期待：AI が予約完了メッセージを表示

### 测试 2：チャット履歴の回復

1. 上記の slot-filling テストを完了
2. **ブラウザをリロード**
3. チャットウィジェットを再度開く
4. 期待：以前の会話が表示される

## 注意点

1. `customer_id` が `ai_conversations` テーブルに正しく保存されているか確認
2. `user-history` API が正しいレスポンスを返すか確認
3. デバッグログを有効にして、slot-filling の進行状況を監視
