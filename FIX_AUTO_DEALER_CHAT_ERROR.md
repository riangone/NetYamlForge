# 自動車販売 AI チャット - エラー修正レポート

## 問題概要

**現象**: 自動車販売 AI 業務アシスタントで「今日应该联系的客户有哪些？」と質問すると、
```
リクエスト失敗：Failed to fetch
```
というエラーが表示される。

## 原因分析

### 1. エラーの発生源

エラーは `wwwroot/js/dealer-chat-widget.js` の `sendMessage()` 関数で発生しています。

**該当箇所**（修正前）:
```javascript
} catch (error) {
  console.error('Send message error:', error);
  addMessage(`リクエスト失敗：${error.message}`, 'system');
  updateStatus('error');
}
```

### 2. "Failed to fetch" エラーの一般的な原因

JavaScript の `fetch()` API で "Failed to fetch" エラーが発生する主な原因：

1. **サーバーが起動していない** - 最も一般的な原因
2. **ネットワーク接続の問題** - クライアントがサーバーに到達できない
3. **CORS（Cross-Origin Resource Sharing）設定の問題** - ブラウザーがリクエストをブロック
4. **SSL/TLS 証明書の問題** - HTTPS 接続の失敗
5. **サーバーが応答しない** - タイムアウト

### 3. 自動車販売 AI システムの特定の問題

このシステムでは、以下の追加要因が考えられます：

#### a) ユーザーログイン状態のチェック不足

```javascript
// 修正前：ログインチェックが sendMessage() にない
if (!dealerConversationId) {
  await startDealerSession();
}
```

`isUserLoggedIn()` チェックが `init()` 関数にしかないため、ログインしていないユーザーがチャットパネルを使用しようとするとエラーになります。

#### b) セッション開始エラーの不適切な処理

```javascript
// 修正前：エラー詳細をログにのみ出力
} catch (e) {
  console.error('DealerChat: セッション開始エラー', e);
}
```

エラーがコンソールにのみ出力され、ユーザーに明確なメッセージが表示されませんでした。

#### c) API エンドポイントのパス設定

```javascript
CONFIG.apiBaseUrl = (opts.apiBase || '') + '/' + currentProject + '/api/ai';
CONFIG.chatApiBase = CONFIG.apiBaseUrl + '/chat';
```

設定が正しくない場合、間違った URL にリクエストが送信されます。

## 修正内容

### 修正 1: `startDealerSession()` 関数のエラー処理改善

**ファイル**: `wwwroot/js/dealer-chat-widget.js` (行 713-759)

**修正後**:
```javascript
async function startDealerSession() {
  // セッションキーで sessionStorage から復元
  const storedConvId = sessionStorage.getItem('dealer_conv_' + currentMode);
  if (storedConvId) {
    dealerConversationId = storedConvId;
    console.log('DealerChat: セッションを復元しました', dealerConversationId);
    return;
  }

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
      // エラーメッセージを表示
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

**改善点**:
- デバッグログの追加
- HTTP ステータスコードに基づく適切なエラーメッセージ
- "Failed to fetch" エラーの特別な処理

### 修正 2: `sendMessage()` 関数のログインチェック追加

**ファイル**: `wwwroot/js/dealer-chat-widget.js` (行 1170-1240)

**修正後**:
```javascript
async function sendMessage() {
  // ... 前略 ...

  try {
    // ログインチェック
    if (!isUserLoggedIn()) {
      addMessage('ログインが必要です。ログインしてください。', 'system');
      updateStatus('error');
      setSendingState(false);
      return;
    }

    // セッションがなければ先に開始
    if (!dealerConversationId) {
      await startDealerSession();
    }
    if (!dealerConversationId) {
      addMessage('セッションを開始できませんでした。ページを再読み込みしてください。', 'system');
      updateStatus('error');
      setSendingState(false);
      return;
    }

    const msgUrl = CONFIG.chatApiBase + '/' + currentTheme.msgPath + '/' + dealerConversationId + '/message';
    console.log('DealerChat: メッセージ送信 URL:', msgUrl);
    
    const response = await fetch(msgUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: message })
    });

    if (response.ok) {
      const data = await response.json();
      const reply = data.responseText || data.result || data.message || '';
      if (reply.trim()) {
        addMessage(reply, 'assistant');
      }
      updateStatus('completed');
    } else {
      let errMsg = 'エラーが発生しました';
      try {
        const errBody = await response.json();
        errMsg = errBody.error || errMsg;
      } catch (_) {}
      
      if (response.status === 401) {
        errMsg = 'ログインが必要です。ログインしてください。';
      } else if (response.status === 404) {
        errMsg = 'セッションが見つかりません。ページを再読み込みしてください。';
      } else if (response.status === 500) {
        errMsg = 'サーバーエラーが発生しました。しばらくお待ちください。';
      }
      
      addMessage(`エラー：${errMsg}`, 'system');
      updateStatus('error');
    }
  } catch (error) {
    console.error('Send message error:', error);
    let errorMsg = error.message || '不明なエラー';
    
    // "Failed to fetch" の場合、より具体的なメッセージを表示
    if (errorMsg.includes('Failed to fetch')) {
      errorMsg = 'サーバーに接続できません。サーバーが起動しているか確認してください。';
    }
    
    addMessage(`リクエスト失敗：${errorMsg}`, 'system');
    updateStatus('error');
  } finally {
    setSendingState(false);
  }
}
```

**改善点**:
- 明示的なログインチェックの追加
- API URL のデバッグログ出力
- HTTP ステータスコードに基づくエラーメッセージ
- "Failed to fetch" エラーの特別な処理

## トラブルシューティングガイド

### ユーザー向け

#### エラー「リクエスト失敗：サーバーに接続できません」が表示された場合

1. **サーバーの起動状態を確認**
   - 管理者にサーバーが起動しているか確認してください
   
2. **ログイン状態を確認**
   - 画面右上にユーザー名が表示されているか確認
   - 表示されていない場合は、ログインし直してください

3. **ページをリロード**
   - ブラウザの更新ボタンをクリック
   - それでも解決しない場合は、ブラウザを完全に閉じて再度開く

4. **ブラウザのコンソールを確認**（上級者向け）
   - F12 キーを押して開発者ツールを開く
   - 「Console」タブにエラーメッセージが表示されていないか確認

### 管理者・開発者向け

#### 1. サーバー起動状態の確認

```bash
# プロセス確認
ps aux | grep -E "dotnet.*NetYamlForge"

# ログ確認
tail -f /path/to/application.log
```

#### 2. デバッグログの確認

ブラウザの開発者ツール（F12）で以下のログを確認：

```
DealerChat: セッション開始 URL: /auto-dealer-demo/api/ai/chat/session
DealerChat: メッセージ送信 URL: /auto-dealer-demo/api/ai/chat/session/{id}/message
```

#### 3. API エンドポイントの動作確認

```bash
# セッション開始 API をテスト
curl -X POST http://localhost:5000/auto-dealer-demo/api/ai/chat/session \
  -H "Content-Type: application/json" \
  -d '{"channel": "staff"}' \
  -c cookies.txt

# メッセージ送信 API をテスト
curl -X POST http://localhost:5000/auto-dealer-demo/api/ai/chat/session/{id}/message \
  -H "Content-Type: application/json" \
  -d '{"message": "テスト"}' \
  -b cookies.txt
```

#### 4. CORS 設定の確認

`Program.cs` または `Startup.cs` で CORS 設定を確認：

```csharp
app.UseCors(policy =>
{
    policy.AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials()  // 認証付きリクエストに必要
          .WithOrigins("https://your-domain.com"); // 許可するオリジン
});
```

#### 5. 認証設定の確認

ログインしていないユーザーが API にアクセスしようとすると 401 エラーになります。

```csharp
// AutoDealerChatController.cs
[Authorize]  // この属性があるとログイン必須
[HttpPost("staff/{conversationId}/message")]
public async Task<IActionResult> SendStaffMessage(...)
```

## 予防策

### 1. 自動ヘルスチェック

定期的に API の死活監視を実行：

```javascript
setInterval(async () => {
  try {
    const resp = await fetch('/api/ai/health');
    if (!resp.ok) {
      console.warn('AI サーバーの応答がありません');
      // 管理者に通知
    }
  } catch (e) {
    console.error('ヘルスチェック失敗', e);
  }
}, 60000); // 1 分ごと
```

### 2. エラー収集サービスの導入

- Application Insights
- Sentry
- LogRocket

などのサービスを使用して、エラーを自動的に収集・分析

### 3. ユーザーフレンドリーなエラーメッセージ

エラーコードではなく、ユーザーが理解できる言葉で表示：

| エラーコード | ユーザー表示 | 推奨アクション |
|------------|-------------|---------------|
| 401 | ログインが必要です | ログインページへ誘導 |
| 404 | セッションが見つかりません | ページ再読み込みを推奨 |
| 500 | サーバーエラーが発生しました | 時間をおいて再試行 |
| Failed to fetch | サーバーに接続できません | 管理者に連絡 |

## テスト手順

### 単体テスト

1. **ログインしていない状態でメッセージ送信**
   - 期待結果：「ログインが必要です」メッセージ表示

2. **サーバー停止状態でメッセージ送信**
   - 期待結果：「サーバーに接続できません」メッセージ表示

3. **無効なセッション ID でメッセージ送信**
   - 期待結果：「セッションが見つかりません」メッセージ表示

### 統合テスト

```bash
# 全テストを実行
dotnet test

# 特定のテストを実行
dotnet test --filter "FullyQualifiedName~AutoDealerChat"
```

## 関連ドキュメント

- [AI 助手完全指南](docs/ai-assistant-guide.md)
- [自動車販売 AI システムプロンプト](skills/auto-dealer/_system-prompt-staff.md)
- [ツール定義](skills/auto-dealer/_tools-definition.md)

## 変更履歴

| 日付 | 変更内容 | 担当者 |
|------|---------|--------|
| 2026/04/01 | エラー処理の改善、デバッグログ追加 | システム管理者 |

---

*最終更新：2026 年 4 月 1 日*
