# AI 驱动的信息提取改进方案

## 当前问题

目前 `ExtractSlotValuesFromMessageAsync` 方法使用了大量硬编码的正则表达式和字典来提取信息：

- ❌ ~200 行硬编码规则
- ❌ 难以维护与扩展
- ❌ 只能处理预定模式
- ❌ 对自然语言理解能力差

## 改进方案：使用 AI 统一提取

### 方案 1：完全 AI 提取（推荐）

将 `ExtractSlotValuesFromMessageAsync` 方法简化为约 50 行：

```csharp
private async Task ExtractSlotValuesFromMessageAsync(string conversationId, string message, string scenario)
{
    if (_slotFilling == null || _llmProvider == null) return;

    try
    {
        // 根据场景定义要提取的槽位
        var slotsToExtract = scenario switch
        {
            "test_drive" => "vehicle_model, preferred_date, preferred_time, customer_name, customer_phone",
            "estimate" => "vehicle_model, grade, budget, customer_name, customer_phone",
            "appointment_service" => "service_type, preferred_date, preferred_time, customer_name, customer_phone",
            "trade_in" => "vehicle_model, vehicle_year, mileage, customer_name, customer_phone",
            _ => "customer_name, customer_phone"
        };

        // 使用 AI 提取
        var prompt = $@"从以下消息中提取这些槽位值，返回 JSON 格式：
槽位: {slotsToExtract}
消息: {message}

JSON 格式:
{{""vehicle_model"": ""..."", ""preferred_date"": ""..."", ""customer_name"": ""..."", ...}}
只返回 JSON，不要其他解释。";

        var response = await _llmProvider.CompleteAsync(prompt, temperature: 0.1f);
        var extracted = JsonSerializer.Deserialize<Dictionary<string, string>>(response);
        
        // 更新槽位
        if (extracted != null)
        {
            foreach (var (key, value) in extracted)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    await _slotFilling.UpdateSlotAsync(conversationId, key, value, _projectName);
                }
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "AI 提取失败");
        // 失败时保持现有槽位不变
    }
}
```

**优势**：
- ✅ 代码量减少 75%（200 行 → 50 行）
- ✅ 自动理解各种表达方式
- ✅ 支持多语言（日语、中文、英语等）
- ✅ 更容易添加新槽位
- ✅ 更好的容错能力

### 方案 2：混合提取（保守方案）

保留简单的正则提取作为快速路径，AI 作为补充：

```csharp
private async Task ExtractSlotValuesFromMessageAsync(string conversationId, string message, string scenario)
{
    // 1. 快速路径：简单正则（日期、电话）
    await ExtractWithRegex(message, "preferred_date", @"(\d{4})[-/](\d{1,2})[-/](\d{1,2})");
    await ExtractWithRegex(message, "customer_phone", @"(\d{2,4}-\d{1,4}-\d{4})");
    
    // 2. AI 路径：名字、车种等复杂提取
    await ExtractWithAI(conversationId, message, scenario);
}
```

### 方案 3：系统提示词中整合提取

在 LLM 生成回复时，同时输出提取的槽位信息：

```csharp
// 在 GenerateAiResponseAsync 中
var systemPrompt = BuildSystemPrompt();
systemPrompt += @"\n\n## 信息提取要求
同时从用户消息中提取以下信息并以 JSON 格式输出：
- customer_name: 用户名字
- vehicle_model: 车辆型号
- preferred_date: 偏好日期
- preferred_time: 偏好时间

输出格式：
<thinking>
提取的信息: {""customer_name"": ""..."", ...}
</thinking>
你的回复...
";
```

## 实现步骤

### Step 1: 修改 ExtractSlotValuesFromMessageAsync

**文件**: `NetYamlForge/Services/AI/AutoDealerChatService.cs`

**位置**: 第 503-720 行

**操作**: 删除所有硬编码的正则表达式逻辑，替换为 AI 调用

### Step 2: 删除 LooksLikeNonNameText 方法

**位置**: 第 680-720 行

**原因**: AI 已经能理解什么是名字，不再需要过滤列表

### Step 3: 测试验证

```bash
# 测试各种表达方式
"田中です" → 应该提取 customer_name = "田中"
"私の名前は山田です" → customer_name = "山田"
"明日の午後 10 時" → preferred_date = "明日", preferred_time = "午後 10 時"
"プリウスの試乗をしたい" → vehicle_model = "プリウス"
```

### Step 4: 性能优化建议

**问题**: 每次消息都调用 AI 可能会慢

**解决方案**:
1. **缓存提取结果** - 同样的消息不重复提取
2. **异步提取** - 不阻塞主流程
3. **批量提取** - 多个槽位一次调用完成
4. **降级策略** - AI 失败时使用简单正则作为回退

## 预期效果

### 代码量对比

| 项目 | 当前 | 改进后 | 减少 |
|------|------|--------|------|
| 代码行数 | ~220 行 | ~50 行 | 77% ↓ |
| 正则表达式 | 10+ 个 | 0-2 个 | 80%+ ↓ |
| 硬编码字典 | 4 个 | 0 个 | 100% ↓ |
| 支持的语言 | 日语 | 多语言 | 扩展 |

### 功能对比

| 场景 | 当前 | 改进后 |
|------|------|--------|
| "田中です" | ✅ 可以 | ✅ 可以 |
| "私の名前は田中です" | ⚠️ 可能 | ✅ 可以 |
| "I'm Tanaka" | ❌ 不行 | ✅ 可以 |
| "我是田中" | ❌ 不行 | ✅ 可以 |
| "田中と申します" | ✅ 可以 | ✅ 可以 |
| "明天下午" | ⚠️ 部分 | ✅ 可以 |

## 成本估算

**AI 调用成本**（假设使用 OpenAI GPT-4o-mini）：
- 每次提取约 100 tokens
- 成本约 $0.0001/次
- 每月 10000 次对话 ≈ $1/月

**性能影响**：
- 额外延迟：~200-500ms（AI 响应时间）
- 可以通过异步执行和缓存优化

## 建议

对于 **auto-dealer-demo** 项目，我推荐：

1. **短期**：采用方案 1（完全 AI 提取），快速减少代码
2. **中期**：添加缓存和降级策略
3. **长期**：考虑系统提示词整合（方案 3）

## 相关文件

- `NetYamlForge/Services/AI/AutoDealerChatService.cs` - 主要修改
- `NetYamlForge.Tests/Services/AI/NameExtractionTests.cs` - 测试用例

---

*创建日期：2026-04-08*
*状态：等待实施*
