# auto-dealer チャット バグ修正計画

> 作成日：2026-04-08  
> 担当：AI アシスタント  
> 優先度：🔴 高（チャット機能の正常動作に影響）

---

## 背景・経緯

`AutoDealerChatService` を `BaseChatService` に統合した際、いくつかの問題が発生した。
コードレビューの結果、以下の通り **4 つの問題のうち 2 つはすでに修正済み**、残り 2 つが現在も残存している。

---

## ✅ 修正済み問題（確認済み）

### 修正済み 問題 1：スタッフ向けプロンプトへのフレームワーク開発指示混入

**状況**：修正済み  
`AutoDealerChatService.BuildSystemPrompt()` は現在、スタッフ向けには `_system-prompt-staff.md` を、顧客向けには `_system-prompt-customer.md` を直接読み込む。`SkillLoader.GetSystemPrompt()` は呼ばれていない。

### 修正済み 問題 2：システムプロンプトの二重送信

**状況**：修正済み  
`BaseChatService.BuildPromptWithHistory()` はシステムプロンプトを埋め込まない。システムプロンプトは `ExecuteWithSystemPromptOverrideAsync()` で `systemPromptOverride` として 1 回だけ渡される。

### 修正済み 問題 3：JSON 抽出の脆弱性

**状況**：修正済み  
`TryParseQueryDataToolCall()` は正規表現ベースの 3 段階抽出ロジックを持つ：
1. ` ```json ... ``` ` コードブロック
2. `"tool_call"` を含む JSON（ブラケットマッチング）
3. 最初の `{` からのフォールバック

### 修正済み 問題 4：`mode: "aggregate"` 乖離

**状況**：修正済み  
`_tools-definition.md` から `mode: "aggregate"` が削除され、`action: "list|count"` に統一されている。

---

## ✅ 修正済み（追加確認済み）

### 問題 A：`_tools-definition.md` の JSON 例に `tool_call` フィールドが欠落

**状況**：修正済み（確認日：2026-04-08）  
`_tools-definition.md` の全 JSON 例（メイン形式・例 1〜4）に `"tool_call": "query_data"` が含まれることを確認。

---

### 問題 B：システムプロンプト末尾のクイックリファレンス例に `tool_call` 欠落

**状況**：修正済み（2026-04-08）  
- `_system-prompt-staff.md`：3 件の JSON 例すべてに `tool_call` 確認済み
- `_system-prompt-customer.md`：クイックリファレンス「電気自動車の在庫を取得」例に `"tool_call": "query_data"` を追加

---

### 問題 C：`create_appointment_request` ツールのバックエンド未実装

**深刻度**：🟡 中  
**影響**：顧客が試乗予約を依頼しても、AI が予約作成 JSON を生成するが C# 側で処理されない

#### 現在の状態

プロンプトに `create_appointment_request` ツールが定義されているが、`BaseChatService.TryParseQueryDataToolCall()` は `query_data` のみを処理し、`create_appointment_request` はシレントに無視される。

#### 修正方針

1. `BaseChatService` に `TryParseAppointmentToolCall()` メソッドを追加
2. `ProcessAiResponseAsync()` で `query_data` の後に `create_appointment_request` も検出
3. `AutoDealerChatService` で `HandleAppointmentToolCallAsync()` を実装（`service_appointments` テーブルへの INSERT）
4. または：短期対応として顧客向けプロンプトから `create_appointment_request` 例を削除し、テキストで対応

---

## 修正対象ファイル一覧

| ファイル | 問題 | 状態 |
|---------|------|------|
| `NetYamlForge/skills/auto-dealer/_tools-definition.md` | A | ✅ 修正済み |
| `NetYamlForge/skills/auto-dealer/_system-prompt-staff.md` | B | ✅ 修正済み |
| `NetYamlForge/skills/auto-dealer/_system-prompt-customer.md` | B | ✅ 修正済み（2026-04-08） |
| `NetYamlForge/Services/AI/BaseChatService.cs` | C | ⏳ 後日対応（`create_appointment_request` バックエンド実装） |

---

## 残存タスク

1. **後日対応**：問題 C（`create_appointment_request` バックエンド）→ 機能追加

---

## テスト確認事項

修正後に以下を確認する：

- [ ] スタッフチャット：「今日のリードを見せて」→ `query_data` が正常に発動し、`sales_leads` データが返る
- [ ] 顧客チャット：「電気自動車を見せて」→ `query_data` が `vehicles` を検索して返す
- [ ] スタッフチャット：「VIP 顧客は何人？」→ `action: "count"` で件数が返る
- [ ] JSON 解析ログ：`query_data ツール呼び出しを検出` のログが出力される
- [ ] `dotnet build` でビルドエラーなし

---

*ドキュメント作成：2026-04-08*
