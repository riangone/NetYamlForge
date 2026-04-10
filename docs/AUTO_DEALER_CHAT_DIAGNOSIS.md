# 汽车销售系统聊天记录诊断报告

## 调查日期
2026-04-10

## 调查项目
- **数据库**: `NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db`
- **前端**: `wwwroot/js/dealer-chat-widget.js`
- **后端**: `Services/AI/AutoDealerChatService.cs`

---

## 1. 进程池使用情况 ✅ 已确认使用

### 调用链路
```
AutoDealerChatService.SendMessageAsync()
  → BaseChatService.ExecuteWithSystemPromptOverrideAsync()
    → CLIServiceFactory.TryGetService("qwen")
      → PooledCLIService.ExecuteAsync()
        → DaemonChatServiceFactory.GetService()
          → DaemonChatService.ChatAsync()
            → DaemonProcessInstance (常驻进程)
```

### 关键证据
| 文件 | 行号 | 内容 |
|------|------|------|
| `Program.cs` | 254-262 | 所有 CLI provider 注册为 `PooledCLIService` |
| `PooledCLIService.cs` | 14-35 | 构造函数接收 `DaemonChatServiceFactory` |
| `PooledCLIService.cs` | 46-53 | `ExecuteStreamingAsync` 直接调用 `daemonService.ChatStreamingAsync` |
| `PooledCLIService.cs` | 91-100 | `ExecuteAsync` 调用 `daemonService.ChatAsync` |
| `BaseChatService.cs` | 164-182 | 通过 `_cliFactory.TryGetService(providerOverride)` 获取服务 |

### 结论
**AutoDealerChatService 完全使用了常驻进程池**。每次 AI 响应都会通过 `PooledCLIService` → `DaemonChatService` → `DaemonProcessInstance` 执行，避免每次启动新进程的 2-5 秒开销。

---

## 2. 富组件系统状态

### 2.1 后端 ✅ 完整实现

#### 组件类型（9 种）
| 类型 | 后端 Record | 前端渲染器 | 用途 |
|------|-------------|-----------|------|
| `quick_reply_group` | `QuickReplyGroup` | `renderQuickReplyGroup` | 快捷回复按钮 |
| `single_select` | `SingleSelectGroup` | `renderSingleSelect` | 单选（车种/时间带） |
| `multi_select` | `MultiSelectGroup` | `renderMultiSelect` | 多选 |
| `datetime_picker` | `DateTimePicker` | `renderDateTimePicker` | 日期/时间选择 |
| `range_slider` | `RangeSlider` | `renderRangeSlider` | 范围滑块 |
| `card_carousel` | `CardCarousel` | `renderCardCarousel` | 卡片轮播（车辆列表） |
| `confirm` | `ConfirmPrompt` | `renderConfirm` | 确认对话框 |
| `rating` | `RatingWidget` | `renderRating` | 评分 |
| `text_suggestions` | `TextSuggestions` | `renderTextSuggestions` | 文本建议 |

#### 数据库验证
```sql
-- 总消息数: 545
-- 带组件消息: 12 (2.2%)
SELECT COUNT(*) as total, COUNT(components_json) as with_components FROM ai_messages;
```

#### 组件内容示例
```json
[
  {
    "$type": "card_carousel",
    "type": "card_carousel",
    "title": "検索結果（8件）",
    "items": [
      {
        "subtitle": "¥4280万 · 2024年",
        "badgeLabel": "available",
        "actions": [
          {"label": "詳細", "value": "車両ID の詳細を教えて"},
          {"label": "試乗予約", "value": "車両ID を試乗予約したい"}
        ]
      }
    ]
  }
]
```

#### 序列化配置
```csharp
// BaseChatService.cs line 411-417
componentsJson = JsonSerializer.Serialize(components, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
});
```

**注意**: 由于使用了 `[JsonDerivedType]` 特性，序列化输出包含 `$type` 和 `type` 两个字段。前端使用 `comp.type` 进行 switch，能正确匹配。

### 2.2 前端 ✅ 渲染器完整

#### 文件结构
| 文件 | 路径 | 作用 |
|------|------|------|
| `ai-chat-components.js` | `wwwroot/js/` | 组件渲染引擎（9 种组件） |
| `ai-chat-components.css` | `wwwroot/css/` | 组件样式 |
| `dealer-chat-widget.js` | `wwwroot/js/` | 聊天 Widget（集成组件渲染） |

#### 关键代码路径

**1. 数据恢复** (`dealer-chat-widget.js` line 1975-1987)
```javascript
// 解析 components_json
let components = null;
if (m.componentsJson) {
  try {
    components = JSON.parse(m.componentsJson);
  } catch (e) {
    console.warn('Failed to parse components:', e);
  }
}

const extra = components ? { components: components } : null;
chatHistory.push({ content: m.content, type: type, timestamp: ts, extra: extra });
addMessage(m.content, type, true, ts, extra);
```

**2. 组件渲染** (`dealer-chat-widget.js` line 1871-1882)
```javascript
if (extra?.components?.length && typeof AiChatComponents !== 'undefined') {
  const compEl = AiChatComponents.render(extra.components, (value) => {
    const inputEl = document.getElementById('dc-input-message');
    if (inputEl) {
      inputEl.value = value;
      sendMessage();
    }
  });
  messageEl.appendChild(compEl);
}
```

**3. 内容过滤** (`dealer-chat-widget.js` line 1848-1854)
```javascript
const hasComponents = extra?.components?.length && typeof AiChatComponents !== 'undefined';

if (type === 'assistant') {
  if (hasComponents && content && (content.trim().startsWith('{') || content.trim().startsWith('['))) {
    contentEl.innerHTML = '';  // 清空 JSON 内容，只显示组件
  } else {
    contentEl.innerHTML = renderMarkdown(content);
  }
}
```

### 2.3 组件构建场景

| 场景 | 组件列表 | 代码位置 |
|------|---------|---------|
| **车辆选择** | `SingleSelectGroup` (车种列表) | `AutoDealerChatService.cs` ~line 580 |
| **日期选择** | `DateTimePicker` | `AutoDealerChatService.cs` ~line 590 |
| **时间带选择** | `SingleSelectGroup` (时间带) | `AutoDealerChatService.cs` ~line 600 |
| **预约完成** | `CardCarousel` + `QuickReplyGroup` | `AutoDealerChatService.cs` ~line 490-507 |
| **搜索结果** | `CardCarousel` (车辆列表) | `AutoDealerChatService.cs` ~line 750 |

---

## 3. 可能的问题原因

### 3.1 组件不可见的可能原因

1. **CSS 层级问题**: 组件元素可能被其他元素遮挡或 z-index 不正确
2. **DOM 插入位置**: `messageEl.appendChild(compEl)` 可能插入了但不可见
3. **空内容过滤**: 当 `content` 是 JSON 时被清空，如果组件渲染失败则显示空白

### 3.2 验证步骤

在浏览器控制台运行：
```javascript
// 检查组件渲染器是否加载
console.log(typeof AiChatComponents); // 应该输出 "object"

// 检查组件 CSS 是否加载
console.log(document.querySelector('link[href*="ai-chat-components.css"]'));

// 检查带有组件的消息
fetch('/api/auto-dealer-chat/session/YOUR_SESSION_ID/messages')
  .then(r => r.json())
  .then(messages => {
    const withComponents = messages.filter(m => m.componentsJson);
    console.log('Messages with components:', withComponents.length);
    withComponents.forEach(m => {
      console.log('Components:', JSON.parse(m.componentsJson));
    });
  });
```

### 3.3 建议的改进

1. **添加调试日志**: 在 `addMessage` 函数中输出组件信息
2. **组件渲染失败回退**: 当组件渲染失败时显示原始 JSON 或错误消息
3. **CSS 可见性检查**: 确保 `.aic-components` 和子元素正确显示

---

## 4. 结论

| 项目 | 状态 | 说明 |
|------|------|------|
| **进程池使用** | ✅ 确认 | 完整链路使用常驻进程 |
| **后端组件** | ✅ 完整 | 9 种组件类型 + 数据库存储 |
| **前端渲染器** | ✅ 完整 | 9 种组件渲染 + CSS 样式 |
| **数据完整性** | ✅ 确认 | 12 条消息包含组件数据 |
| **UI 显示** | ⚠️ 需验证 | 代码正确，需运行时调试 |

### 下一步行动

1. 在浏览器中打开汽车销售聊天页面
2. 打开开发者工具 Console
3. 发送触发组件的消息（如"試乗予約したい"）
4. 检查 Console 是否有组件渲染相关日志
5. 检查 DOM 中是否存在 `.aic-components` 元素

---

*报告生成时间: 2026-04-10*
