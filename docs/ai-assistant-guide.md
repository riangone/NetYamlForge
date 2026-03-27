# AI アシスタントガイド

NetYamlForge の AI アシスタント機能に関するドキュメント一覧です。

---

## クイックスタート

### 対応 AI ツール

| ツール | タイプ | 説明 |
|--------|--------|------|
| **GitHub Copilot** 🆕 | クラウド | GitHub 公式 AI コーディングアシスタント |
| **Claude Code** | クラウド | Anthropic 製 AI コーディングアシスタント |
| **Qwen Code** | クラウド | Alibaba 製コード生成 AI |
| **OpenAI Codex** | クラウド | OpenAI 製コードモデル |
| **Google Gemini** | クラウド | Google 製 AI アシスタント |
| **Ollama** | ローカル 🆕 | 自前で動作する軽量モデルランナー |
| **LM Studio** | ローカル 🆕 | GUI 付きローカルモデルツール |
| **Mock** | テスト | 動作確認用のモック |

---

## 設定ガイド

### ローカルモデル設定（推奨）

| ドキュメント | 内容 |
|------------|------|
| [ローカルモデル設定ガイド](guides/ai-local-model-setup.md) | Ollama/LM Studio の完全設定手順 |

**メリット:**
- ✅ データが外部に送信されない
- ✅ 無料で無制限に使用可能
- ✅ オフラインで動作
- ✅ カスタムモデルを使用可能

**推奨モデル:**
- `qwen2.5-coder:7b` - プログラミングに最適（8GB VRAM）
- `qwen2.5-coder:14b` - 高品質（16GB VRAM）
- `llama3.2:3b` - 高速動作（4GB VRAM）

---

### クラウド AI 設定

| ドキュメント | 内容 |
|------------|------|
| [GitHub Copilot 設定](guides/ai-copilot-setup.md) | GitHub Copilot CLI の設定 |
| [Claude Code 設定](guides/ai-claude-setup.md) | Claude Code の API キー設定 |
| [Qwen Code 設定](guides/ai-qwen-setup.md) | Qwen Code の API キー設定 |

---

## 設定ファイル例

### appsettings.json

```json
{
  "AICli": {
    "DefaultTool": "copilot",
    "TaskTimeoutSeconds": 1800,
    "MaxConcurrentTasks": 2,
    "Copilot": {
      "Token": "",
      "Path": ""
    },
    "Claude": {
      "ApiKey": "",
      "Path": ""
    },
    "QwenCode": {
      "ApiKey": "",
      "BaseUrl": "",
      "Model": ""
    },
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen2.5-coder:7b",
      "UseApi": true,
      "ContextSize": 4096,
      "Temperature": 0.7
    },
    "LmStudio": {
      "BaseUrl": "http://localhost:1234",
      "Model": "qwen2.5-coder-7b-instruct",
      "ContextSize": 4096,
      "Temperature": 0.7
    }
  }
}
```

---

## 使い方

1. **AI チャットウィンドウを開く**
   - 右上の 💬 ボタンをクリック

2. **AI ツールを選択**
   - ドロップダウンから使用する AI を選択

3. **コマンドを入力**
   - 例：「Task エンティティの YAML を作成して」
   - 例：「このプロジェクトの構造を分析して」

4. **結果を確認**
   - 生成されたコードやファイルを確認・適用

---

## トラブルシューティング

| 問題 | 解決策 |
|------|--------|
| ローカルモデルが接続できない | [トラブルシューティングセクション](guides/ai-local-model-setup.md#故障排查) を参照 |
| 応答が遅い | モデルを小さくする、ContextSize を減らす |
| 品質が悪い | Temperature を調整する、大きなモデルを使用 |

---

## 関連ドキュメント

- [AI ウィンドウシステム設計](../ai-window-system-design.md) - 内部設計
- [共通フック一覧](COMMON_HOOKS.md) - AI が生成するフックの例
- [開発者チュートリアル](developer-tutorial-ja.md) - AI を使った開発入門

---

*最終更新：2026 年 3 月*
