# AI CLI ファースト移行 & 顧客向けAIファーストUX 設計ドキュメント

> 対象ブランチ: `feature/ai-window-system`
> 作成日: 2026-03-29

---

## 1. 現状分析

### 1.1 AI呼び出しの現在の実装方式

| サービス | 現在の方式 | 問題 |
|---|---|---|
| `AutoDealerChatService` | Claude API 直接 HTTP 呼び出し | プロバイダー固定、切り替え不可 |
| `LlmResponseGenerator` | `ILlmProvider` 経由（`OllamaProvider` など） | CLIと独立した実装体系 |
| `HybridIntentClassifier` | `ILlmProvider` 経由 | 同上 |
| `AIController` | `CLIServiceFactory` 経由 ✅ | すでにCLI対応済み |

### 1.2 CLI基盤の現状（すでに実装済み）

`Services/AI/Providers/` に以下の7プロバイダーが実装済み：

| プロバイダー | クラス | 設定セクション |
|---|---|---|
| Claude Code | `ClaudeCLIService` | `AICli:Claude` |
| Qwen Code | `QwenCodeCLIService` | `AICli:QwenCode` |
| Codex (OpenAI) | `CodexCLIService` | `AICli:Codex` |
| Gemini | `GeminiCLIService` | `AICli:Gemini` |
| Ollama | `OllamaCLIService` | `AICli:Ollama` |
| LM Studio | `LMStudioCLIService` | `AICli:LmStudio` |
| GitHub Copilot | `CopilotCLIService` | `AICli:Copilot` |

`CliConfig.DefaultTool`（デフォルト: `"claude"`）で使用するプロバイダーを切り替える。

### 1.3 顧客向けチャットウィンドウの現在位置

- **`auto-dealer-chat-widget.js`** が右下フローティングボタン（FAB）として外部サイトへの埋め込み用に設計
- **`AutoDealerChatController`** がAPIエンドポイントを提供（認証不要）
- **`OperatorChatController`** がオペレーター管理UI（認証必要）を提供
- 顧客がシステムにログインした際のランディングページには**顧客ロールが定義されていない**

---

## 2. AI CLI ファースト移行方針

### 2.1 基本原則

すべての後端 AI 呼び出しを `CLIServiceFactory` 経由に統一する。
フォールバック順位：

```
CLIServiceFactory (DefaultTool)
  → CLI 実行成功  → 結果を返す
  → CLI 失敗/タイムアウト → 直接 API フォールバック（オプション）
  → 直接 API 失敗 → テンプレート応答
```

### 2.2 `AutoDealerChatService` の移行

**変更前**: `IHttpClientFactory` で Claude API を直接呼び出す

```csharp
// 現状（移行対象）
var client = _httpClientFactory.CreateClient();
client.DefaultRequestHeaders.Add("x-api-key", ClaudeApiKey);
var response = await client.PostAsync("https://api.anthropic.com/v1/messages", ...);
```

**変更後**: `CLIServiceFactory` 経由で CLI を呼び出す

```csharp
// 移行後
var cli = _cliFactory.GetService(_cliConfig.DefaultTool);
var prompt = BuildDealerPrompt(customerMessage, history, intent);
var result = await cli.ExecuteAsync(prompt, workingDirectory: null);
return result.Output;
```

**移行対象メソッド**:
- `GenerateResponseAsync()` — AI応答生成
- `ClassifyIntentAsync()` — 意図分類（将来CLIへ移行）

### 2.3 `LlmResponseGenerator` の移行

`ILlmProvider` を `ICLIService` アダプターに置き換える。
`OllamaProvider`（直接HTTP）は既存プロジェクトとの互換性のため維持しつつ、新規呼び出しはCLI経由を優先。

**新しい優先順位**:
1. ナレッジベース検索（変更なし）
2. **CLI経由 LLM呼び出し**（新規）
3. `ILlmProvider` 直接呼び出し（後方互換フォールバック）
4. テンプレート応答（変更なし）

### 2.4 設定例（appsettings.json）

```json
{
  "AICli": {
    "DefaultTool": "claude",
    "TaskTimeoutSeconds": 30,
    "Claude": {
      "ApiKey": "",
      "Path": ""
    },
    "QwenCode": {
      "ApiKey": "",
      "Model": "qwen-max"
    },
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen2.5-coder:7b",
      "UseApi": true
    }
  },
  "AiWindow": {
    "DealerName": "AI 窓口ディーラー",
    "BusinessHours": "月〜土 9:00〜18:00",
    "FallbackToTemplate": true,
    "CliFirst": true
  }
}
```

---

## 3. 顧客向けAIファーストUX 設計

### 3.1 設計コンセプト

> 「ダッシュボードの前に、AIが顧客を迎える」

顧客がシステムにログインした際、すぐにデータ画面を見せるのではなく、**AI アシスタントが全画面で出迎え**、顧客の目的をヒアリングした後、最適なページへ誘導する。ダッシュボードは背景に薄く表示し、顧客が望めばいつでも移行できる。

### 3.2 UXフロー

```
顧客ログイン
    │
    ▼
顧客ダッシュボードページ（背景）
    │
    ▼ 同時に
    ▼
全画面AIオーバーレイ表示（前景）
  ┌──────────────────────────────────────┐
  │  🚗 AI アシスタント                  │
  │  「こんにちは！本日はどのようなご用件ですか？」│
  │                                      │
  │  [試乗の予約] [車を探す] [サービス確認]│
  │                                      │
  │  [チャット入力フォーム]              │
  │                                      │
  │  [×] ダッシュボードへ               │
  └──────────────────────────────────────┘
    │
    ├─ AIが目的を把握 → 該当ページへ誘導
    │       例: 「試乗の予約ですね。こちらへどうぞ →」
    │
    └─ [×]ボタン / ESC → オーバーレイを閉じてダッシュボード表示
```

### 3.3 顧客ランディングページの構成

#### レイヤー構造

```
z-index: 100  AIオーバーレイ（初期表示: 全画面）
z-index:   1  顧客ダッシュボード（背景: blur / dim）
```

#### AIオーバーレイの要素

| 要素 | 説明 |
|---|---|
| ヘッダー | ディーラーロゴ + 「AIアシスタント」 + 閉じるボタン |
| ウェルカムメッセージ | AI による挨拶（顧客名を含む） |
| クイックリプライボタン | 試乗予約 / 在庫検索 / 点検予約 / よくある質問 |
| チャット入力エリア | 自由入力フォーム + 送信ボタン |
| 誘導リンク | AIが返答後、「詳細はこちら→」で対象ページへ |
| フッター | 「ダッシュボードを表示する」テキストリンク |

#### 顧客ダッシュボード（背景）に表示する情報

顧客がオーバーレイを閉じた後に見えるダッシュボード：

```yaml
# pages/CustomerDashboard.yaml（新規作成対象）
sections:
  - id: my_vehicles        # 自分の車両情報
  - id: upcoming_appointments  # 次回の予約
  - id: service_history    # 点検・修理履歴
  - id: active_leads       # 商談進捗（購入検討中の場合）
  - id: ai_chat_summary    # 今日のAIチャット履歴サマリー
```

### 3.4 AI誘導シナリオ例

| 顧客の発言 | AI の判断 | 誘導先 |
|---|---|---|
| 「試乗したい」 | intent: appointment_test_drive | `/Page/Appointments?type=test_drive` |
| 「新型車を見たい」 | intent: vehicle_browse | `/Page/VehicleInventory` |
| 「前回の点検いつ？」 | intent: service_history | `/Page/ServiceRequests?filter=mine` |
| 「見積もりを出してほしい」 | intent: price_inquiry | オペレーターエスカレーション |
| 「担当者に聞きたい」 | intent: human_agent | オペレーターエスカレーション |

### 3.5 オーバーレイの動作仕様

- **初回ログイン時**: 毎回表示（セッションごと）
- **オーバーレイを閉じた後**: FABウィジェットに縮小（右下に常時表示）
- **ページ遷移後**: FABウィジェットとして継続動作
- **ESCキー**: オーバーレイを閉じてダッシュボードへ
- **AIによる誘導完了後**: 「詳細ページに移動しますか？ → 移動する / チャットを続ける」の選択肢を提示

---

## 4. 実装タスク

### 4.1 CLIファースト移行

| タスク | 対象ファイル | 優先度 |
|---|---|---|
| `AutoDealerChatService.GenerateResponseAsync()` を CLI 経由に変更 | `Services/AI/AutoDealerChatService.cs` | 高 |
| `LlmResponseGenerator` に CLI ファーストロジック追加 | `Services/AI/LlmResponseGenerator.cs` | 中 |
| `AiWindowConfig` に `CliFirst` フラグ追加 | `Models/AI/AiWindowConfig.cs` | 高 |
| `appsettings.json` に `AICli.DefaultTool` 設定追加 | `appsettings.json` | 高 |
| フォールバックテスト（CLI失敗時のテンプレート応答確認） | テスト | 中 |

### 4.2 顧客向けAIファーストUX

| タスク | 対象ファイル | 優先度 |
|---|---|---|
| `CustomerDashboard.yaml` 新規作成 | `projects/auto-dealer-demo/pages/CustomerDashboard.yaml` | 高 |
| `project.yaml` に `customer` ロールのランディング追加 | `projects/auto-dealer-demo/project.yaml` | 高 |
| AIオーバーレイ JavaScript (`customer-ai-overlay.js`) 実装 | `wwwroot/js/customer-ai-overlay.js` | 高 |
| Razor ビューにオーバーレイを埋め込む | `Views/Page/Index.cshtml` または `_Layout.cshtml` | 中 |
| 顧客ロール用ナビゲーション追加 | `projects/auto-dealer-demo/project.yaml` | 中 |
| オーバーレイの閉じた状態をセッションに保持 | フロントエンド JS | 低 |

---

## 5. ファイル構成（変更・新規作成一覧）

```
NetYamlForge/
├── Services/AI/
│   ├── AutoDealerChatService.cs          [修正] CLI経由に変更
│   └── LlmResponseGenerator.cs           [修正] CLIファーストロジック追加
│
├── Models/AI/
│   └── AiWindowConfig.cs                 [修正] CliFirst フラグ追加
│
├── wwwroot/js/
│   ├── auto-dealer-chat-widget.js         [既存] FABウィジェット（変更なし）
│   └── customer-ai-overlay.js            [新規] 全画面AIオーバーレイ
│
└── projects/auto-dealer-demo/
    ├── project.yaml                       [修正] customer ロール追加
    └── pages/
        └── CustomerDashboard.yaml         [新規] 顧客向けダッシュボード

appsettings.json                           [修正] AICli 設定追加
```

---

## 6. 設計上の考慮事項

### 6.1 CLIファーストのメリット

- **マルチプロバイダー**: 設定1行でClaude/Qwen/Gemini/Ollamaを切り替え可能
- **ローカルLLM対応**: Ollama/LM Studio でオフライン動作可能
- **コスト管理**: プロバイダーごとのコスト差を設定で最適化
- **統一インターフェース**: `ICLIService` 一本化でコードの複雑性削減

### 6.2 顧客AIファーストのメリット

- **初回体験の向上**: データ画面より先にAIが目的をヒアリング → 迷子防止
- **コンバージョン向上**: 試乗・見積もりへの誘導が自然な流れで発生
- **オペレーター負荷軽減**: AI が解決できる問い合わせを先にフィルタリング
- **データ収集**: 顧客の最初の発言から購買意欲・ニーズを把握

### 6.3 注意事項

- オーバーレイは**モバイルレスポンシブ**に対応する（全画面表示の高さ制限）
- 顧客が未ログインの場合は `auto-dealer-chat-widget.js`（外部埋め込み用）を使用
- CLI呼び出しのタイムアウトは顧客向けは **10秒以下** に設定（UX劣化防止）
- CLI未インストール環境では `FallbackToTemplate: true` で自動フォールバック

---

## 7. 顧客ロール定義（project.yaml 追加案）

```yaml
# project.yaml への追加
layout:
  landingPageByRole:
    customer: /auto-dealer-demo/Page/CustomerDashboard  # [新規]
    operator: /auto-dealer-demo/Page/OperatorConsole
    sales_rep: /auto-dealer-demo/Page/SalesRepDashboard
    # ... 既存ロール

# ナビゲーション追加
navigation:
  items:
    - label: マイページ
      url: /auto-dealer-demo/Page/CustomerDashboard
      icon: 👤
      section: ""
      roles: [customer]
    - label: 私の予約
      url: /auto-dealer-demo/Page/Appointments?filter=mine
      icon: 📅
      section: マイページ
      roles: [customer]
    - label: 車両情報
      url: /auto-dealer-demo/Page/VehicleInventory?filter=mine
      icon: 🚗
      section: マイページ
      roles: [customer]
```
