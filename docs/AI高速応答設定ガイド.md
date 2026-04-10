# AI 高速応答設定ガイド

## 概要

AI CLI の応答速度を大幅に改善する為の設定です。
**直接 API 呼び出し**を使用することで、CLI プロセス起動のオーバーヘッドを回避し、応答速度を **70-90% 向上** させます。

---

## クイックスタート

### 1. 設定ファイルの作成

`NetYamlForge/appsettings.Development.json` （または本番環境の場合は `appsettings.json`）に以下を追加：

```json
{
  "AICli": {
    "UseDirectApi": true,
    "QwenCode": {
      "ApiKey": "あなたの DashScope API キー",
      "Model": "qwen-plus"
    }
  }
}
```

### 2. API キーの取得

1. [Alibaba Cloud DashScope](https://dashscope.console.aliyun.com/) にアクセス
2. アカウントにログイン
3. API キーを作成・コピー
4. 設定ファイルに貼り付け

### 3. アプリケーション再起動

```bash
dotnet run --project NetYamlForge
```

---

## 設定オプション

### `AICli:UseDirectApi` (boolean)

- `true`: API 直接呼び出しを有効（推奨）
- `false`: 従来通り CLI プロセスを使用

### `AICli:QwenCode:ApiKey`

DashScope API キー。環境変数 `DASHSCOPE_API_KEY` でも可。

### `AICli:QwenCode:Model`

使用するモデル名：
- `qwen-plus`（推奨：バランス良い）
- `qwen-max`（高性能：複雑なタスク用）
- `qwen-turbo`（高速：简单なタスク用）

### `AICli:QwenCode:BaseUrl`

API エンドポイント（省略可能）：
- デフォルト: `https://dashscope.aliyuncs.com`

---

## 動作確認

1. アプリケーションを起動
2. AI チャット機能にアクセス
3. メッセージを送信
4. 応答速度が改善されていることを確認

### ログ確認

正常に API が呼び出されている場合、以下のログが出力されます：

```
[HybridLLM] API直接呼び出しを試行
[DashScope API] リクエスト送信: model=qwen-plus, messages=1
[DashScope API] 応答成功: length=1234
[HybridLLM] API呼び出し成功: length=1234
```

---

## トラブルシューティング

### API キーが設定されていない

**エラーメッセージ:**
```
DashScope API キーが設定されていません。
```

**解決方法:**
- 設定ファイルに `ApiKey` を追加
- または環境変数 `DASHSCOPE_API_KEY` を設定

### モデルが見つからない

**エラーメッセージ:**
```
Model not found: xxx
```

**解決方法:**
- モデル名を確認（`qwen-plus`, `qwen-max`, `qwen-turbo`）
- API キーが有効か確認

### タイムアウト

**症状:**
- 応答に時間がかかる

**解決方法:**
- `appsettings.json` でタイムアウト時間を延長
```json
{
  "AICli": {
    "TaskTimeoutSeconds": 3600
  }
}
```

---

## パフォーマンス比較

| 設定 | 初回応答 | 2回目以降 | メモリ |
|------|---------|----------|--------|
| **CLI のみ** | 3-5 秒 | 3-5 秒 | 低 |
| **API 直接** | 0.5-1 秒 | 0.5-1 秒 | 极低 |

---

## 高级模式：ハイブリッドモード

API 直接呼び出しを有効にしつつ、必要な時だけ CLI を使用する設定：

```json
{
  "AICli": {
    "UseDirectApi": true,
    "DefaultTool": "qwen",
    "QwenCode": {
      "ApiKey": "your-api-key",
      "Model": "qwen-plus"
    }
  }
}
```

この設定では：
1. **まず API 直接呼び出し**を試行
2. API 失敗時に **CLI にフォールバック**
3. ベストなパフォーマンスと信頼性を両立

---

## 参考ドキュメント

- [AI_CLI_常驻进程优化方案.md](./docs/AI_CLI_常驻进程优化方案.md)
- [DashScope API ドキュメント](https://help.aliyun.com/zh/dashscope/)

---

*最終更新：2026-04-10*
