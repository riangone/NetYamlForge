# AI 替代硬编码机会分析报告

## 📊 概述

基于对整个代码库的扫描，发现了 **多个可以使用 AI 替代硬编码的地方**。以下是按优先级排序的详细分析。

---

## 🔥 高优先级（强烈推荐）

### 1. ✅ HybridIntentClassifier.cs - 实体提取逻辑

**文件**: `NetYamlForge/Services/AI/HybridIntentClassifier.cs`  
**位置**: 第 230-480 行  
**当前代码**: ~250 行硬编码

#### 硬编码内容

| 类型 | 行数 | 示例 |
|------|------|------|
| **车种字典** | ~30 行 | トヨタ: [カローラ, クラウン, ...], ホンダ: [...] |
| **车辆类型字典** | ~15 行 | セダン, SUV, ミニバン, ... |
| **服务类型字典** | ~10 行 | 車検，点検，整備，... |
| **用途字典** | ~8 行 | 通勤，通学，家族，... |
| **日期正则** | ~5 行 | 明日，今日，来週，... |
| **时间正则** | ~10 行 | \d{1,2}時，午前\d{1,2}時，... |
| **预算正则** | ~15 行 | \d{1,3}(?:,\d{3})*万円，... |
| **颜色列表** | ~5 行 | 白，黒，銀，グレー，... |
| **地区列表** | ~5 行 | 東京，神奈川，大阪，... |

#### 建议方案

```csharp
// 当前：~250 行硬编码
var entities = ExtractEntities(message, intent);

// 改进后：~30 行 AI 调用
private async Task<Dictionary<string, string>> ExtractEntitiesWithAI(string message, string intent)
{
    var prompt = $@"从以下消息中提取实体信息，返回 JSON 格式：
消息: {message}
意图: {intent}

需要提取的实体类型：
- 车辆相关：vehicle_brand, vehicle_model, vehicle_type, vehicle_condition, vehicle_color
- 价格相关：budget_amount, budget_type, monthly_payment, down_payment
- 时间相关：preferred_date, preferred_time, preferred_period
- 服务相关：service_type
- 客户相关：customer_type, vehicle_use, location, is_first_purchase
- 支付相关：payment_method, has_trade_in

只返回 JSON 格式，找不到的实体为 null";

    var response = await _llmProvider.CompleteAsync(prompt, CancellationToken.None);
    return JsonSerializer.Deserialize<Dictionary<string, string>>(response);
}
```

#### 预期收益

- **代码减少**: ~250 行 → ~30 行 (88% ↓)
- **多语言支持**: 日语、中文、英语等
- **智能理解**: "我想买一辆 Toyota Camry" → 自动识别品牌+型号
- **易于扩展**: 添加新实体类型只需修改提示词

---

### 2. ✅ DashboardController.cs - 聚合查询生成

**文件**: `NetYamlForge/Controllers/DashboardController.cs`  
**位置**: 第 140-262 行  
**当前代码**: ~120 行 switch-case

#### 硬编码内容

```csharp
// 当前：硬编码聚合函数转换
string sql = stat.Aggregate.ToLowerInvariant() switch
{
    "count" => $"COUNT({stat.Field})",
    "sum" => $"SUM({stat.Field})",
    "avg" => $"AVG({stat.Field})",
    "max" => $"MAX({stat.Field})",
    "min" => $"MIN({stat.Field})",
    _ => $"COUNT({stat.Field})"
};

// 硬编码排序逻辑
var orderCol = (chart.OrderBy?.ToLowerInvariant() == "label") ? "label" : "value";
var orderDir = (chart.OrderDir?.ToLowerInvariant() == "asc") ? "ASC" : "DESC";
```

#### 建议方案

虽然这部分代码不长，但可以考虑用 AI 来：
- **自然语言生成 SQL**：用户说"统计每个状态的订单数量" → AI 生成 SQL
- **智能聚合选择**：AI 根据字段类型自动选择合适的聚合函数

---

## 🟡 中优先级（可以考虑）

### 3. SystemDbTestUserSeeder.cs - 角色判断逻辑

**文件**: `NetYamlForge/Data/Seeders/SystemDbTestUserSeeder.cs`  
**位置**: 第 147-158 行  
**当前代码**: ~12 行硬编码

#### 硬编码内容

```csharp
// 当前：硬编码用户名到角色映射
if (userName.Contains("admin")) return "admin";
if (userName.Contains("operator")) return "operator";
if (userName.Contains("sales")) return "sales_rep";
if (userName.Contains("manager")) return "sales_manager";
if (userName.Contains("service")) return "service_staff";
if (userName.Contains("customer")) return "customer";
// ... 更多规则
```

#### 建议方案

```csharp
// 改进后：AI 推断角色
private async Task<string> InferRoleFromUserName(string userName)
{
    var prompt = $@"根据用户名推断其角色。
用户名: {userName}

可选角色：admin, operator, sales_rep, sales_manager, service_staff, customer, executive, vendor, logistics, finance, insurance

只返回角色名称，不要其他解释。";

    return await _llmProvider.CompleteAsync(prompt, CancellationToken.None);
}
```

#### 预期收益

- **代码减少**: 12 行 → 8 行（但更智能）
- **智能推断**: "john_doe_sales" → "sales_rep", "admin_user" → "admin"
- **多语言**: 支持中文、日文用户名

---

### 4. NaturalLanguageQuery.cs - 时间范围解析

**文件**: `NetYamlForge/Models/AI/NaturalLanguageQuery.cs`  
**位置**: 第 235 行附近  
**当前代码**: switch-case

#### 硬编码内容

```csharp
// 当前：硬编码时间范围解析
return range.ToLower() switch
{
    "today" => DateTime.Today,
    "yesterday" => DateTime.Today.AddDays(-1),
    "this_week" => GetStartOfWeek(),
    "last_week" => GetStartOfWeek().AddDays(-7),
    "this_month" => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
    // ... 更多规则
};
```

#### 建议方案

```csharp
// 改进后：AI 解析自然语言时间
private async Task<DateTime> ParseNaturalLanguageDate(string expression)
{
    var prompt = $@"将自然语言时间表达式转换为具体日期。
表达式: {expression}
参考日期: {DateTime.Today:yyyy-MM-dd}

返回 ISO 格式日期（yyyy-MM-dd），不要其他解释。";

    var response = await _llmProvider.CompleteAsync(prompt, CancellationToken.None);
    return DateTime.Parse(response.Trim());
}
```

#### 预期收益

- **智能理解**: "上周三"、"下个月第一个周一"、"国庆节后第一天"
- **多语言**: "上周"、"last week"、"先週"
- **相对日期**: "3 天后"、"两周前"

---

### 5. FilterValueParser.cs - 过滤器解析

**文件**: `NetYamlForge/Services/FilterValueParser.cs`  
**位置**: 全文  
**当前代码**: ~60 行

#### 当前逻辑

```csharp
// 简单的键值对解析
var filters = new Dictionary<string, string?>();
foreach (var key in query.Keys)
{
    filters[key] = query[key];
}
```

#### 建议方案

用 AI 解析复杂过滤条件：
- **用户输入**: "状态是活跃的且金额大于 1000 的订单"
- **AI 输出**: `{"status": "active", "amount_gt": "1000"}`

---

## 🟢 低优先级（可选优化）

### 6. ProjectTemplateScaffolder.cs - YAML 解析逻辑

**文件**: `NetYamlForge/Services/Cli/ProjectTemplateScaffolder.cs`  
**位置**: 第 468-557 行  
**当前代码**: ~90 行正则表达式

#### 硬编码内容

多个正则表达式用于解析 YAML 文件结构：
```csharp
var inlineMatch = Regex.Match(line, @"label:\s*['""]?(?<label>[^'"",]+)['""]?.*labelKey:\s*['""]?(?<key>[^'""]+)['""]?");
var labelMatch = Regex.Match(line, @"^\s*-?\s*label:\s*['""]?(?<label>[^'""]+)['""]?\s*$");
// ... 更多正则
```

#### 建议

由于这是 CLI 脚手架工具，对性能要求高，**建议保持现状**。但可以考虑：
- 用 YAML 解析库替代正则
- 或用 AI 理解 YAML 结构（但可能过度设计）

---

### 7. EmailChannelService.cs - 邮箱提取

**文件**: `NetYamlForge/Services/AI/EmailChannelService.cs`  
**位置**: 第 337 行  
**当前代码**: 1 行正则

```csharp
var match = Regex.Match(fromHeader, @"<([^>]+)>");
```

#### 建议

这个场景太简单，**不建议用 AI**。正则已经足够。

---

## 📈 总结对比

| 项目 | 当前代码 | 改进后 | 减少 | 优先级 |
|------|---------|--------|------|--------|
| **HybridIntentClassifier 实体提取** | ~250 行 | ~30 行 | 88% ↓ | 🔥 高 |
| **AutoDealerChatService 槽位提取** | ~200 行 | ~60 行 | 70% ↓ | ✅ 已完成 |
| **DashboardController 聚合逻辑** | ~120 行 | ~40 行 | 67% ↓ | 🟡 中 |
| **SystemDbTestUserSeeder 角色推断** | ~12 行 | ~8 行 | 33% ↓ | 🟡 中 |
| **NaturalLanguageQuery 时间解析** | ~20 行 | ~10 行 | 50% ↓ | 🟡 中 |
| **FilterValueParser 过滤器解析** | ~60 行 | ~20 行 | 67% ↓ | 🟡 中 |
| **ProjectTemplateScaffolder YAML 解析** | ~90 行 | - | - | 🟢 低 |
| **EmailChannelService 邮箱提取** | 1 行 | - | - | 🟢 低 |

---

## 🎯 推荐实施顺序

### 第一阶段（已完成）
- ✅ AutoDealerChatService 槽位提取

### 第二阶段（推荐下一步）
1. **HybridIntentClassifier 实体提取** - 最大收益（250 行 → 30 行）
2. **NaturalLanguageQuery 时间解析** - 提升用户体验

### 第三阶段（可选）
3. **FilterValueParser 过滤器解析** - 支持自然语言过滤
4. **SystemDbTestUserSeeder 角色推断** - 智能化

### 第四阶段（按需）
5. **DashboardController 聚合逻辑** - 自然语言生成 SQL

---

## 💡 AI 替代硬编码的优势

### 代码质量
- ✅ 代码量减少 60-90%
- ✅ 更易维护和理解
- ✅ 减少 bug 和边缘情况

### 功能增强
- ✅ 多语言支持（日语、中文、英语等）
- ✅ 智能理解各种表达方式
- ✅ 自动处理边缘情况

### 扩展性
- ✅ 添加新规则只需修改提示词
- ✅ 无需编写复杂的正则表达式
- ✅ 易于适配新场景

---

## ⚠️ 注意事项

### 性能影响
- **AI 调用延迟**: ~200-500ms
- **适用场景**: 用户交互、低频操作
- **不适用**: 高频循环、实时处理

### 成本估算
- **GPT-4o-mini**: ~$0.0001/次
- **月 10000 次**: ~$1/月
- **可接受范围**: 用户交互场景

### 降级策略
- AI 失败时提供默认值
- 保留简单的正则作为回退
- 记录详细日志便于调试

---

## 📝 实施模板

对于任何硬编码的字典/规则/正则，都可以使用以下模式：

```csharp
// 1. 定义要提取的信息
var slotsToExtract = "field1, field2, field3";

// 2. 构建 AI 提示词
var prompt = $@"从以下消息中提取信息，返回 JSON：
消息: {message}
字段: {slotsToExtract}

JSON 格式：
{{""field1"": ""..."", ""field2"": ""..."", ...}}
只返回 JSON，不要其他解释。";

// 3. 调用 AI
var response = await _llmProvider.CompleteAsync(prompt, CancellationToken.None);

// 4. 解析结果
var extracted = JsonSerializer.Deserialize<Dictionary<string, string>>(response);

// 5. 使用结果
foreach (var (key, value) in extracted)
{
    if (!string.IsNullOrWhiteSpace(value))
    {
        // 处理提取的信息
    }
}
```

---

*分析日期：2026-04-08*  
*分析工具：AI Assistant*  
*状态：📋 待实施*
