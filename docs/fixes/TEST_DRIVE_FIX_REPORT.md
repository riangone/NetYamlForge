# 试驾预约 AI 客服修复报告

## 📋 问题描述

Customer2（佐藤花子）在使用 AI 客服进行试驾预约时，系统无法独立完成预约流程。具体表现为：

```
customer: 試乗をしたい          → intent: general ❌
ai:       试乘预约是吧！🚗
customer: そうです。            → intent: general ❌
ai:       好的！试乘预约是吧！🚗
customer: 試乗をしたい          → intent: general ❌
ai:       どの車種の試乗をご希望ですか？
customer: toyota                → intent: general ❌
ai:       TOYOTAですね！承ります。  ← 然后就没有后续了！
```

## 🔍 根本原因分析

### 问题 1: 意图分类器优先级错误

**现象**: 所有消息都被识别为 `general` 意图，而不是 `test_drive_booking`

**原因**:
1. `HybridIntentClassifier` 优先使用 LLM 进行分类
2. LLM 的 prompt 中**没有包含 `test_drive_booking` 意图**
3. LLM 返回 `general_inquiry`，置信度低于阈值
4. 规则匹配被跳过，因为 LLM 已返回结果（即使置信度低）

**代码位置**: `HybridIntentClassifier.cs` 第 31-68 行

### 问题 2: 槽位收集流程不够明确

**现象**: AI 收集了车型信息后，没有继续引导用户填写其他必要信息

**原因**:
1. `BuildSlotStatusMessage` 的指令不够强烈
2. AI 没有严格遵守"只问下一个问题"的规则
3. 缺少详细的日志记录，难以调试

## ✅ 修复方案

### 修复 1: 优化意图分类器优先级（规则优先）

**文件**: `HybridIntentClassifier.cs`

**改动**:
```csharp
// 修改前：LLM 优先
public async Task<IntentResult> ClassifyAsync(...)
{
    // 1. LLM 分析
    if (_config.Intent.LlmEnabled && _llmProvider != null)
    {
        var llmResult = await ClassifyWithLlmAsync(...);
        if (llmResult.Confidence >= threshold)
            return llmResult;
    }
    
    // 2. 规则回退
    var ruleResult = await TryRuleMatchingAsync(...);
    ...
}

// 修改后：规则优先
public async Task<IntentResult> ClassifyAsync(...)
{
    // 1. 首先尝试规则匹配（快速、稳定、可预测）
    var ruleResult = await TryRuleMatchingAsync(message, conversationContext);
    if (ruleResult != null && ruleResult.Confidence >= 0.8)
    {
        _logger.LogInformation("✅ ルールマッチ成功: {Intent}...", ...);
        return ruleResult;
    }

    // 2. LLM 分析（当规则匹配置信度低时）
    if (_config.Intent.LlmEnabled && _llmProvider != null)
    {
        try
        {
            var llmResult = await ClassifyWithLlmAsync(...);
            if (llmResult.Confidence >= _config.Intent.ConfidenceThreshold)
                return llmResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM 分類に失敗...");
        }
    }

    // 3. 规则回退（即使置信度低）
    if (ruleResult != null)
        return ruleResult;

    // 4. 默认回退
    _logger.LogWarning("⚠️ 意図分類失敗...");
    return new IntentResult { Intent = "general_inquiry", ... };
}
```

**效果**: 
- ✅ "試乗" 关键词直接匹配规则，置信度 0.9，立即返回 `test_drive_booking`
- ✅ 不依赖 LLM，速度更快，结果更可预测
- ✅ 添加了详细的日志记录

### 修复 2: 更新 LLM 分类 Prompt

**文件**: `HybridIntentClassifier.cs` - `BuildClassificationPrompt` 方法

**改动**:
```csharp
sb.AppendLine("【利用可能なインテント】");
sb.AppendLine("- greeting: 挨拶");
sb.AppendLine("- estimate_request: 見積もり依頼");
sb.AppendLine("- test_drive_booking: 試乗予約（キーワード：試乗、テストドライブ、運転してみたい）"); // ✅ 新增
sb.AppendLine("- appointment_booking: 予約の申し込み（試乗以外）"); // ✅ 区分
...
sb.AppendLine("【重要】");
sb.AppendLine("- 「試乗」「テストドライブ」「運転してみたい」というキーワードがあったら、必ず test_drive_booking に分類してください");
sb.AppendLine("- 置信度は 0.9 以上に設定してください");
```

**效果**:
- ✅ LLM 知道 `test_drive_booking` 意图的存在
- ✅ 明确告知关键词匹配规则
- ✅ 即使 LLM 被调用，也能正确分类

### 修复 3: 强化槽位收集指令

**文件**: `AutoDealerChatService.cs` - `BuildSlotStatusMessage` 方法

**改动**:
```csharp
if (nextSlot != null)
{
    var remainingSlots = GetRemainingSlotNames(nextSlot);
    sb.AppendLine("🎯 **次のアクション（必須）:**");
    sb.AppendLine($"ユーザーに以下の質問を**そのまま**伝えてください：");
    sb.AppendLine();
    sb.AppendLine($"> **{nextSlot.Prompt}**");
    sb.AppendLine();
    sb.AppendLine($"**重要ルール**:");
    sb.AppendLine($"1. 上記の質問だけをユーザーに伝えてください");
    sb.AppendLine($"2. 他の情報を一緒に聞かないでください");
    sb.AppendLine($"3. まだ收集していない情報: {remainingSlots}");
    sb.AppendLine($"4. ユーザーが他のことを聞いても、まずはこの質問に答えてもらってください");
    sb.AppendLine($"5. 短く丁寧に返信してください");
}
```

**效果**:
- ✅ 明确告知 AI 必须执行的行动
- ✅ 强调 5 条重要规则
- ✅ 显示剩余槽位信息，让 AI 了解整体进度

### 修复 4: 添加详细日志记录

**文件**: `AutoDealerChatService.cs` - `SendMessageAsync` 方法

**改动**:
```csharp
var intentCheck = await _intentClassifier.ClassifyAsync(customerMessage, projectId: _projectName);
_logger.LogInformation("🎯 意図分類結果: Message={Message}, Intent={Intent}, Confidence={Confidence}, Method={Method}", 
    customerMessage, intentCheck.Intent, intentCheck.Confidence, intentCheck.Method);

var newScenario = MapIntentToScenario(intentCheck.Intent);
_logger.LogInformation("🔄 シナリオマップ: Intent={Intent} → Scenario={Scenario}", 
    intentCheck.Intent, newScenario ?? "null");

if (newScenario != null && activeScenario == null)
{
    _logger.LogInformation("🚀 Slot-filling: 新規セッション開始: Scenario={Scenario}, Message={Message}", 
        newScenario, customerMessage);
    await _slotFilling.GetSessionAsync(conversationId, newScenario, _projectName);
    activeScenario = newScenario;
}
else if (newScenario != null && activeScenario != null)
{
    _logger.LogInformation("📝 Slot-filling: 既存セッション継続: Scenario={Scenario}", activeScenario);
}
```

**效果**:
- ✅ 每次意图分类都有详细日志
- ✅ 场景映射清晰可见
- ✅ 会话创建/续接状态明确记录

## 🧪 测试验证

### 意图分类器测试
```bash
dotnet test --filter "FullyQualifiedName~HybridIntentClassifier"
```

**结果**: ✅ 16/16 测试通过

包括：
- ✅ `試乗を予約したい` → `test_drive_booking`
- ✅ `実際に乗ってみたいです` → `test_drive_booking`
- ✅ `テストドライブをお願いします` → `test_drive_booking`
- ✅ `試乗したい` → `test_drive_booking`
- ✅ `試乗予約` → `test_drive_booking`
- ✅ `ランドクルーザーを試乗したい` → `test_drive_booking`

### 试驾预约集成测试
```bash
dotnet test --filter "FullyQualifiedName~AutoDealerTestDriveIntegrationTests"
```

**结果**: ✅ 4/4 测试通过

包括：
- ✅ 完整试驾预约流程（5 个槽位全部收集）
- ✅ 槽位自动推进流程
- ✅ Tool 验证流程
- ✅ 低置信度 ESCALATE 流程

## 📊 修复前后对比

| 项目 | 修复前 | 修复后 |
|------|--------|--------|
| **意图识别** | `general` ❌ | `test_drive_booking` ✅ |
| **识别方法** | LLM（慢且不准） | 规则匹配（快且准） |
| **槽位收集** | 中断 | 完整收集 5/5 ✅ |
| **AI 引导** | 无后续 | 主动引导用户 ✅ |
| **日志记录** | 缺失 | 详细完整 ✅ |
| **测试通过率** | 部分通过 | 100% (20/20) ✅ |

## 🎯 预期效果

修复后，customer2 的试驾预约流程应该如下：

```
customer: 試乗をしたい
→ 意图识别: test_drive_booking ✅
→ 创建 Slot-filling 会话 ✅
ai: どの車種の試乗をご希望ですか？

customer: toyota
→ 槽位提取: vehicle_model = "Toyota" ✅
→ 检查: 还需 4 个槽位 ✅
ai: ご希望の日付を教えてください（例：明日、来週月曜日）

customer: 明日
→ 槽位提取: preferred_date = "明日" ✅
ai: ご希望の時間帯を教えてください（例：午前 10 時、午後 2 時）

customer: 午前 10 時
→ 槽位提取: preferred_time = "午前 10 時" ✅
ai: お名前を教えてください

customer: 佐藤
→ 槽位提取: customer_name = "佐藤" ✅
ai: ご連絡先電話番号を教えてください

customer: 090-2345-6789
→ 槽位提取: customer_phone = "090-2345-6789" ✅
→ 所有槽位收集完成 ✅
→ 调用 CompleteTestDriveBookingAsync ✅

ai: 試乗予約を承りました！ ✅

    ご予約内容:
    - 車種: Toyota
    - 希望日: 明日
    - 時間: 午前 10 時
    - お名前: 佐藤
    - 電話番号: 090-2345-6789

    予約番号: APT-xxxxx
    ...
```

## 📝 修改文件清单

1. ✅ `NetYamlForge/Services/AI/HybridIntentClassifier.cs`
   - 修改 `ClassifyAsync` 方法：规则匹配优先
   - 修改 `BuildClassificationPrompt` 方法：添加 `test_drive_booking` 意图

2. ✅ `NetYamlForge/Services/AI/AutoDealerChatService.cs`
   - 修改 `BuildSlotStatusMessage` 方法：强化 AI 指令
   - 添加 `GetRemainingSlotNames` 辅助方法
   - 增强 `SendMessageAsync` 方法的日志记录

## 🚀 后续建议

1. **监控日志**: 部署后监控意图分类日志，确保规则匹配正常工作
2. **用户测试**: 邀请 customer2 重新测试试驾预约流程
3. **扩展规则**: 如果发现其他意图识别问题，可以添加更多规则
4. **性能监控**: 对比修复前后的响应时间和成功率

## 📅 修复时间

- **修复日期**: 2026-04-09
- **修复人员**: AI Assistant
- **测试状态**: ✅ 全部通过

---

**总结**: 通过修复意图分类器优先级和优化槽位收集流程，AI 客服现在能够独立完成完整的试驾预约业务，无需人工干预。
