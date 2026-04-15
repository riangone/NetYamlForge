# Email 連携設定ガイド

## 概要

自動車ディーラー AI 窓口システムをメールサーバーと連携する設定手順です。

---

## 1. 必要パッケージ

この機能は以下の NuGet パッケージを使用します：

- **MailKit** (4.6.0) - IMAP/SMTP クライアント
- **MimeKit** (4.6.0) - メールメッセージ処理

```bash
dotnet add package MailKit --version 4.6.0
dotnet add package MimeKit --version 4.6.0
```

---

## 2. メールサーバー設定

### 2.1 推奨メールプロバイダー

| プロバイダー | IMAP サーバー | SMTP サーバー | 備考 |
|-------------|--------------|--------------|------|
| **Gmail** | `imap.gmail.com:993` | `smtp.gmail.com:587` | アプリパスワード必要 |
| **Office 365** | `outlook.office365.com:993` | `smtp.office365.com:587` | 認証必要 |
| **さくらインターネット** | `imap.sakura.ne.jp:993` | `smtp.sakura.ne.jp:587` | 標準 IMAP |
| **カスタム** | 独自設定 | 独自設定 | 下記設定を参照 |

### 2.2 appsettings.json 設定

```json
{
  "Email": {
    "Enabled": true,
    
    "IncomingServer": "imap.gmail.com",
    "IncomingPort": 993,
    "IncomingUseSsl": true,
    "IncomingUsername": "support@your-dealership.com",
    "IncomingPassword": "your-app-password",
    
    "OutgoingServer": "smtp.gmail.com",
    "OutgoingPort": 587,
    "OutgoingUseSsl": true,
    "OutgoingUsername": "support@your-dealership.com",
    "OutgoingPassword": "your-app-password",
    
    "FromAddress": "support@your-dealership.com",
    "FromName": "自動車ディーラー AI アシスタント",
    
    "PollingIntervalMinutes": 5
  }
}
```

### 2.3 環境変数での設定（推奨）

```bash
# 開発環境
export Email__Enabled=true
export Email__IncomingServer="imap.gmail.com"
export Email__IncomingPort=993
export Email__IncomingUsername="support@your-dealership.com"
export Email__IncomingPassword="your-app-password"
export Email__OutgoingServer="smtp.gmail.com"
export Email__OutgoingPort=587
export Email__OutgoingUsername="support@your-dealership.com"
export Email__OutgoingPassword="your-app-password"
export Email__FromAddress="support@your-dealership.com"
export Email__FromName="自動車ディーラー AI アシスタント"
export Email__PollingIntervalMinutes=5
```

---

## 3. Gmail 設定（推奨）

### 3.1 2 段階認証の有効化

1. Google アカウント設定 → セキュリティ
2. 「2 段階認証プロセス」を有効化

### 3.2 アプリパスワードの発行

1. Google アカウント → セキュリティ → アプリパスワード
2. 「メール」を選択
3. デバイス名を入力（例：`NetYamlForge AI`）
4. 生成された 16 桁パスワードをコピー
5. `IncomingPassword` と `OutgoingPassword` に設定

### 3.3 IMAP の有効化

1. Gmail 設定 → 「すべての設定」→「メール転送と POP/IMAP」
2. 「IMAP を有効にする」を選択
3. 変更を保存

---

## 4. 動作確認

### 4.1 メール受信テスト

```bash
# テストメール送信
echo "テストメール本文" | mail -s "AI テスト" support@your-dealership.com
```

### 4.2 ログ確認

```
[INFO] メールポーリング実行
[INFO] 3 件のメールを受信
[INFO] 受信メール処理：customer@example.com - お問い合わせ
[INFO] 応答メール送信済み：customer@example.com
```

### 4.3 応答メール確認

顧客に自動返信されたメールを確認：

```
件名：[自動応答] Re: お問い合わせ

お客様

お問い合わせありがとうございます。
自動車ディーラー AI アシスタントです。

─────────────────────────────
[AI 応答内容]
─────────────────────────────

自動車ディーラー AI アシスタント
営業時間：平日 9:00-19:00 / 土日祝 9:00-18:00
定休日：水曜日
TEL: 03-XXXX-XXXX

※本メールは AI により自動生成されています。
```

---

## 5. 高度な設定

### 5.1 メールフィルタリング

特定のアドレスからのメールのみ処理：

```json
{
  "Email": {
    "AllowedDomains": ["@example.com", "@test.com"],
    "BlockedAddresses": ["spam@example.com"]
  }
}
```

### 5.2 署名設定

```json
{
  "Email": {
    "Signature": {
      "Enabled": true,
      "Html": "<div class='signature'>
                 <p>────────────────────────────<br>
                 <strong>自動車ディーラー AI アシスタント</strong><br>
                 TEL: 03-XXXX-XXXX
                 </p>
               </div>"
    }
  }
}
```

### 5.3 添付ファイル処理

```json
{
  "Email": {
    "Attachments": {
      "Enabled": true,
      "MaxSizeMB": 10,
      "AllowedTypes": ["image/jpeg", "image/png", "application/pdf"],
      "SavePath": "/var/attachments"
    }
  }
}
```

---

## 6. 監視・運用

### 6.1 監視項目

| 項目 | 閾値 | アラート |
|------|------|---------|
| メール受信失敗率 | > 10% | Slack 通知 |
| 応答時間（平均） | > 5 分 | メール通知 |
| 未処理メール数 | > 50 | 日報 |

### 6.2 バックグラウンドサービス

Email ポーリングはバックグラウンドサービスとして動作：

```csharp
// Program.cs
builder.Services.AddHostedService<EmailPollingBackgroundService>();
```

### 6.3 エラーハンドリング

認証エラー、接続エラーは自動的にリトライされます：

- 初回：即時リトライ
- 2 回目：1 分後
- 3 回目：5 分後
- 4 回目以降：30 分間隔

---

## 7. トラブルシューティング

### Q: 認証エラーが発生する

**A1:** アプリパスワードを確認（Gmail の場合）

**A2:** 2 段階認証が有効か確認

**A3:** 安全性の低いアプリのアクセス許可（旧 Gmail アカウント）

### Q: メールが受信できない

**A1:** IMAP が有効か確認

```bash
telnet imap.gmail.com 993
```

**A2:** ファイアウォール設定を確認

**A3:** メールボックス容量を確認

### Q: 応答メールが届かない

**A1:** SPF/DKIM/DMARC レコードを設定

```dns
; SPF レコード
@ IN TXT "v=spf1 include:_spf.google.com ~all"

; DKIM レコード（Gmail の場合）
google._domainkey IN TXT "v=DKIM1; k=rsa; p=..."

; DMARC レコード
_dmarc IN TXT "v=DMARC1; p=quarantine; rua=mailto:dmarc@your-dealership.com"
```

**A2:** 迷惑メールフォルダを確認

**A3:** 送信制限を確認（Gmail: 500 通/日）

---

## 8. セキュリティベストプラクティス

1. **パスワード管理**: 環境変数またはシークレットマネージャーを使用
2. **SSL/TLS**: 必ず暗号化接続を使用
3. **アプリパスワード**: メインパスワードではなくアプリパスワードを使用
4. **アクセス制限**: 特定の IP アドレスからのみアクセス許可
5. **ログ管理**: 認証情報をログに出力しない

---

## 9. 次のステップ

- [x] Phase 2-1: LINE 実機テスト設定
- [x] Phase 2-2: Email IMAP 実装
- [ ] Phase 2-3: ダッシュボード詳細画面
- [ ] Phase 2-4: 分析レポート PDF 出力

---

**最終更新**: 2026 年 3 月 28 日  
**バージョン**: 1.0  
**担当**: NetYamlForge AI Team
