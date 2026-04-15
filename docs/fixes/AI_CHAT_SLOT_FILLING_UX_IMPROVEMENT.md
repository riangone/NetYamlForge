# AI チャット応答速度改善 & インタラクティブコンポーネント実装計画

> **作成日**: 2026-04-10
> **対象**: auto-dealer-demo プロジェクト（試乗予約チャット）
> **ステータス**: ✅ 実装完了（Phase 0-4 完了、Phase 5 動作テスト待ち）

---

## 概要

試乗予約チャットにおける2つのUX課題を解決する：

1. **応答遅延** — 送信〜応答まで 40-60 秒の间隔が長すぎる
2. **テキスト入力負担** — 選択肢提示なのにユーザーがテキストを入力する必要がある

本ドキュメントは **詳細設計 + 実装計画** をまとめたものである。

---

## 目次

1. [現状分析](#1-現状分析)
2. [課題1: 応答遅延の解決策](#2-課題1-応答遅延の解決策)
3. [課題2: インタラクティブコンポーネント](#3-課題2-インタラクティブコンポーネント)
4. [アーキテクチャ設計](#4-アーキテクチャ設計)
5. [実装計画](#5-実装計画)
6. [テスト計画](#6-テスト計画)
7. [リスクと対策](#7-リスクと対策)
8. [チェックリスト](#8-チェックリスト)

---

## 1. 現状分析

### 1.1 試乗予約フローの観察

```
User:  試乗を予約したい               12:20:35
AI:    車種を教えてください            12:21:10  ← 35秒
User:  プリウス                       12:21:19
AI:    希望日を教えてください          12:21:53  ← 34秒
User:  明日                           12:22:06
AI:    時間帯を教えてください          12:22:51  ← 45秒
User:  午前10時                       12:23:07
AI:    お名前を教えてください          12:23:46  ← 39秒
User:  田中みなみ                     12:24:00
AI:    電話番号を教えてください        12:24:34  ← 34秒
User:  090-1234-5678                  12:24:44
AI:    予約内容ご確認ください          12:25:33  ← 49秒
User:  はい                           12:25:51
AI:    予約確定                        12:27:23  ← 92秒
```

**合計**: 約7分（うちAI応答待機時間 約5分）

### 1.2 現在の処理フロー（遅延の原因）

```
ユーザー送信
  → AutoDealerChatService.SendMessageAsync()
    → HybridIntentClassifier.ClassifyAsync()          ← ルールマッチ（高速）
    → BaseChatService.GenerateAiResponseAsync()
      → ILlmProvider.CompleteAsync()
        → DashScopeApiProvider.ChatAsync()             ← API呼び出し
          OR
        → PooledCLIService.ExecuteAsync()              ← CLIプロセス
          → ProcessExecutor → qwen プロセス起動         ← ここがボトルネック
    → BuildComponents()                                 ← UIコンポーネント生成
    → SaveMessageToHistory()
  → レスポンス返却
```

**ボトルネックの特定**:

| 段階 | 推定時間 | 備考 |
|------|---------|------|
| インテント分類 | 10-50ms | ルールベースなので高速 |
| LLM 呼び出し | **30-55秒** | ここが主原因 |
| コンポーネント生成 | 5-10ms | 無視できる |
| DB保存 | 10-30ms | 無視できる |
| **合計** | **30-55秒** | |

### 1.3 既存の高速化仕組み（未活用）

プロジェクトには既に以下の高速化仕組みが**実装済み**だが、適切に設定・活用されていない：

| 仕組み | 状態 | 問題点 |
|--------|------|--------|
| `DashScopeApiProvider` | ✅ 実装済み | 設定されていない可能性 |
| `AIProcessPoolManager` | ✅ 実装済み | デーモンモードが未対応 |
| `HybridLlmProvider` | ❌ 未実装 | API優先+CLIフォールバックの切り替え |

### 1.4 UI コンポーネントの現状

| コンポーネント | バックエンド | フロントエンド | 状態 |
|---------------|-------------|---------------|------|
| `UiComponent` モデル | ✅ `Models/AI/UiComponents.cs` | - | 定義済み |
| `ai-chat-components.js` | - | ✅ 実装済み | レンダラー存在 |
| `ai-chat-components.css` | - | ✅ 実装済み | スタイル存在 |
| `SendMessageResponse.Components` | ✅ `AIWindowRequests.cs` | ✅ 対応済み | 統合済み |
| `BuildComponents()` | ⚠️ 一部実装 | - | インテント限定 |

**結論**: UI コンポーネントの**インフラは既に存在する**。あとは Slot-filling フローで適切にコンポーネントを生成するだけ。

---

## 2. 課題1: 応答遅延の解決策

### 2.1 解決策の比較

| 方案 | 応答時間 | 実装コスト | 維持コスト | 推奨度 |
|------|---------|-----------|-----------|--------|
| **A. Direct API 有効化** | 1-3秒 | 低（設定のみ） | 低（API課金） | ★★★★★ |
| **B. プロセスプール活用** | 3-8秒 | 中 | 中（メモリ） | ★★★ |
| **C. ハイブリッドLLM** | 1-5秒 | 中 | 低 | ★★★★ |
| **D. ルールベース応答** | <100ms | 高 | 低 | ★★★ |

### 2.2 推奨方案: Direct API 有効化 + ルールベース応答（A + D）

#### 2.2.1 Direct API 有効化（即効性 ★★★★★）

**設定変更のみで 30-55秒 → 1-3秒 に改善**

```json
// appsettings.Development.json または appsettings.json
{
  "AICli": {
    "UseDirectApi": true,
    "QwenCode": {
      "ApiKey": "${DASHSCOPE_API_KEY}",
      "Model": "qwen-plus"
    },
    "TaskTimeoutSeconds": 120
  }
}
```

**効果**:
- CLI プロセス起動オーバーヘッド（3-5秒）がゼロに
- API 直接呼び出しなので応答一貫性が向上
- `qwen-plus` モデルでコストバランス最適

**検証コマンド**:
```bash
# ログ確認
# 成功時: [DashScope API] 応答成功: length=1234
# 失敗時: [DashScope API] エラー: ...
```

#### 2.2.2 Slot-filling 段階では LLM をスキップする（抜本的解決）

**核心アイディア**: Slot-filling 中は AI の推論が不要。ルールベースで応答を生成すれば **< 100ms** で応答できる。

```
試乗予約 Slot-filling フロー（改善後）:

User: 「プリウス」
  → IntentClassifier: test_drive_booking（ルールマッチ、< 50ms）
  → SlotFillingManager: vehicle_model スロットを更新
  → ✅ LLM を呼ばずに、テンプレートから応答生成（< 10ms）
  → 「プリウスの試乗ですね！承ります。希望日をお知らせください。」

User: 「明日」
  → SlotFillingManager: preferred_date スロットを更新
  → ✅ テンプレート応答（< 10ms）
  → 「明日（4月11日）ですね！時間帯をお知らせください。」

User: 「はい」（確認OK）
  → ✅ DB INSERT + 完了メッセージ（< 100ms）
  → 「予約が確定しました！」
```

**実装アプローチ**:

```
SendMessageAsync()
  │
  ├─ [1] アクティブSlot-filling セッション確認
  │    ├─ セッション存在 → [2] Slot-filling フロー（LLM不要）
  │    └─ セッションなし → [3] インテント分類 → LLMフロー
  │
  ├─ [2] Slot-filling フロー（高速）
  │    ├─ ExtractSlotValuesFromMessage()     ← 正規表現/ルール抽出
  │    ├─ UpdateSlotAsync()                  ← スロット更新
  │    ├─ IsComplete() チェック               ← 全スロット完了?
  │    │    ├─ 未完了 → BuildSlotPromptResponse()  ← テンプレート応答
  │    │    └─ 完了   → BuildConfirmationResponse() ← 確認画面
  │    └─ Components 生成（ボタン/日付ピッカー）
  │
  └─ [3] LLM フロー（通常）
       └─ GenerateAiResponseAsync()
```

**メリット**:
- Slot-filling 中は **LLM 呼び出しゼロ** → 100ms 以内応答
- API コスト大幅削減（1回の予約で 5-6回 の LLM 呼び出しがゼロに）
- 応答の一貫性が向上（テンプレートなのでブレない）

**デメリット**:
- テンプレートの管理コスト（多言語対応時）
- 複雑な質問には対応不可（LLM にフォールバックする仕組みが必要）

#### 2.2.3 フォールバック: Slot-filling 中でも LLM を呼べる仕組み

ユーザーが Slot-filling の質問に対して**想定外の回答**をした場合:

```
AI: 「希望日をお知らせください」
User: 「プリウスのバッテリー寿命はどれくらい？」  ← 想定外の質問

→ ExtractSlotValuesFromMessage() で日付が抽出できない
→ DetectOffTopicQuestion() で質問と判定
→ LLM にフォールバックして回答
→ Slot-filling セッションは維持（回答後再開）
```

---

## 3. 課題2: インタラクティブコンポーネント

### 3.1 設計方針

**既存のインフラを活用する**:
- `UiComponent` モデル（`Models/AI/UiComponents.cs`）— ✅ 定義済み
- `SendMessageResponse.Components` — ✅ 対応済み
- `ai-chat-components.js` — ✅ レンダラー実装済み
- `ai-chat-components.css` — ✅ スタイル実装済み

**新たに必要なもの**:
1. Slot-filling フローでの `BuildComponents()` 実装
2. フロントエンドでのコンポーネント送信処理（一部修正）
3. Slot-filling セッション状態とコンポーネントの連携

### 3.2 インテント別コンポーネント設計

#### 3.2.1 試乗予約 (test_drive_booking) — Slot-filling 各段階

| 段階 | 収集スロット | テキスト応答 | UI コンポーネント |
|------|-------------|-------------|------------------|
| **1. 車種選択** | `vehicle_model` | 「どの車種の試乗をご希望ですか？」 | `SingleSelectGroup`（在庫車種一覧） |
| **2. 日付選択** | `preferred_date` | 「ご希望の日付をお知らせください」 | `DateTimePicker` (mode: date) |
| **3. 時間選択** | `preferred_time` | 「ご希望の時間帯をお知らせください」 | `SingleSelectGroup`（時間帯の選択肢） |
| **4. 氏名入力** | `customer_name` | 「ご予約のため、お名前をお知らせください」 | `TextSuggestions`（入力補助） |
| **5. 電話番号入力** | `customer_phone` | 「ご連絡先電話番号をお知らせください」 | `TextSuggestions`（フォーマット例） |
| **6. 確認** | - | 「予約内容をご確認ください」 | `ConfirmPrompt`（はい/いいえ） |
| **7. 完了** | - | 「予約が確定しました！」 | `CardCarousel`（予約確認カード） |

#### 3.2.2 各コンポーネントの詳細仕様

##### 段階1: 車種選択（SingleSelectGroup）

```csharp
new SingleSelectGroup(
    Title: "どの車種の試乗をご希望ですか？",
    Options: new List<SelectOption>
    {
        new("トヨタ プリウス", "プリウス", Icon: "🚗", Description: "ハイブリッドカー"),
        new("トヨタ ランドクルーザー", "ランドクルーザー", Icon: "🚙", Description: "SUV"),
        new("トヨタ アルファード", "アルファード", Icon: "🚐", Description: "ミニバン"),
        new("ホンダ CR-V", "CR-V", Icon: "🚙", Description: "SUV"),
        new("ホンダ フィット", "フィット", Icon: "🚗", Description: "コンパクトカー"),
        new("日産 アリア", "アリア", Icon: "🚗", Description: "EV"),
    },
    SubmitLabel: "この車種で試乗"
)
```

**ユーザーが選択すると** → `value` がメッセージとして送信される → `ExtractSlotValuesFromMessage` で `vehicle_model` スロットにセット

##### 段階2: 日付選択（DateTimePicker）

```csharp
new DateTimePicker(
    Title: "ご希望の日付を選択してください",
    Mode: "date",
    MinDate: DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),  // 明日から
    MaxDate: DateTime.Today.AddMonths(1).ToString("yyyy-MM-dd"), // 1ヶ月後まで
    SubmitLabel: "この日にちで決定"
)
```

**ユーザーが選択すると** → 日付がフォーマットされて送信 → `ExtractSlotValuesFromMessage` で `preferred_date` スロットにセット

##### 段階3: 時間選択（SingleSelectGroup）

```csharp
new SingleSelectGroup(
    Title: "ご希望の時間帯を選択してください",
    Options: new List<SelectOption>
    {
        new("午前 9:00", "09:00", Icon: "🌅"),
        new("午前 10:00", "10:00", Icon: "🌅"),
        new("午前 11:00", "11:00", Icon: "🌅"),
        new("午後 13:00", "13:00", Icon: "☀️"),
        new("午後 14:00", "14:00", Icon: "☀️"),
        new("午後 15:00", "15:00", Icon: "☀️"),
        new("午後 16:00", "16:00", Icon: "🌇"),
        new("午後 17:00", "17:00", Icon: "🌇"),
    },
    SubmitLabel: "この時間で決定"
)
```

##### 段階4: 氏名入力（TextSuggestions）

```csharp
new TextSuggestions(
    Placeholder: "お名前を入力してください",
    Suggestions: new List<string>
    {
        "田中 太郎",
        "佐藤 花子",
        "鈴木 一郎"
    }
)
```

※ 氏名は個人差が大きいため、`TextSuggestions` は補助的な用途。メインはテキスト入力。

##### 段階5: 電話番号入力（TextSuggestions）

```csharp
new TextSuggestions(
    Placeholder: "電話番号を入力してください（例: 090-1234-5678）",
    Suggestions: new List<string>()
    // 電話番号にサジェストは不要。フォーマット例のみ表示
)
```

##### 段階6: 確認（ConfirmPrompt）

```csharp
new ConfirmPrompt(
    Question: "この内容で試乗予約を確定してよろしいですか？\n\n" +
              "車種: トヨタ プリウス\n" +
              "日付: 2026年4月11日\n" +
              "時間: 午前10時\n" +
              "お名前: 田中みなみ 様\n" +
              "電話: 090-1234-5678",
    ConfirmLabel: "はい、予約します",
    CancelLabel: "いいえ、変更する",
    ConfirmValue: "yes",
    CancelValue: "no",
    Style: "default"
)
```

##### 段階7: 完了（CardCarousel）

```csharp
new CardCarousel(
    Title: "✅ 試乗予約が確定しました",
    Items: new List<CardItem>
    {
        new CardItem(
            Id: "confirmation",
            Title: "試乗予約確認",
            Subtitle: "トヨタ プリウス / 2026年4月11日 10:00",
            BadgeLabel: "確定",
            BadgeStyle: "success",
            Actions: new List<CardAction>
            {
                new("詳細を見る", "postback", "予約詳細を確認したい"),
                new("予約を変更", "postback", "予約を変更したい"),
                new("キャンセル", "postback", "予約をキャンセルしたい"),
            }
        )
    }
)
```

### 3.3 コンポーネント生成の実装場所

```
AutoDealerChatService.cs
│
├─ SendMessageAsync()
│   └─ Slot-filling フロー中:
│       ├─ ProcessSlotFillingAsync()
│       │   └─ BuildSlotFillingComponents(scenario, slots) ← 新規メソッド
│       │
│       └─ BuildConfirmationResponse()
│           └─ BuildConfirmationComponents(slots) ← 新規メソッド
│
└─ BuildComponents() (既存)
    └─ switch (intent) で拡張
```

#### 3.3.1 `BuildSlotFillingComponents()` 設計

```csharp
/// <summary>
/// Slot-filling の現在の段階に応じた UI コンポーネントを生成
/// </summary>
private List<UiComponent>? BuildSlotFillingComponents(
    string scenario,
    SlotSession session,
    string? context = null)
{
    return scenario switch
    {
        "test_drive" => BuildTestDriveSlotComponents(session),
        "estimate"   => BuildEstimateSlotComponents(session),
        "appointment_service" => BuildServiceSlotComponents(session),
        _ => null
    };
}

/// <summary>
/// 試乗予約の Slot-filling コンポーネント
/// </summary>
private List<UiComponent> BuildTestDriveSlotComponents(SlotSession session)
{
    // まだ埋まっていない最初の必須スロットを特定
    var nextSlot = GetNextUnfilledSlot(session);

    return nextSlot switch
    {
        "vehicle_model" => BuildVehicleSelectionComponent(),
        "preferred_date" => BuildDateSelectionComponent(),
        "preferred_time" => BuildTimeSelectionComponent(),
        "customer_name" => BuildNameInputComponent(),
        "customer_phone" => BuildPhoneInputComponent(),
        _ => null
    };
}

private SingleSelectGroup BuildVehicleSelectionComponent()
{
    // DBから在庫車種を取得（またはキャッシュ）
    var vehicles = GetAvailableVehicles();

    return new SingleSelectGroup(
        Title: "どの車種の試乗をご希望ですか？",
        Options: vehicles.Select(v =>
            new SelectOption(v.DisplayName, v.ModelName, Icon: "🚗"))
            .ToList(),
        SubmitLabel: "この車種で試乗"
    );
}

private DateTimePicker BuildDateSelectionComponent()
{
    var tomorrow = DateTime.Today.AddDays(1);
    var oneMonthLater = DateTime.Today.AddMonths(1);

    return new DateTimePicker(
        Title: "ご希望の日付を選択してください",
        Mode: "date",
        MinDate: tomorrow.ToString("yyyy-MM-dd"),
        MaxDate: oneMonthLater.ToString("yyyy-MM-dd"),
        SubmitLabel: "この日にちで決定"
    );
}

private SingleSelectGroup BuildTimeSelectionComponent()
{
    var timeSlots = new[]
    {
        ("09:00", "午前 9:00", "🌅"),
        ("10:00", "午前 10:00", "🌅"),
        ("11:00", "午前 11:00", "🌅"),
        ("13:00", "午後 13:00", "☀️"),
        ("14:00", "午後 14:00", "☀️"),
        ("15:00", "午後 15:00", "☀️"),
        ("16:00", "午後 16:00", "🌇"),
        ("17:00", "午後 17:00", "🌇"),
    };

    return new SingleSelectGroup(
        Title: "ご希望の時間帯を選択してください",
        Options: timeSlots.Select(t =>
            new SelectOption(t.Item2, t.Item1, Icon: t.Item3)).ToList(),
        SubmitLabel: "この時間で決定"
    );
}

private TextSuggestions BuildNameInputComponent()
{
    return new TextSuggestions(
        Placeholder: "お名前を入力してください",
        Suggestions: new List<string>()
    );
}

private TextSuggestions BuildPhoneInputComponent()
{
    return new TextSuggestions(
        Placeholder: "電話番号を入力してください（例: 090-1234-5678）",
        Suggestions: new List<string>()
    );
}
```

#### 3.3.2 `BuildConfirmationComponents()` 設計

```csharp
private ConfirmPrompt BuildConfirmationComponents(SlotSession session)
{
    var vehicleName = session.GetSlotValue("vehicle_model") ?? "";
    var date = session.GetSlotValue("preferred_date") ?? "";
    var time = session.GetSlotValue("preferred_time") ?? "";
    var name = session.GetSlotValue("customer_name") ?? "";
    var phone = session.GetSlotValue("customer_phone") ?? "";

    var summary = $"車種: {vehicleName}\n日付: {date}\n時間: {time}\nお名前: {name} 様\n電話: {phone}";

    return new ConfirmPrompt(
        Question: $"この内容で試乗予約を確定してよろしいですか？\n\n{summary}",
        ConfirmLabel: "はい、予約します",
        CancelLabel: "いいえ、変更する",
        ConfirmValue: "yes",
        CancelValue: "no"
    );
}
```

### 3.4 フロントエンド側の修正

#### 3.4.1 修正が必要なファイル

| ファイル | 修正内容 | 工数 |
|---------|---------|------|
| `ai-chat-widget.js` | コンポーネント送信後のメッセージ処理 | 小 |
| `ai-chat-components.js` | コンポーネントの dismissed 状態の改善 | 小 |
| `ai-chat-components.css` | スマートフォン対応のレスポンシブ改善 | 中 |

#### 3.4.2 `ai-chat-widget.js` 修正箇所

**現状** (L819-825):
```javascript
if (extra?.components?.length && typeof AiChatComponents !== 'undefined') {
    const compEl = AiChatComponents.render(extra.components, (value) => {
        const inputEl = document.getElementById('aw-input');
        if (inputEl) inputEl.value = value;
        sendMessage();
    });
    row.appendChild(compEl);
}
```

**問題点**:
- コンポーネント操作で送信されたメッセージが「ユーザーメッセージ」として履歴に残る
- `value` がそのまま送信されるので、`SelectOption.Value` の生の値（例: `09:00`）が表示される

**修正案**:
```javascript
if (extra?.components?.length && typeof AiChatComponents !== 'undefined') {
    const compEl = AiChatComponents.render(extra.components, (value, label) => {
        // label（表示用テキスト）があればそれを使う、なければ value
        const displayValue = label || value;

        // ユーザーメッセージとして表示（ラベルで）
        renderMessage(displayValue, 'user');

        // 実際の送信は value
        sendDealerMessageWithComponentValue(value);
    });
    row.appendChild(compEl);
}

// 新規: コンポーネント値による送信
async function sendDealerMessageWithComponentValue(value) {
    setSending(true);
    try {
        // 通常と同じ API 呼び出し（値のみ送信）
        // バックエンドでは Slot-filling が値を解釈
        await sendDealerMessage(value);
    } finally {
        setSending(false);
    }
}
```

#### 3.4.3 `ai-chat-components.js` 修正案

**`renderOne()` の `onSubmit` コールバックシグネチャ変更**:

```javascript
// 変更前: onSubmit(value)
// 変更後: onSubmit(value, label)

function renderQuickReplyGroup(comp, onSubmit) {
    // ...
    btn.addEventListener('click', () => {
        if (comp.dismissible !== false) dismissGroup(div);
        onSubmit(item.value, item.label);  // label も渡す
    });
    // ...
}

function renderSingleSelect(comp, onSubmit) {
    // ...
    submitBtn.addEventListener('click', () => {
        if (!selected) return;
        const opt = comp.options.find(o => o.value === selected);
        dismissGroup(div);
        onSubmit(opt?.value || selected, opt?.label || selected);  // label も渡す
    });
    // ...
}

function renderConfirm(comp, onSubmit) {
    // ...
    confirmBtn.addEventListener('click', () => {
        dismissGroup(div);
        onSubmit(comp.confirmValue, comp.confirmLabel);
    });
    cancelBtn.addEventListener('click', () => {
        dismissGroup(div);
        onSubmit(comp.cancelValue, comp.cancelLabel);
    });
    // ...
}
```

---

## 4. アーキテクチャ設計

### 4.1 改善後の処理フロー

```
┌──────────────────────────────────────────────────────────────┐
│                    SendMessageAsync()                        │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  [1] アクティブ Slot-filling セッション確認                  │
│       │                                                      │
│       ├─ あり → [2] Slot-filling フロー（高速）              │
│       │        │                                             │
│       │        ├─ ExtractSlotValuesFromMessage()             │
│       │        │    (正規表現/ルール抽出, < 10ms)             │
│       │        │                                             │
│       │        ├─ UpdateSlotAsync()                          │
│       │        │    (スロット更新, < 5ms)                     │
│       │        │                                             │
│       │        ├─ IsOffTopic?                                │
│       │        │    ├─ Yes → [4] LLM フロー（フォールバック）│
│       │        │    └─ No  → [3]                              │
│       │        │                                             │
│       │        ├─ IsComplete?                                │
│       │        │    ├─ No  → BuildSlotPromptResponse()       │
│       │        │    │         + BuildSlotFillingComponents()  │
│       │        │    │         (テンプレート + コンポーネント) │
│       │        │    │         応答時間: < 50ms                │
│       │        │    │                                         │
│       │        │    └─ Yes → BuildConfirmationResponse()     │
│       │        │              + BuildConfirmationComponents() │
│       │        │              (確認画面)                       │
│       │        │              応答時間: < 50ms                │
│       │        │                                             │
│       │        └─ CompleteScenarioAsync()                    │
│       │             (DB INSERT + 完了メッセージ)               │
│       │             応答時間: < 200ms                         │
│       │                                                      │
│       └─ なし → [4] インテント分類 → LLM フロー              │
│                │                                             │
│                ├─ Slot-filling 開始インテント                 │
│                │   → StartSlotFillingAsync()                 │
│                │   → BuildSlotFillingComponents()             │
│                │   応答時間: < 100ms（初回のみLLM呼び出し）    │
│                │                                             │
│                └─ 一般インテント                              │
│                    → GenerateAiResponseAsync()                │
│                    (LLM 呼び出し, 1-3秒)                      │
│                    + BuildComponents()                        │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### 4.2 応答時間比較（改善前後）

| フロー | 現在 | 改善後 | 改善率 |
|--------|------|--------|--------|
| Slot-filling（各段階） | 30-55秒 | < 50ms | **99% 削減** |
| Slot-filling（初回LLM） | 30-55秒 | 1-3秒 | **90% 削減** |
| 確認→確定 | 30-92秒 | < 200ms | **99% 削減** |
| 一般質問 | 30-55秒 | 1-3秒 | **90% 削減** |
| **試乗予約合計（6往復）** | **約5分** | **約5秒** | **98% 削減** |

### 4.3 データフロー図

```
┌──────────┐      POST /message      ┌───────────────────┐
│          │ ───────────────────────→ │                   │
│  ブラウザ  │                         │  AutoDealerChat   │
│          │ ←────────────────────── │  Service          │
└──────────┘   JSON Response         │                   │
  │                                   └────────┬──────────┘
  │                                            │
  │  {                                        │
  │    "responseText": "...",                 │
  │    "components": [                        │
  │      {                                    │
  │        "type": "single_select",           │
  │        "title": "車種を選択",              │
  │        "options": [...]                   │
  │      }                                    │
  │    ],                                     │
  │    "intent": "test_drive_booking",        │
  │    "entities": { "vehicle": "プリウス" }  │
  │  }                                        │
  │                                            │
  ├────────────────────────────────────────────┤
  │                                            │
  │  AiChatComponents.render(components,       │
  │    (value, label) => {                     │
  │      renderMessage(label, 'user');         │
  │      sendDealerMessage(value);             │
  │    })                                      │
  │                                            │
  └────────────────────────────────────────────┘
```

---

## 5. 実装計画

### 5.1 フェーズ分け

| フェーズ | 内容 | 工数 | 依存関係 |
|---------|------|------|---------|
| **Phase 0** | 設定確認・環境整備 | 0.5日 | なし |
| **Phase 1** | Direct API 有効化 | 0.5日 | Phase 0 |
| **Phase 2** | Slot-filling 高速化（LLMスキップ） | 2日 | Phase 1 |
| **Phase 3** | UI コンポーネント実装 | 2日 | Phase 2 |
| **Phase 4** | フロントエンド修正 | 1日 | Phase 3 |
| **Phase 5** | テスト・検証 | 1日 | Phase 4 |
| **合計** | | **7日** | |

### 5.2 Phase 0: 設定確認・環境整備（0.5日）

#### 5.2.1 確認事項

```bash
# 1. DashScope API キーが設定されているか確認
cat appsettings.Development.json | grep -i apikey

# 2. 現在の LLM プロバイダーを確認
# Startup.cs または Program.cs で ILlmProvider の登録を確認

# 3. プロセスプールの状態確認
cat appsettings.Development.json | grep -i processpool
```

#### 5.2.2 必要な設定追加

```json
{
  "AICli": {
    "UseDirectApi": true,
    "QwenCode": {
      "ApiKey": "sk-...",
      "Model": "qwen-plus",
      "BaseUrl": "https://dashscope.aliyuncs.com"
    },
    "TaskTimeoutSeconds": 120,
    "ProcessPool": {
      "EnableDaemonMode": false
    }
  }
}
```

### 5.3 Phase 1: Direct API 有効化（0.5日）

#### 5.3.1 修正ファイル

| ファイル | 修正内容 |
|---------|---------|
| `appsettings.Development.json` | `UseDirectApi: true` 追加 |
| `Program.cs` or `Startup.cs` | `DashScopeApiProvider` の DI 登録確認 |
| `BaseChatService.cs` | `UseDirectApi` 設定の読み込み確認 |

#### 5.3.2 検証手順

```bash
# 1. アプリ起動
dotnet run --project NetYamlForge

# 2. AI チャットでメッセージ送信
# 3. ログ確認: [DashScope API] 応答成功 が出力されるか
# 4. 応答時間が 1-3秒 になっているか確認
```

### 5.4 Phase 2: Slot-filling 高速化（2日）

#### 5.4.1 修正ファイル

| ファイル | 修正内容 |
|---------|---------|
| `AutoDealerChatService.cs` | `SendMessageAsync` に Slot-filling 高速フロー追加 |
| `AutoDealerChatService.cs` | `ProcessSlotFillingAsync` 汎用化 |
| `AutoDealerChatService.cs` | `BuildSlotFillingComponents` 新規実装 |
| `AutoDealerChatService.cs` | `ExtractSlotValuesFromMessage` 拡張 |
| `AutoDealerChatService.cs` | `DetectOffTopicQuestion` 新規実装 |
| `SlotFillingManager.cs` | `GetNextUnfilledSlot` メソッド追加 |
| `SlotFillingManager.cs` | `GetSlotValue` メソッド追加 |

#### 5.4.2 実装詳細

##### `SendMessageAsync` の修正

```csharp
public override async Task<SendMessageResponse> SendMessageAsync(
    string conversationId,
    string message,
    CancellationToken ct = default)
{
    var sw = Stopwatch.StartNew();

    // [0] アクティブ Slot-filling セッション確認
    if (_slotFilling != null)
    {
        var activeScenario = await _slotFilling.GetActiveScenarioAsync(conversationId);
        if (activeScenario != null)
        {
            // Slot-filling 継続中 → 高速フロー
            return await ProcessSlotFillingFastAsync(
                conversationId, message, activeScenario, sw, ct);
        }
    }

    // [1] 通常フロー（インテント分類 → LLM）
    var response = await ProcessNormalFlowAsync(conversationId, message, sw, ct);

    // Slot-filling 開始インテントならセッションを開始
    if (response.Intent is "test_drive_booking" or "estimate_request"
        or "service_booking" or "trade_inquiry")
    {
        await StartSlotFillingSessionAsync(conversationId, response.Intent);
    }

    return response;
}
```

##### `ProcessSlotFillingFastAsync` 新規実装

```csharp
private async Task<SendMessageResponse> ProcessSlotFillingFastAsync(
    string conversationId,
    string message,
    string scenario,
    Stopwatch sw,
    CancellationToken ct)
{
    // [1] スロット値抽出（正規表現/ルール）
    await ExtractSlotValuesFromMessageAsync(conversationId, message, scenario);

    var session = await _slotFilling!.GetSessionAsync(conversationId);
    var nextSlot = _slotFilling.GetNextUnfilledSlot(session);

    // [2] オフトピック質問検出（LLM フォールバックが必要か）
    if (IsOffTopicQuestion(message, scenario))
    {
        return await ProcessSlotFillingFallbackAsync(
            conversationId, message, scenario, sw, ct);
    }

    // [3] 全スロット完了確認
    if (_slotFilling.IsComplete(session))
    {
        // 確認画面
        var response = BuildConfirmationResponse(conversationId, session, sw);
        response.Components = BuildConfirmationComponents(session);
        return response;
    }

    // [4] 次のスロットプロンプト
    var promptMessage = GetSlotPromptMessage(scenario, nextSlot);
    var response = new SendMessageResponse
    {
        ConversationId = conversationId,
        ResponseText = promptMessage,
        Intent = scenario,
        AiModel = "RuleBased",
        ProcessingTimeMs = sw.ElapsedMilliseconds,
        Entities = session.Slots.ToDictionary(
            s => s.Key, s => s.Value?.ToString() ?? "")
    };

    // UI コンポーネント追加
    response.Components = BuildSlotFillingComponents(scenario, session);

    return response;
}
```

##### `ExtractSlotValuesFromMessageAsync` 拡張

```csharp
private async Task ExtractSlotValuesFromMessageAsync(
    string conversationId,
    string message,
    string scenario)
{
    var lowerMessage = message.ToLowerInvariant();

    switch (scenario)
    {
        case "test_drive":
            // 車種抽出
            var vehicleKeywords = new Dictionary<string, string>
            {
                { "プリウス", "プリウス" },
                { "ランドクルーザー", "ランドクルーザー" },
                { "アルファード", "アルファード" },
                { "cr-v", "CR-V" },
                { "フィット", "フィット" },
                { "アリア", "アリア" },
                { "フォレスター", "フォレスター" },
                { "ヴェゼル", "ヴェゼル" },
            };
            foreach (var (keyword, value) in vehicleKeywords)
            {
                if (lowerMessage.Contains(keyword))
                {
                    await _slotFilling!.UpdateSlotAsync(
                        conversationId, "vehicle_model", value);
                    break;
                }
            }

            // 日付抽出
            var datePattern = @"(\d{1,2})月(\d{1,2})日";
            var dateMatch = Regex.Match(message, datePattern);
            if (dateMatch.Success)
            {
                var month = int.Parse(dateMatch.Groups[1].Value);
                var day = int.Parse(dateMatch.Groups[2].Value);
                var year = DateTime.Now.Year;
                var date = new DateTime(year, month, day);
                await _slotFilling!.UpdateSlotAsync(
                    conversationId, "preferred_date",
                    date.ToString("yyyy-MM-dd"));
            }
            else if (lowerMessage.Contains("明日"))
            {
                var tomorrow = DateTime.Today.AddDays(1);
                await _slotFilling!.UpdateSlotAsync(
                    conversationId, "preferred_date",
                    tomorrow.ToString("yyyy-MM-dd"));
            }

            // 時間抽出
            var timePattern = @"(\d{1,2})時";
            var timeMatch = Regex.Match(message, timePattern);
            if (timeMatch.Success)
            {
                var hour = int.Parse(timeMatch.Groups[1].Value);
                if (lowerMessage.Contains("午後") || lowerMessage.Contains("pm"))
                    hour += 12;
                await _slotFilling!.UpdateSlotAsync(
                    conversationId, "preferred_time",
                    $"{hour:D2}:00");
            }
            else
            {
                var timeKeywords = new Dictionary<string, string>
                {
                    { "午前中", "10:00" },
                    { "午後", "14:00" },
                    { "夕方", "16:00" },
                };
                foreach (var (keyword, value) in timeKeywords)
                {
                    if (lowerMessage.Contains(keyword))
                    {
                        await _slotFilling!.UpdateSlotAsync(
                            conversationId, "preferred_time", value);
                        break;
                    }
                }
            }

            // 氏名抽出（日本語名の正規表現は困難なので、スロットが空で他のスロットが埋まっている場合に判定）
            if (session.Slots["vehicle_model"]?.Filled == true &&
                session.Slots["preferred_date"]?.Filled == true &&
                session.Slots["preferred_time"]?.Filled == true &&
                session.Slots["customer_name"]?.Filled != true &&
                session.Slots["customer_phone"]?.Filled != true)
            {
                // 氏名Candidates: 日本語名らしき文字列
                // ここではシンプルにメッセージ全体を氏名として扱う
                await _slotFilling!.UpdateSlotAsync(
                    conversationId, "customer_name", message);
            }

            // 電話番号抽出
            var phonePattern = @"(\d{2,4}-\d{2,4}-\d{4})";
            var phoneMatch = Regex.Match(message, phonePattern);
            if (phoneMatch.Success)
            {
                await _slotFilling!.UpdateSlotAsync(
                    conversationId, "customer_phone", phoneMatch.Value);
            }
            break;

        case "estimate":
            // 見積もり用のスロット抽出（T-01 参照）
            break;

        case "appointment_service":
            // サービス予約用のスロット抽出（T-01 参照）
            break;
    }
}
```

##### `IsOffTopicQuestion` 新規実装

```csharp
/// <summary>
/// Slot-filling 中の質問がスロット収集と無関係かを判定
/// </summary>
private bool IsOffTopicQuestion(string message, string scenario)
{
    var lowerMessage = message.ToLowerInvariant();

    // 疑問詞を含む → オフトピックの可能性
    var questionWords = new[] { "？", "?", "か", "どう", "なぜ", "何", "いつ", "どこ" };
    var hasQuestionWord = questionWords.Any(w => lowerMessage.Contains(w));

    if (!hasQuestionWord) return false;

    // スロットに関連するキーワードがなければオフトピック
    var slotKeywords = GetSlotKeywords(scenario);
    var hasSlotKeyword = slotKeywords.Any(k => lowerMessage.Contains(k));

    return hasQuestionWord && !hasSlotKeyword;
}

private string[] GetSlotKeywords(string scenario) => scenario switch
{
    "test_drive" => new[]
    {
        "プリウス", "ランドクルーザー", "アルファード", "cr-v", "フィット",
        "アリア", "フォレスター", "ヴェゼル",
        "月", "日", "明日", "明後日", "来週", "今日",
        "時", "分", "午前", "午後", "朝", "夕方",
        "時", "分", "午前", "午後", "朝", "夕方",
    },
    "estimate" => new[] { "見積", "価格", "円", "万", "ローン", "月々" },
    "appointment_service" => new[] { "車検", "点検", "オイル", "タイヤ", "修理" },
    _ => Array.Empty<string>()
};
```

##### `ProcessSlotFillingFallbackAsync` 新規実装

```csharp
/// <summary>
/// Slot-filling 中のオフトピック質問に LLM で回答
/// </summary>
private async Task<SendMessageResponse> ProcessSlotFillingFallbackAsync(
    string conversationId,
    string message,
    string scenario,
    Stopwatch sw,
    CancellationToken ct)
{
    // LLM に回答を生成させる（Slot-filling コンテキストを付与）
    var fallbackPrompt = $"あなたは試乗予約の受付をしています。" +
        $"ユーザーから以下の質問がありました。簡潔に回答してください。" +
        $"なお、Slot-filling は中断せず、回答後に引き続き予約を受け付けてください。\n\n" +
        $"ユーザーの質問: {message}";

    var aiResponse = await _llmProvider.CompleteAsync(fallbackPrompt, ct);

    var response = new SendMessageResponse
    {
        ConversationId = conversationId,
        ResponseText = aiResponse,
        Intent = scenario,
        AiModel = _llmProvider.GetType().Name,
        ProcessingTimeMs = sw.ElapsedMilliseconds,
        IsFallback = true  // フォールバックフラグ
    };

    // フォールバック後も引き続きスロット収集を継続
    // コンポーネントは次回送信時に表示
    return response;
}
```

#### 5.4.3 `SlotFillingManager` 拡張

```csharp
// ISlotFillingManager インターフェースに追加
public interface ISlotFillingManager
{
    // ... 既存メソッド ...

    /// <summary>
    /// まだ埋まっていない最初の必須スロット名を返す
    /// </summary>
    string? GetNextUnfilledSlot(SlotSession session);

    /// <summary>
    /// 特定スロットの値を取得
    /// </summary>
    string? GetSlotValue(SlotSession session, string slotName);

    /// <summary>
    /// 全必須スロットが埋まったか判定
    /// </summary>
    bool IsComplete(SlotSession session);
}

// SlotFillingManager 実装
public string? GetNextUnfilledSlot(SlotSession session)
{
    foreach (var slotName in session.RequiredSlots)
    {
        if (!session.Slots.TryGetValue(slotName, out var slot) ||
            !slot.Filled ||
            string.IsNullOrWhiteSpace(slot.Value?.ToString()))
        {
            return slotName;
        }
    }
    return null;
}

public string? GetSlotValue(SlotSession session, string slotName)
{
    return session.Slots.TryGetValue(slotName, out var slot)
        ? slot.Value?.ToString()
        : null;
}

public bool IsComplete(SlotSession session)
{
    return GetNextUnfilledSlot(session) == null;
}
```

### 5.5 Phase 3: UI コンポーネント実装（2日）

#### 5.5.1 修正ファイル

| ファイル | 修正内容 |
|---------|---------|
| `AutoDealerChatService.cs` | `BuildSlotFillingComponents` 実装（§3.3.1 参照） |
| `AutoDealerChatService.cs` | `BuildConfirmationComponents` 実装（§3.3.2 参照） |
| `AutoDealerChatService.cs` | `GetAvailableVehicles` 実装（DBから車種一覧取得） |
| `Models/AI/AIWindowRequests.cs` | `SendMessageResponse` に `IsFallback` プロパティ追加 |

#### 5.5.2 `GetAvailableVehicles` 実装

```csharp
/// <summary>
/// 試乗可能な車両一覧を取得（キャッシュ付き）
/// </summary>
private List<VehicleInfo> GetAvailableVehicles()
{
    // シンプル実装: vehicles テーブルから取得
    var vehicles = _db.Query<dynamic>(@"
        SELECT vehicle_id, name, manufacturer
        FROM vehicles
        WHERE status = 'available' OR status = 'test_drive'
        ORDER BY manufacturer, name
    ").ToList();

    return vehicles.Select(v => new VehicleInfo(
        VehicleId: v.vehicle_id?.ToString() ?? "",
        DisplayName: $"{v.manufacturer} {v.name}",
        ModelName: v.name?.ToString() ?? ""
    )).ToList();
}

private record VehicleInfo(string VehicleId, string DisplayName, string ModelName);
```

### 5.6 Phase 4: フロントエンド修正（1日）

#### 5.6.1 修正ファイル

| ファイル | 修正内容 |
|---------|---------|
| `ai-chat-widget.js` | コンポーネント送信処理改善（§3.4.2 参照） |
| `ai-chat-components.js` | `onSubmit` シグネチャ変更 (value, label)（§3.4.3 参照） |
| `ai-chat-components.css` | スマートフォン対応レスポンシブ改善 |

#### 5.6.2 レスポンシブ改善ポイント

```css
/* スマートフォン対応 */
@media (max-width: 768px) {
    .aic-carousel-track {
        gap: 8px;
    }
    .aic-card {
        min-width: 160px;  /* 220px → 160px */
        padding: 8px;
    }
    .aic-select-group {
        max-width: 100%;
    }
    .aic-confirm {
        max-width: 100%;
    }
    .aic-confirm-btns {
        flex-direction: column;  /* 縦積み */
    }
}
```

### 5.7 Phase 5: テスト・検証（1日）

#### 5.7.1 テストシナリオ

| # | テストケース | 期待結果 |
|---|-------------|---------|
| 1 | 試乗予約: 車種選択ボタン → 日付選択 → 時間選択 → 名前入力 → 電話入力 → 確認 → 確定 | 各段階でコンポーネント表示、選択後自動送信、確定後DB登録 |
| 2 | 試乗予約中にオフトピック質問（「プリウスの燃費は？」） | LLM フォールバックで回答、Slot-filling は継続 |
| 3 | 日付選択で過去の日付を選択 | バリデーションエラーまたは選択不可 |
| 4 | 確認画面で「いいえ、変更する」を選択 | Slot-filling の先頭に戻る or 変更対象を聞く |
| 5 | Direct API 無効時（CLI フォールバック） | CLI で動作継続（応答時間は増加） |
| 6 | スマートフォン表示 | コンポーネントが画面幅に収まる |

---

## 6. テスト計画

### 6.1 単体テスト

| テストクラス | テスト対象 |
|-------------|-----------|
| `SlotFillingFastFlowTests.cs` | `ProcessSlotFillingFastAsync` の各パス |
| `SlotValueExtractionTests.cs` | `ExtractSlotValuesFromMessageAsync` の正規表現 |
| `OffTopicDetectionTests.cs` | `IsOffTopicQuestion` の精度 |
| `SlotFillingComponentBuilderTests.cs` | `BuildSlotFillingComponents` の出力 |

### 6.2 統合テスト

| テストクラス | テスト対象 |
|-------------|-----------|
| `TestDriveBookingIntegrationTests.cs` | エンドツーエンドの試乗予約フロー |
| `DirectApiFallbackTests.cs` | API 失敗時の CLI フォールバック |

### 6.3 パフォーマンステスト

```csharp
[Benchmark]
public async Task TestDriveBooking_SlotFilling_Fast()
{
    // Slot-filling 中の1往復（LLM なし）
    var sw = Stopwatch.StartNew();
    var response = await _chatService.SendMessageAsync(
        convId, "プリウス");
    sw.Stop();

    Assert.True(sw.ElapsedMilliseconds < 100);  // 100ms 以内
    Assert.Equal("RuleBased", response.AiModel);
    Assert.NotNull(response.Components);
}

[Benchmark]
public async Task TestDriveBooking_FullFlow()
{
    // 試乗予約全体（6往復）
    var sw = Stopwatch.StartNew();

    await SendAndReceive("試乗を予約したい");    // LLM: 1-3秒
    await SendAndReceive("プリウス");            // Rule: < 50ms
    await SendAndReceive("明日");                // Rule: < 50ms
    await SendAndReceive("午前10時");            // Rule: < 50ms
    await SendAndReceive("田中みなみ");          // Rule: < 50ms
    await SendAndReceive("090-1234-5678");       // Rule: < 50ms
    await SendAndReceive("はい");                // Rule: < 200ms

    sw.Stop();
    Assert.True(sw.ElapsedMilliseconds < 10000);  // 全体 10秒以内
}
```

---

## 7. リスクと対策

| リスク | 影響 | 対策 |
|--------|------|------|
| **DashScope API 障害** | 応答不能 | CLI フォールバック自動切り替え |
| **正規表現の抽出漏れ** | スロット未収集 | LLM フォールバック + ユーザーに再入力を促す |
| **コンポーネント未対応ブラウザ** | UI 崩れ | グレースフルデグラデーション（既存 quickReplies にフォールバック） |
| **API コスト増加** | 課金 | `qwen-plus` 使用 + Slot-filling で LLM 呼び出し削減 |
| **セッション消失** | 途中再開不可 | DB永続化（T-05 参照） |

---

## 8. チェックリスト

### Phase 0: 設定確認

- [ ] `appsettings.Development.json` に `UseDirectApi: true` を追加
- [ ] `DASHSCOPE_API_KEY` 環境変数が設定されている
- [ ] `DashScopeApiProvider` が DI コンテナに登録されている

### Phase 1: Direct API 有効化

- [ ] アプリ起動時に API キーが検証される
- [ ] ログに `[DashScope API] 応答成功` が出力される
- [ ] 応答時間が 1-3秒 になっている

### Phase 2: Slot-filling 高速化

- [ ] `ProcessSlotFillingFastAsync` が実装されている
- [ ] `ExtractSlotValuesFromMessageAsync` が車種・日付・時間・氏名・電話を抽出できる
- [ ] `IsOffTopicQuestion` がオフトピック質問を検出できる
- [ ] フォールバック時に LLM が呼ばれる
- [ ] `SlotFillingManager` に `GetNextUnfilledSlot`, `GetSlotValue`, `IsComplete` が追加されている

### Phase 3: UI コンポーネント

- [ ] `BuildSlotFillingComponents` が試乗予約の各段階で正しいコンポーネントを返す
- [ ] `BuildConfirmationComponents` が確認ダイアログを返す
- [ ] `GetAvailableVehicles` が DB から車種一覧を取得できる

### Phase 4: フロントエンド

- [ ] `ai-chat-widget.js` でコンポーネント送信が正しく処理される
- [ ] `ai-chat-components.js` で `onSubmit(value, label)` シグネチャに変更されている
- [ ] スマートフォン表示で崩れていない

### Phase 5: テスト

- [ ] 単体テストが全てパスする
- [ ] 統合テストで試乗予約が完了する
- [ ] パフォーマンステストで目標応答時間を達成している

---

## 付録 A: 設定ファイル例

```json
{
  "AICli": {
    "DefaultTool": "qwen",
    "UseDirectApi": true,
    "TaskTimeoutSeconds": 120,
    "MaxConcurrentTasks": 2,

    "QwenCode": {
      "ApiKey": "${DASHSCOPE_API_KEY}",
      "Model": "qwen-plus",
      "BaseUrl": "https://dashscope.aliyuncs.com"
    },

    "ProcessPool": {
      "EnableDaemonMode": false
    }
  }
}
```

---

## 付録 B: 既存ドキュメントとの関係

| 既存ドキュメント | 関連箇所 |
|-----------------|---------|
| `docs/ai-chat-rich-ui-design.md` | UI コンポーネント設計（本設計で引用） |
| `docs/AI_CLI_常驻进程优化方案.md` | Direct API の詳細設計 |
| `docs/AI高速応答設定ガイド.md` | Direct API 設定手順 |
| `docs/AI_PROCESS_POOL_OPTIMIZATION.md` | プロセスプールの設定 |
| `docs/AUTO-DEALER-AI-IMPROVEMENT-TASKS.md` | T-01（Slot-filling フロー拡張） |
| `docs/auto-dealer-chat-bug-fix-plan.md` | 既存バグ修正状況 |
| `docs/auto-dealer-chat-data-display-fix.md` | コンポーネント表示修正 |

---

*ドキュメント作成: 2026-04-10*
*次回レビュー予定: 実装着手前*
