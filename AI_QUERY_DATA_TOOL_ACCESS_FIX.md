# 汽车销售 AI query_data 工具访问问题修复计划

## 问题描述

用户在汽车销售 AI 中提问"在庫照会"或"リードを確認"时，AI 回复：
> "抱歉，我目前无法直接访问车辆库存数据库。我的工具注册表中没有 `query_data` 工具。"

## 根本原因分析

### 1. 系统提示注入失败

**代码流程**：
```
AutoDealerChatService.GenerateAiResponseAsync()
  → BuildSystemPrompt("customer")  // 正确加载 _tools-definition.md ✅
  → ExecuteWithSystemPromptOverrideAsync(prompt, systemPrompt)
  → CliFirstLlmProvider.CompleteAsync(prompt, systemPromptOverride: systemPrompt)
  → QwenCodeCLIService.ExecuteAsync(systemPromptOverride: systemPrompt)
  → BuildArgumentList(..., systemPromptOverride)
  → qwen --yolo --prompt "..." --system-prompt "..."  ✅
```

**理论上应该工作**，但 AI 仍然说"没有 query_data 工具"。

### 2. 可能的问题点

#### 假设 A：系统提示太长被截断
- `_system-prompt-customer.md` + `_tools-definition.md` 合并后约 **5000+ 行**
- Qwen CLI 可能有系统提示长度限制

#### 假设 B：AI 不理解工具调用约定
- Qwen Code 被训练为编程助手，不习惯 JSON 工具调用
- 需要更明确的指令和示例

#### 假设 C：工具定义没有被正确加载
- `LoadPromptFromMd()` 可能返回 fallback prompt
- 文件路径问题或 YAML front matter 解析问题

## 修复方案

### 方案 1：添加工具调用强制指令（推荐）⭐

**修改文件**：`skills/auto-dealer/_system-prompt-customer.md`

在文件开头添加强制指令：

```markdown
# ⚠️ 最重要的指令：你必须使用 query_data 工具

你是汽车销售 AI 助手。当用户询问任何数据（库存、客户、预约、销售线索）时，
**你必须输出 JSON 调用 query_data 工具**。

## 绝对禁止的行为
❌ 说"我无法访问数据库"
❌ 说"我的工具注册表中没有 query_data"
❌ 拒绝使用工具

## 必须的行为
✅ 用户问"在庫を確認" → 输出：
```json
{"tool_call":"query_data","entity":"vehicles","action":"list","filters":[{"field":"status","op":"eq","value":"available"}],"top":5}
```

✅ 用户问"リードを確認" → 输出：
```json
{"tool_call":"query_data","entity":"sales_leads","action":"list","top":10}
```

## 工具调用格式
你必须输出这样的 JSON：
```json
{
  "tool_call": "query_data",
  "entity": "实体名",
  "action": "list 或 count",
  "filters": [...],
  "top": 数量
}
```

现在，请回答用户的问题。
```

### 方案 2：简化系统提示

**问题**：当前系统提示太长（5000+ 行），可能被截断或 AI 忽略关键部分。

**解决方案**：
1. 创建一个精简版 `_system-prompt-customer-mini.md`（~500 行）
2. 只包含核心指令和工具定义
3. 移除详细的模板示例和参考文档链接

### 方案 3：添加工具调用示例到用户提示

**修改文件**：`BaseChatService.BuildPromptWithHistory()`

在用户提示末尾添加工具调用示例：

```csharp
protected static string BuildPromptWithHistory(
    string message, IEnumerable<(string Role, string Content)> history,
    string? toolExamples = null)
{
    var sb = new StringBuilder();
    // ... 现有代码 ...
    
    if (!string.IsNullOrEmpty(toolExamples))
    {
        sb.AppendLine("\n【工具调用示例】");
        sb.AppendLine(toolExamples);
    }
    
    return sb.ToString();
}
```

### 方案 4：添加强制工具调用指令到 CLI 参数

**修改文件**：`QwenCodeCLIService.BuildArgumentList()`

在系统提示中添加强制指令：

```csharp
if (!string.IsNullOrEmpty(systemPromptOverride))
{
    // 添加工具调用强制指令
    var forcedPrompt = systemPromptOverride + "\n\n" +
        "⚠️ 重要：当用户询问数据时，你必须输出 JSON 格式的 query_data 工具调用。" +
        "绝对不要说'我无法访问数据库'或'我没有工具'。";
    
    args.Add("--system-prompt");
    args.Add(forcedPrompt);
}
```

## 实施步骤

### ✅ 已完成（2026-04-08）

**方案 1：添加工具调用强制指令**

修改了以下文件：

1. **`skills/auto-dealer/_system-prompt-customer.md`** (v3.1 → v3.2)
   - 在文件开头添加了"⚠️ 最重要：ツール呼び出しの強制義務"章节
   - 明确列出禁止行为（❌）和必须行为（✅）
   - 提供具体的工具调用 JSON 示例

2. **`skills/auto-dealer/_system-prompt-staff.md`** (v3.1 → v3.2)
   - 添加了相同的禁止行为和必须行为清单
   - 确保员工 AI 也能正确调用工具

3. **`skills/auto-dealer/_tools-definition.md`** (v1.0 → v1.1)
   - 在文件开头添加"🚨 强制义务：工具调用规则"章节
   - 添加工具调用示例（在庫、リード、顧客数）

### 如果方案 1 不够：方案 2（简化系统提示）
   - 创建精简版系统提示
   - 保留核心指令和工具定义
   - 移除冗余内容

3. **长期优化**：方案 3 + 方案 4
   - 改进 `BuildPromptWithHistory()` 方法
   - 在 CLI 参数中添加工具调用指令
   - 提高工具调用成功率

## 测试计划

### 测试步骤

1. **重启应用程序**（加载新的系统提示文件）
   ```bash
   # 如果正在运行，重启应用
   dotnet run --project NetYamlForge
   ```

2. **打开汽车销售 AI 聊天窗口**
   - 访问：`http://localhost:<port>/auto-dealer-demo/Page/Chat`

3. **测试用例 1：車両在庫照会**
   - 输入：`在庫照会` 或 `在庫を確認`
   - **预期结果**：
     - ✅ AI 输出 JSON 工具调用：`{"tool_call":"query_data","entity":"vehicles","action":"list",...}`
     - ✅ 系统执行查询并返回车辆库存数据
     - ✅ 显示车辆列表、价格、详细信息
     - ❌ **不应该**说"我没有工具"或"无法访问数据库"

4. **测试用例 2：販売リード照会**
   - 输入：`リードを確認` 或 `販売リードを見せて`
   - **预期结果**：
     - ✅ AI 输出 JSON 工具调用：`{"tool_call":"query_data","entity":"sales_leads","action":"list",...}`
     - ✅ 系统执行查询并返回销售线索数据
     - ✅ 显示リード列表、状態、顧客信息
     - ❌ **不应该**说"我无法访问数据库"

5. **测试用例 3：顧客数查询**
   - 输入：`顧客数は？` 或 `何人顧客がいますか？`
   - **预期结果**：
     - ✅ AI 输出 JSON 工具调用：`{"tool_call":"query_data","entity":"customers","action":"count"}`
     - ✅ 系统执行查询并返回顾客数量
     - ✅ 显示具体数字

6. **测试用例 4：予約確認**
   - 输入：`予約を確認` 或 `試乗予約一覧`
   - **预期结果**：
     - ✅ AI 输出 JSON 工具调用：`{"tool_call":"query_data","entity":"service_appointments","action":"list",...}`
     - ✅ 系统执行查询并返回预约数据
     - ✅ 显示预约列表

### 调试技巧

如果测试失败，检查以下内容：

1. **查看日志**：
   ```bash
   tail -f logs/*.log | grep -i "systemPrompt\|query_data\|ツール"
   ```

2. **检查系统提示是否正确加载**：
   - 在日志中搜索 `AI 応答生成開始`
   - 确认 `systemPromptLength` 是否足够长（应该 > 5000 字符）

3. **检查 CLI 调用**：
   - 在日志中搜索 `[CLI执行] 開始`
   - 确认 `--system-prompt` 参数是否正确传递

## 预期结果

修复后，AI 应该：
1. ✅ 正确理解工具定义
2. ✅ 在用户询问数据时输出 JSON 工具调用
3. ✅ 不再回复"我没有工具"的错误消息
4. ✅ 显示实际的数据库查询结果

## 后续优化建议

如果修复后仍有问题，可以考虑：

1. **简化系统提示**：创建精简版提示（~500 行），避免 AI 忽略关键部分
2. **添加工具调用示例到用户提示**：在每次用户消息后附加示例
3. **使用 Few-Shot 学习**：在系统提示中添加多个对话示例
4. **检查 Qwen 模型版本**：确保使用支持工具调用的模型版本
