# Auto-Dealer-Chat 問題修复方案

> **创建日期**: 2026-04-08  
> **优先级**: 🔴 高（问题 1、2 必须立即修复）  
> **影响范围**: `AutoDealerChatService.cs`, `BaseChatService.cs`, `_system-prompt-staff.md`

---

## 📋 问题总览

| # | 问题 | 严重度 | 状态 |
|---|------|--------|------|
| 🔴 1 | スタッフ向けプロンプトにフレームワーク開発指示が混入 | **致命** | 待修复 |
| 🔴 2 | システムプロンプトが二重送信される | **致命** | 待修复 |
| 🟡 3 | `query_data` ツール呼び出し検出が脆弱 | **中等** | 待修复 |
| 🟡 4 | `_tools-definition.md` の定義と C# パーサーの乖離 | **中等** | 待修复 |

---

## 🔴 问题 1：スタッフ向けプロンプトにフレームワーク開発指示が混入

### 问题描述

**文件**: `AutoDealerChatService.cs` 第 44〜55 行

```csharp
if (isStaff)
{
    var frameworkPrompt = _skillLoader.GetSystemPrompt();  // ← 问题所在！
    systemPrompt = frameworkPrompt
        .Replace("❌ **auto-dealer-demo の業務データへのアクセス**", "✅ ...");
    // その後にディーラー業務指示を追記
}
```

**根本原因**：
- `_skillLoader.GetSystemPrompt()` 读取的是 `skills/_system-prompt.md`
- 这是 **NetYamlForge フレームワーク開発専用** 的系统提示
- 包含 "スキャフォールディング"、"Roslyn アナライザー"、"Entity YAML" 等与经销商业务完全无关的开发指示
- 工作人员 AI 会收到混乱的指令：既要写 C# 代码，又要处理销售线索

**影响范围**：
- ✅ **顾客向け**（`context != "staff"`）：直接读取 `_system-prompt-customer.md`，不受影响
- ❌ **スタッフ向け**（`context == "staff"`）：混入框架开发指令，功能损坏

### 修复方案

**目标**：スタッフ向けも直接 `_system-prompt-staff.md` を使う

**修改文件**: `AutoDealerChatService.cs`

**修改前**（第 42〜61 行）：
```csharp
if (isStaff)
{
    var frameworkPrompt = _skillLoader.GetSystemPrompt();
    systemPrompt = frameworkPrompt
        .Replace("❌ **auto-dealer-demo の業務データへのアクセス**", "✅ **auto-dealer-demo の業務データへのアクセス**")
        .Replace("顧客情報・車両在庫・販売リードの照会は禁止", "顧客情報・車両在庫・販売リードの照会が可能")
        .Replace("業務ロジックの変更は禁止", "業務ロジックの変更は禁止（読み取り専用）");

    var staffPrompt = LoadPromptFromMd("auto-dealer", "_system-prompt-staff.md");
    var toolsDefinition = LoadPromptFromMd("auto-dealer", "_tools-definition.md");

    systemPrompt += Environment.NewLine + Environment.NewLine;
    systemPrompt += "# 🤝 自動車販売ディーラー社員向け AI 業務アシスタント" + Environment.NewLine;
    systemPrompt += staffPrompt;
    systemPrompt += Environment.NewLine + Environment.NewLine;
    systemPrompt += "# 🔧 ツール定義" + Environment.NewLine;
    systemPrompt += toolsDefinition;
}
```

**修改后**：
```csharp
if (isStaff)
{
    // ✅ 修正: フレームワーク開発用プロンプトではなく、ディーラー業務専用プロンプトを直接使用
    var staffPrompt = LoadPromptFromMd("auto-dealer", "_system-prompt-staff.md");
    var toolsDefinition = LoadPromptFromMd("auto-dealer", "_tools-definition.md");

    systemPrompt = $"# 🤝 自動車販売ディーラー社員向け AI 業務アシスタント{Environment.NewLine}{Environment.NewLine}";
    systemPrompt += staffPrompt;
    systemPrompt += $"{Environment.NewLine}{Environment.NewLine}# 🔧 ツール定義{Environment.NewLine}";
    systemPrompt += toolsDefinition;
}
```

**验证要点**：
- [ ] スタッフ向けチャットに "スキャフォールディング" "Roslyn" "Entity YAML" などの単語が含まれない
- [ ] `_system-prompt-staff.md` の内容が正しく反映される
- [ ] `query_data` ツール呼び出しが正常に機能する

---

## 🔴 问题 2：システムプロンプトが二重送信される

### 问题描述

**文件**: `BaseChatService.cs` 第 113〜128 行

```csharp
var systemPrompt = BuildSystemPrompt(context);

// ① systemPrompt を prompt の先頭に埋め込む
var prompt = BuildPromptWithHistory(message, history, systemPrompt);
//   → BuildPromptWithHistory の中: sb.AppendLine(systemPrompt) ← 埋め込まれる

// ② さらに systemPromptOverride として別途送信
var response = await ExecuteWithSystemPromptOverrideAsync(prompt, systemPrompt, cts.Token);
```

**根本原因**：
- `BuildPromptWithHistory` 方法内部使用 `sb.AppendLine(systemPrompt)` 将系统提示嵌入到 `prompt` 变量中
- 然后 `ExecuteWithSystemPromptOverrideAsync` 又将同一个 `systemPrompt` 作为 `systemPromptOverride` 参数传递
- CLI 工具（如 Qwen Code）会收到 **同一系统提示的两次**：
  1. 第一次：作为用户提示的一部分（`prompt` 的开头）
  2. 第二次：作为系统提示参数（`--system-prompt`）

**影响**：
- 浪费 Token（系统提示通常很长）
- 可能导致 AI 困惑（重复指令）
- 增加响应延迟

### 修复方案

**目标**：系统提示只通过 `systemPromptOverride` 发送，不嵌入到 `prompt` 中

**修改文件**: `BaseChatService.cs`

#### 修改 1：`BuildPromptWithHistory` 方法

**修改前**（第 330〜342 行）：
```csharp
protected static string BuildPromptWithHistory(
    string message, IEnumerable<(string Role, string Content)> history, string systemPrompt)
{
    var sb = new StringBuilder();
    sb.AppendLine(systemPrompt);  // ← 删除这行
    sb.AppendLine();
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

**修改后**：
```csharp
protected static string BuildPromptWithHistory(
    string message, IEnumerable<(string Role, string Content)> history)
{
    var sb = new StringBuilder();
    // ✅ 修正: systemPrompt は systemPromptOverride として別途送信するため、
    // ここでは埋め込まない
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

#### 修改 2：调用 `BuildPromptWithHistory` 的地方

**修改前**（第 115 行）：
```csharp
var prompt = BuildPromptWithHistory(message, history, systemPrompt);
```

**修改后**：
```csharp
var prompt = BuildPromptWithHistory(message, history);
```

**验证要点**：
- [ ] CLI 收到的 prompt 中不包含系统提示内容
- [ ] `systemPromptOverride` 参数正确传递
- [ ] AI 响应正常（没有因缺少系统提示而行为异常）
- [ ] Token 使用量减少（可通过日志确认）

---

## 🟡 问题 3：`query_data` ツール呼び出し検出が脆弱

### 问题描述

**文件**: `BaseChatService.cs` 第 354〜357 行

```csharp
var trimmed = response.Trim();
if (!trimmed.StartsWith("{")) return null;  // ← { で始まらないと無効
```

**根本原因**：
- 当前实现要求 JSON 必须**从响应文本的第一个字符开始**
- 但 LLM 经常在 JSON 前面添加解释性文本，例如：
  ```
  在庫を確認します。
  {"tool_call":"query_data","entity":"vehicles",...}
  ```
- 这种情况下，工具调用检测会**失败**，AI 返回文本而非执行查询

**影响**：
- AI 本应查询数据库却返回 "我无法访问数据" 等错误回复
- 用户体验严重受损

### 修复方案

**目标**：从响应文本中提取**第一个 JSON 块**，即使前面有解释文本

**修改文件**: `BaseChatService.cs`

**修改前**（第 351〜383 行）：
```csharp
protected static ParsedQueryParams? TryParseQueryDataToolCall(string response)
{
    var trimmed = response.Trim();
    if (!trimmed.StartsWith("{")) return null;

    try
    {
        using var doc = JsonDocument.Parse(trimmed);
        var root = doc.RootElement;

        if (!root.TryGetProperty("tool_call", out var tc) || tc.GetString() != "query_data")
            return null;

        return new ParsedQueryParams
        {
            Entity = root.TryGetProperty("entity", out var e) ? e.GetString() ?? "" : "",
            Action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "list" : "list",
            Filters = root.TryGetProperty("filters", out var f)
                ? JsonSerializer.Deserialize<List<FilterClause>>(f.GetRawText())
                : new List<FilterClause>(),
            OrderBy = root.TryGetProperty("orderBy", out var o)
                ? JsonSerializer.Deserialize<OrderClause>(o.GetRawText())
                : null,
            Top = root.TryGetProperty("top", out var t) ? t.GetInt32() : 20,
            Select = root.TryGetProperty("select", out var s)
                ? JsonSerializer.Deserialize<List<string>>(s.GetRawText())
                : new List<string>()
        };
    }
    catch (JsonException)
    {
        return null;
    }
}
```

**修改后**：
```csharp
protected static ParsedQueryParams? TryParseQueryDataToolCall(string response)
{
    // ✅ 修正: JSON ブロックを正規表現で抽出（前後のテキストを許容）
    var jsonStart = response.IndexOf('{');
    if (jsonStart < 0) return null;

    // 対応する閉じ括弧を探す
    var depth = 0;
    var jsonEnd = -1;
    for (var i = jsonStart; i < response.Length; i++)
    {
        var c = response[i];
        if (c == '{') depth++;
        else if (c == '}')
        {
            depth--;
            if (depth == 0)
            {
                jsonEnd = i;
                break;
            }
        }
        // 文字列リテラル内の括弧をスキップ
        else if (c == '"' && i > jsonStart)
        {
            // 直前の文字がエスケープ文字でないか確認
            var escapeCount = 0;
            var j = i - 1;
            while (j >= jsonStart && response[j] == '\\')
            {
                escapeCount++;
                j--;
            }
            if (escapeCount % 2 == 0)
            {
                // エスケープされていない引用符 → 文字列の区切り
                // 閉じ括弧までスキップ
                var closeQuote = response.IndexOf('"', i + 1);
                if (closeQuote < 0) break;
                i = closeQuote;
            }
        }
    }

    if (jsonEnd < 0) return null;

    var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

    try
    {
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        if (!root.TryGetProperty("tool_call", out var tc) || tc.GetString() != "query_data")
            return null;

        return new ParsedQueryParams
        {
            Entity = root.TryGetProperty("entity", out var e) ? e.GetString() ?? "" : "",
            Action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "list" : "list",
            Filters = root.TryGetProperty("filters", out var f)
                ? JsonSerializer.Deserialize<List<FilterClause>>(f.GetRawText())
                : new List<FilterClause>(),
            OrderBy = root.TryGetProperty("orderBy", out var o)
                ? JsonSerializer.Deserialize<OrderClause>(o.GetRawText())
                : null,
            Top = root.TryGetProperty("top", out var t) ? t.GetInt32() : 20,
            Select = root.TryGetProperty("select", out var s)
                ? JsonSerializer.Deserialize<List<string>>(s.GetRawText())
                : new List<string>()
        };
    }
    catch (JsonException)
    {
        return null;
    }
}
```

**新逻辑说明**：
1. 找到第一个 `{` 字符
2. 使用括号匹配算法找到对应的 `}`（处理嵌套对象）
3. 提取完整的 JSON 字符串
4. 解析 JSON 并验证 `tool_call` 字段

**验证要点**：
- [ ] `{"tool_call":"query_data"...}` 在响应开头时正常工作
- [ ] `在庫を確認します。\n{"tool_call":"query_data"...}` 也能正确提取
- [ ] 多个 JSON 块时只提取第一个
- [ ] 无效 JSON 时返回 null（不抛异常）

---

## 🟡 问题 4：`_tools-definition.md` の定義と C# パーサーの乖離

### 问题描述

**文件**: `_tools-definition.md` 定义了以下参数：
```json
{
  "mode": "structured",
  "action": "aggregate",
  "groupBy": ["brand", "model"],
  "aggregations": [
    { "function": "count", "field": "id", "alias": "total_count" }
  ],
  ...
}
```

但 `TryParseQueryDataToolCall()` 只解析：
- `entity`, `action`, `filters`, `orderBy`, `top`, `select`

**忽略的参数**：
- `mode`（structured/template/raw_sql）
- `groupBy`
- `aggregations`
- `template`
- `templateParams`
- `raw_sql`

**影响**：
- AI 返回 `action: "aggregate"` 时，C# 代码仍按 `list` 处理
- AI 返回 `mode: "template"` 时，模板系统未被调用
- 聚合查询结果不正确

### 修复方案

**短期方案**（推荐先实施）：从 `_tools-definition.md` 中删除 `aggregate` 示例

**修改文件**: `NetYamlForge/skills/auto-dealer/_tools-definition.md`

删除以下内容：
- 第 23 行：`"action": "list|count|aggregate"` → 改为 `"action": "list|count"`
- 第 28〜30 行：删除 `groupBy` 和 `aggregations` 字段定义
- 第 43〜45 行：删除 `action: "aggregate"` 相关的说明行
- 第 120〜137 行：删除 "例 5: 聚合查询" 整个示例
- 第 139〜165 行：删除 "例 6: テンプレート查询" 整个示例
- 第 167〜182 行：删除 "例 7: 原始 SQL 查询" 整个示例

**长期方案**（后续迭代）：实现完整的聚合查询支持

需要在 `ParsedQueryParams` 类中添加：
```csharp
public class ParsedQueryParams
{
    // 现有字段...
    public string? Mode { get; set; }          // "structured" | "template" | "raw_sql"
    public List<string>? GroupBy { get; set; }
    public List<AggregationClause>? Aggregations { get; set; }
    public string? Template { get; set; }
    public Dictionary<string, object>? TemplateParams { get; set; }
    public string? RawSql { get; set; }
    public Dictionary<string, object>? SqlParams { get; set; }
}
```

并实现对应的查询执行逻辑。

**当前建议**：
- ✅ 先删除 `_tools-definition.md` 中的未实现功能示例
- ✅ 保持 AI 行为与 C# 解析器一致
- 📋 后续版本再实现完整的聚合查询支持

---

## 🧪 测试计划

### 测试场景 1：スタッフ向けチャット（问题 1）

```bash
# 启动应用
dotnet run --project NetYamlForge

# 访问 auto-dealer-demo 项目
# 打开スタッフ向けチャット

# 测试查询
提问："今日連絡すべき顧客は？"
预期：
  ✅ AI 调用 query_data 查询 sales_leads
  ✅ 返回分析レポート格式（優先度分類 + 推奨アクション）
  ❌ 不应提到 "スキャフォールディング" 或 "Roslyn"
```

### 测试场景 2：プロンプト二重送信（问题 2）

```bash
# 启用详细日志
# 修改 appsettings.Development.json：
{
  "Logging": {
    "LogLevel": {
      "NetYamlForge.Services.AI": "Debug"
    }
  }
}

# 发送消息并检查日志
# 预期：
#   ✅ prompt 长度明显缩短（不含系统提示）
#   ✅ systemPromptOverride 正确传递
#   ✅ AI 响应正常
```

### 测试场景 3：JSON 提取（问题 3）

```csharp
// 单元测试
[Theory]
[InlineData("{\"tool_call\":\"query_data\",\"entity\":\"vehicles\"}", true)]
[InlineData("在庫を確認します。\n{\"tool_call\":\"query_data\",\"entity\":\"vehicles\"}", true)]
[InlineData("以下の JSON を呼び出します：\n```json\n{\"tool_call\":\"query_data\",\"entity\":\"vehicles\"}\n```", true)]
[InlineData("通常のテキスト応答です", false)]
public void TryParseQueryDataToolCall_ShouldExtractJson(string response, bool shouldParse)
{
    var result = BaseChatService.TryParseQueryDataToolCall(response);
    if (shouldParse)
        Assert.NotNull(result);
    else
        Assert.Null(result);
}
```

### 测试场景 4：聚合查询（问题 4）

```bash
# 删除 aggregate 示例后
提问："ブランド別の在庫数を集計して"
预期：
  ✅ AI 返回 list 格式的查询（而非 aggregate）
  ✅ C# 正确解析并执行
  ✅ 返回车辆列表（AI 自行在文本中汇总）
```

---

## 📝 修改文件清单

| 文件 | 修改类型 | 预计行数 |
|------|---------|---------|
| `AutoDealerChatService.cs` | 重构 `BuildSystemPrompt` | ~10 行 |
| `BaseChatService.cs` | 修复 `BuildPromptWithHistory` + `TryParseQueryDataToolCall` | ~50 行 |
| `_tools-definition.md` | 删除未实现功能示例 | ~60 行删除 |

---

## ⚠️ 风险评估

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| スタッフ向けチャットが一時的にシステムプロンプト不足 | AI 行为异常 | `_system-prompt-staff.md` 已包含完整业务指令 |
| JSON 提取逻辑引入新 bug | 工具调用检测失败 | 添加单元测试覆盖 |
| 删除 aggregate 示例后 AI 功能受限 | 无法做聚合分析 | 短期用 list + AI 分析替代，后续实现完整聚合 |

---

## 🚀 实施顺序

1. **第一步**：修复问题 1（`AutoDealerChatService.cs`）
2. **第二步**：修复问题 2（`BaseChatService.cs` - `BuildPromptWithHistory`）
3. **第三步**：修复问题 3（`BaseChatService.cs` - `TryParseQueryDataToolCall`）
4. **第四步**：修复问题 4（`_tools-definition.md` - 删除未实现示例）
5. **第五步**：运行测试并验证
6. **第六步**：人工测试 auto-dealer-demo チャット

---

*本文档创建者：AI 助手*  
*最后更新：2026-04-08*
