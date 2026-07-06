# LINE 公式アカウント連携設定ガイド

## 概要

自動車ディーラー AI 窓口システムを LINE 公式アカウントと連携する設定手順です。

---

## 1. LINE 開発者アカウント設定

### 1.1 LINE Developers コンソールでチャンネル作成

1. [LINE Developers コンソール](https://developers.line.biz/console/) にアクセス
2. 「Create a new provider」をクリック
3. プロバイダー名を入力（例：`自動車ディーラー AI 窓口`）
4. 「Create」をクリック

### 1.2 Messaging API チャンネル作成

1. 作成したプロバイダーを選択
2. 「Create a new channel」→「Messaging API」を選択
3. 基本情報を入力：
   - **チャンネル名**: `自動車ディーラー AI ボット`
   - **チャンネル説明**: `AI による 24 時間顧客対応ボット`
   - **カテゴリ**: `Automotive`
   - **サブカテゴリ**: `Car Dealer`
4. 「Create」をクリック

### 1.3 基本設定

1. **チャンネルアイコン**: 自動車ディーラーのロゴを設定（推奨サイズ：240x240px）
2. **カバー画像**: 1000x1000px 以上の画像
3. **ホーム URL**: `https://your-dealership.com`
4. **プライバシーポリシー URL**: `https://your-dealership.com/privacy`

---

## 2. Messaging API 設定

### 2.1 基本設定タブ

| 設定項目 | 値 | 説明 |
|---------|-----|------|
| **チャンネルシークレット** | （自動生成）| Webhook 署名検証に使用 |
| **Channel Access Token** | （発行）| API リクエストに使用 |
| **Bot 利用** | 利用する | AI ボットとして応答 |

### 2.2 メッセージ応答設定

```
応答メッセージ：
├─ 友達追加：「こんにちは！自動車ディーラー AI アシスタントです。
│             営業時間、予約、車両のお問い合わせなど、
│             お気軽にお尋ねください！」
├─ 不在時応答：「只今対応中です。しばらくお待ちください。」
└─ デフォルト：「申し訳ございませんが、理解できませんでした。
                 もう一度お尋ねください。」
```

### 2.3 Webhook 設定

1. **Webhook URL**: 
   ```
   https://your-domain.com/api/line/webhook
   ```

2. **Webhook 利用**: オン

3. **詳細設定**:
   - [x] 友達追加時イベント
   - [x] 不在時応答
   - [x] 返信メッセージ

### 2.4 自動応答設定

**無効にしてください**（AI システムで処理するため）

- [ ] 自動応答メッセージ
- [ ] キーワードメッセージ

---

## 3. NetYamlForge 設定

### 3.1 appsettings.json 設定

```json
{
  "Line": {
    "Enabled": true,
    "ChannelAccessToken": "YOUR_CHANNEL_ACCESS_TOKEN",
    "ChannelSecret": "YOUR_CHANNEL_SECRET",
    "BotDisplayName": "自動車ディーラー AI ボット"
  }
}
```

### 3.2 環境変数での設定（推奨）

```bash
# 開発環境
export Line__ChannelAccessToken="eyJ0eXAiOiJKV1QiLCJhbGc..."
export Line__ChannelSecret="xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"

# 本番環境（Docker）
docker run -e Line__ChannelAccessToken="..." -e Line__ChannelSecret="..." ...
```

### 3.3 開発環境用設定（appsettings.Development.json）

```json
{
  "Line": {
    "Enabled": false,
    "ChannelAccessToken": "dev_token_here",
    "ChannelSecret": "dev_secret_here",
    "UseMock": true
  }
}
```

---

## 4. 動作確認

### 4.1 Webhook 接続テスト

1. LINE Developers コンソールで「Webhook 送信テスト」を実行
2. 以下のログが出力されれば成功：

```
[INFO] LINE Webhook 受信：EventType=follow, UserId=Uxxxxxxxx
[INFO] 友達追加歓迎メッセージ送信済み
```

### 4.2 友達追加テスト

1. LINE アプリで QR コードまたは友達追加 URL から追加
2. 歓迎メッセージが自動送信されることを確認

### 4.3 メッセージ応答テスト

```
# テストメッセージ例
ユーザー：「営業時間を教えてください」
AI：「営業時間は以下の通りです：
     平日：9:00 - 19:00
     土日祝：9:00 - 18:00
     定休日：水曜日
     
     サービス予約は 24 時間受付中です。」
```

---

## 5. 本番環境設定

### 5.1 SSL 証明書

Webhook URL は HTTPS が必須です：

```bash
# Let's Encrypt で SSL 証明書取得
certbot --nginx -d your-domain.com
```

### 5.2 ドメイン設定

- **推奨ドメイン**: `bot.your-dealership.com`
- **DNS 設定**: A レコードでサーバー IP を設定

### 5.3 負荷分散

複数インスタンス構成の場合：

```yaml
# docker-compose.yml
services:
  ai-window:
    deploy:
      replicas: 2
      resources:
        limits:
          cpus: '1'
          memory: 1G
```

---

## 6. 監視・ログ

### 6.1 監視項目

| 項目 | 閾値 | アラート |
|------|------|---------|
| Webhook エラー率 | > 5% | Slack 通知 |
| 応答時間（P95） | > 3 秒 | メール通知 |
| 未回答率 | > 20% | 日報 |

### 6.2 ログ出力設定

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "NetYamlForge.Controllers.Api.LineWebhookController": "Debug"
      }
    }
  }
}
```

---

## 7. トラブルシューティング

### Q: Webhook が届かない

**A1:** Webhook URL が正しいか確認
```bash
curl -X POST https://your-domain.com/api/line/webhook \
  -H "Content-Type: application/json" \
  -d '{"events":[]}'
```

**A2:** SSL 証明書を確認
```bash
openssl s_client -connect your-domain.com:443
```

### Q: 応答が遅い

**A1:** LLM API の応答時間を確認
- Qwen API: 平均 1-2 秒
- Claude API: 平均 2-3 秒

**A2:** キャッシュ設定を見直す
```json
{
  "Ai": {
    "Cache": {
      "Enabled": true,
      "SessionTimeoutMinutes": 30
    }
  }
}
```

### Q: 認証エラー

**A:** Channel Access Token の有効期限を確認（30 日間）

```bash
# トークン再発行
curl -X POST https://api.line.me/v2/oauth/accessToken \
  -d "grant_type=client_credentials&client_id=xxx&client_secret=xxx"
```

---

## 8. 次のステップ

- [ ] Phase 2-2: Email IMAP 実装
- [ ] Phase 2-3: ダッシュボード詳細画面
- [ ] Phase 2-4: 分析レポート PDF 出力

---

**最終更新**: 2026 年 3 月 28 日  
**バージョン**: 1.0  
**担当**: NetYamlForge AI Team
