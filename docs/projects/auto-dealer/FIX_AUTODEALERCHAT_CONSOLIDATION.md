# auto-dealer チャット統合後の不具合 修正仕様書

**作成日**: 2026-04-08  
**対象ブランチ**: main  
**優先度**: 🔴 高（スタッフチャットが正常に動作しない）

---

## 背景

以前は auto-dealer 専用チャットサービスが独立していたが、`BaseChatService` に統合（共通化）したあと、特にスタッフ向けチャットの AI 応答品質が著しく低下した。

---

## 発見された問題一覧

### 🔴 問題 1：スタッフ向けプロンプトに NetYamlForge フレームワーク開発指示が混入

**ファイル**: `NetYamlForge/Services/AI/AutoDealerChatService.cs`  
**行番号**: 49〜55

**現状コード**:
```csharp
// staff の場合
var frameworkPrompt = _skillLoader.GetSystemPrompt();   // ← NetYamlForge フレームワーク開発専用プロンプト
systemPrompt = frameworkPrompt
    .Replace("❌ **auto-dealer-demo の業務データへのアクセス**", "✅ **auto-dealer-demo の業務データへのアクセス**")
    .Replace("顧客情報・車両在庫・販売リードの照会は禁止", "顧客情報・車両在庫・販売リードの照会が可能")
    .Replace("業務ロジックの変更は禁止", "業務ロジックの変更は禁止（読み取り専用）");
```

**問題**:  
`_skillLoader.GetSystemPrompt()` は `skills/_system-prompt.md` を読み込む。このファイルは「Scaffold Entity」「Roslyn アナライザー」「YAML 設定開発」などの **NetYamlForge フレームワーク開発指示** が書かれており、ディーラー業務 AI には全く不要。

スタッフ向けチャットで AI が「Entity YAML を作成できます」「スキャフォールディングが可能です」などフレームワーク開発の回答をしてしまう原因。

**修正内容**:  
フレームワークプロンプトを一切使わず、`_system-prompt-staff.md` のみを使う。

```csharp
// ✅ 修正後
if (isStaff)
{
    var staffPrompt = LoadPromptFromMd("auto-dealer", "_system-prompt-staff.md");
    var toolsDefinition = LoadPromptFromMd("auto-dealer", "_tools-definition.md");
    
    systemPrompt = staffPrompt;
    systemPrompt += Environment.NewLine + Environment.NewLine;
    systemPrompt += "# 🔧 ツール定義" + Environment.NewLine;
    systemPrompt += toolsDefinition;
}
```

---

### 🔴 問題 2：システムプロンプトが CLI に二重送信される

**ファイル**: `NetYamlForge/Services/AI/BaseChatService.cs`  
**行番号**: 113〜128

**現状コード**:
```csharp
var systemPrompt = BuildSystemPrompt(context);

// ① systemPrompt を prompt 本文の先頭に埋め込む（BuildPromptWithHistory 内部）
var prompt = BuildPromptWithHistory(message, history, systemPrompt);
// BuildPromptWithHistory: sb.AppendLine(systemPrompt) ← ここで埋め込まれる

// ② さらに systemPromptOverride として別途 CLI 引数に渡す
var response = await ExecuteWithSystemPromptOverrideAsync(prompt, systemPrompt, cts.Token);
// QwenCodeCLIService: args.Add("--system-prompt"); args.Add(systemPromptOverride);
```

**問題**:  
同一のシステムプロンプトが CLI に **2 回** 届く：
- 1 回目：`--prompt` の本文先頭
- 2 回目：`--system-prompt` 引数

トークン消費が 2 倍になり、LLM が混乱する。また `--system-prompt` で完全置換されるべき内容が `--prompt` の先頭にも残ってしまう。

**修正内容**:  
`BuildPromptWithHistory` からシステムプロンプトの埋め込みを削除し、会話履歴＋ユーザーメッセージのみを返すよう変更。システムプロンプトは `systemPromptOverride` のみで渡す。

```csharp
// ✅ 修正後（BaseChatService.cs の BuildPromptWithHistory）
protected static string BuildPromptWithHistory(
    string message, IEnumerable<(string Role, string Content)> history)  // systemPrompt 引数を削除
{
    var sb = new StringBuilder();
    sb.AppendLine("【会話履歴】");
    foreach (var (role, content) in history.Reverse().Take(10))
    {
        sb.AppendLine($"{(role == "ai" ? "AI" : "ユーザー")}: {content}");
    }
    sb.AppendLine();
    sb.AppendLine("【現在のメッセージ】");
    sb.AppendLine(message);
    return sb.ToString();
}
```

呼び出し側も合わせて修正：
```csharp
// GenerateAiResponseAsync 内
var prompt = BuildPromptWithHistory(message, history);  // systemPrompt を渡さない
var response = await ExecuteWithSystemPromptOverrideAsync(prompt, systemPrompt, cts.Token);
```

---

### 🟡 問題 3：`query_data` ツール呼び出し検出が脆弱

**ファイル**: `NetYamlForge/Services/AI/BaseChatService.cs`  
**行番号**: 354〜387

**現状コード**:
```csharp
protected static ParsedQueryParams? TryParseQueryDataToolCall(string response)
{
    var trimmed = response.Trim();
    if (!trimmed.StartsWith("{")) return null;  // ← { で始まらないと即終了
    ...
}
```

**問題**:  
LLM が前置きテキスト付きで JSON を出力すると検出されない：
```
「在庫を確認します。」
{"tool_call":"query_data","entity":"vehicles",...}
```

また、JSON コードブロック形式（` ```json ... ``` `）も検出できない。

**修正内容**:  
レスポンス内から JSON ブロックを正規表現で抽出してから解析する。

```csharp
protected static ParsedQueryParams? TryParseQueryDataToolCall(string response)
{
    // 1. まず ` ```json...``` ` コードブロックを探す
    var codeBlockMatch = Regex.Match(response, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```");
    string? jsonCandidate = null;
    
    if (codeBlockMatch.Success)
    {
        jsonCandidate = codeBlockMatch.Groups[1].Value.Trim();
    }
    else
    {
        // 2. レスポンス内の最初の { ... } JSON ブロックを探す
        var jsonMatch = Regex.Match(response, @"\{[\s\S]*""tool_call""[\s\S]*\}");
        if (jsonMatch.Success)
        {
            jsonCandidate = jsonMatch.Value.Trim();
        }
        else
        {
            // 3. レスポンス全体が JSON の場合
            var trimmed = response.Trim();
            if (trimmed.StartsWith("{")) jsonCandidate = trimmed;
        }
    }
    
    if (jsonCandidate == null) return null;
    
    try
    {
        using var doc = JsonDocument.Parse(jsonCandidate);
        var root = doc.RootElement;
        
        if (!root.TryGetProperty("tool_call", out var tc) || tc.GetString() != "query_data")
            return null;
        
        // ... 既存のパース処理
    }
    catch (JsonException) { return null; }
}
```

---

### 🟡 問題 4：`_tools-definition.md` の `aggregate`/`template`/`raw_sql` モードが C# パーサーに未対応

**ファイル**: `NetYamlForge/Services/AI/BaseChatService.cs`  
**行番号**: 354〜387（`TryParseQueryDataToolCall`）

**現状の制限**:  
`TryParseQueryDataToolCall` は以下のフィールドしか読まない：
- `entity`, `action`, `filters`, `orderBy`, `top`, `select`

プロンプト（`_tools-definition.md`）で定義している以下は無視される：
- `mode: "aggregate"` → `action: "list"` にフォールバック
- `mode: "template"` → 無視
- `mode: "raw_sql"` → 無視
- `groupBy`, `aggregations` → 無視

**修正内容**:

**短期対応**（プロンプト側の修正）:  
`_tools-definition.md` から `mode: "aggregate"`, `mode: "template"`, `mode: "raw_sql"` の例を削除または最小化し、AI が対応済みの `list` / `count` のみを使うよう誘導する。

```markdown
<!-- 変更前：aggregate 例を削除 -->
<!-- 変更後：以下のシンプルな形式のみ示す -->

### サポートされる action

| action | 説明 |
|--------|------|
| `list` | 一覧表示（デフォルト） |
| `count` | 件数取得 |
```

---

## 修正ファイル一覧

| ファイル | 変更内容 |
|---------|---------|
| `NetYamlForge/Services/AI/AutoDealerChatService.cs` | 問題 1：staff プロンプトからフレームワークプロンプト除去 |
| `NetYamlForge/Services/AI/BaseChatService.cs` | 問題 2：`BuildPromptWithHistory` からシステムプロンプト除去<br>問題 3：`TryParseQueryDataToolCall` を正規表現対応に強化 |
| `NetYamlForge/skills/auto-dealer/_tools-definition.md` | 問題 4：未対応モードの例を削除・シンプル化 |

---

## 修正後の期待動作

### スタッフ向けチャット
- AI がフレームワーク開発の話を一切しなくなる
- `_system-prompt-staff.md` の指示通りに動作する
- データ照会 → 分析レポート形式で返答

### 顧客向けチャット
- 現状から大きな変化なし（問題 1 の影響を受けていなかった）
- `query_data` ツール検出精度が向上（問題 3 の効果）

### 共通
- システムプロンプト二重送信解消 → トークン消費半減・応答速度向上

---

## テスト手順

```bash
# ビルド確認
dotnet build

# ユニットテスト実行
dotnet test --filter "FullyQualifiedName~ChatService"

# 手動テスト（スタッフチャット）
# 1. ログイン → auto-dealer-demo → スタッフチャット
# 2. 「今日連絡すべき顧客は？」と入力
# 3. 期待：分析レポート形式で返答（フレームワーク開発の話が出ないこと）

# 手動テスト（顧客チャット）
# 1. ゲストとして auto-dealer-demo にアクセス
# 2. 「電気自動車を探しています」と入力
# 3. 期待：vehicles テーブルから在庫データを取得して返答
```

---

*作成者: AI アシスタント / 2026-04-08*
