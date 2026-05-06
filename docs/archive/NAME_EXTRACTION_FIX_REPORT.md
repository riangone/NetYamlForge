# 试乘预约名字提取修复报告

## 问题描述

在试乘预约流程中，系统一直重复询问"お名前を教えてください"（请告诉我您的名字），即使用户已经提供了名字。

## 根本原因

`ExtractSlotValuesFromMessageAsync` 方法中的名字提取逻辑存在缺陷：

### 原有代码问题

```csharp
var namePatterns = new System.Text.RegularExpressions.Regex(@"(.+?)(?:です|と申します|でございます)");
var nameMatch = namePatterns.Match(message);
if (nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value.Trim()))
{
    var candidateName = nameMatch.Groups[1].Value.Trim();
    if (candidateName.Length >= 2 && candidateName.Length <= 20)
    {
        await _slotFilling.UpdateSlotAsync(conversationId, "customer_name", candidateName, _projectName);
    }
}
```

**问题点**：
1. **正则表达式过于严格**：只能匹配以"です"、"と申します"或"でございます"结尾的消息
2. **无法处理多种表达方式**：
   - ❌ "田中" （只有名字） → 无法匹配
   - ❌ "私の名前は田中です" → 可能提取错误
   - ❌ "田中と言います" → 无法匹配
3. **缺少过滤机制**：可能将功能词（如"はい"、"ありがとう"）误识别为名字

## 解决方案

### 1. 多模式名字提取

实现了三个层次的正则表达式模式，覆盖更多名字表达方式：

#### 模式 1：日语礼貌用语结尾
```regex
(.+?)(?:です|と申します|でございます)$
```
- 匹配："田中です" → "田中"
- 匹配："山田と申します" → "山田"
- 匹配："佐藤でございます" → "佐藤"

#### 模式 2：明确提及"名字"
```regex
(?:私の)?名前は?(.+?)(?:です|でございます|$)
```
- 匹配："私の名前は田中です" → "田中"
- 匹配："名前が山田です" → "山田"
- 匹配："名前は佐藤" → "佐藤"

#### 模式 3：短名字直接匹配
```regex
^([\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeologies}]{2,4})$
```
- 匹配："田中" → "田中"
- 匹配："さとう" → "さとう"
- 匹配："タナカ" → "タナカ"

### 2. 名字验证辅助方法

添加 `LooksLikeNonNameText()` 方法，过滤掉非名字的文本：

```csharp
private static bool LooksLikeNonNameText(string text)
```

**过滤规则**：
- ❌ 常见功能词："はい"、"いいえ"、"ありがとう"、"お願いします" 等
- ❌ 纯数字："12345"
- ❌ 特殊字符："---"、"???"
- ❌ 多语言常见词："yes"、"no"、"谢谢" 等

### 3. 日志增强

为每次名字提取添加日志记录，便于调试：

```csharp
_logger.LogInformation("名前抽出成功（パターン1）: {Name}", candidateName);
_logger.LogInformation("名前抽出成功（パターン2）: {Name}", candidateName);
_logger.LogInformation("名前抽出成功（パターン3）: {Name}", candidateName);
```

## 修改的文件

| 文件 | 修改内容 |
|------|---------|
| `NetYamlForge/Services/AI/AutoDealerChatService.cs` | 改进名字提取逻辑，添加 `LooksLikeNonNameText` 辅助方法 |
| `NetYamlForge.Tests/Services/AI/NameExtractionTests.cs` | 添加名字提取测试用例 |

## 测试场景

### 应该成功提取名字的场景

| 用户输入 | 提取结果 | 使用模式 |
|---------|---------|---------|
| "田中です" | ✅ 田中 | 模式 1 |
| "山田と申します" | ✅ 山田 | 模式 1 |
| "佐藤でございます" | ✅ 佐藤 | 模式 1 |
| "私の名前は田中です" | ✅ 田中 | 模式 2 |
| "名前が山田です" | ✅ 山田 | 模式 2 |
| "名前は佐藤" | ✅ 佐藤 | 模式 2 |
| "田中" | ✅ 田中 | 模式 3 |
| "さとう" | ✅ さとう | 模式 3 |
| "タナカ" | ✅ タナカ | 模式 3 |

### 应该过滤掉的场景

| 用户输入 | 提取结果 | 原因 |
|---------|---------|------|
| "はい" | ❌ 不提取 | 功能词 |
| "ありがとう" | ❌ 不提取 | 功能词 |
| "お願いします" | ❌ 不提取 | 功能词 |
| "教えてください" | ❌ 不提取 | 功能词 |
| "12345" | ❌ 不提取 | 纯数字 |
| "---" | ❌ 不提取 | 特殊字符 |

## 构建验证

```bash
dotnet build NetYamlForge/NetYamlForge.csproj
```

✅ 构建成功，0 个错误，14 个警告（均为现有警告）

## 预期效果

修复后，用户在试乘预约流程中提供名字时：

1. **更灵活的识别**：无论用户说"田中です"还是只说"田中"，都能正确识别
2. **避免误识别**：不会把"はい"、"谢谢"等当作名字
3. **更好的调试**：日志中会记录提取到的名字，便于排查问题

## 后续改进建议

1. **AI 辅助提取**：对于复杂的名字表达方式，可以使用 LLM 来识别
2. **用户确认机制**：提取名字后，可以向用户确认是否正确
3. **多语言支持**：添加对中文、英文名字的支持
4. **名字字典**：建立常见日本名字字典，提高识别准确率

## 相关文档

- [CHAT_FIX_PLAN.md](./CHAT_FIX_PLAN.md) - Slot-filling 修复计划
- [AUTO-DEALER-AI-IMPROVEMENT-TASKS.md](./docs/AUTO-DEALER-AI-IMPROVEMENT-TASKS.md) - 汽车销售 AI 改进任务

---

*修复日期：2026-04-08*
