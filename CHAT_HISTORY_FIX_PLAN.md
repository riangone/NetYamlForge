# 聊天历史修复计划

## 问题描述

customer1 登录后使用聊天功能时发现两个问题:
1. **AI 回答不是真正的 AI 生成** - 返回的是硬编码的错误消息
2. **聊天历史没有保存** - 刷新页面后看不到历史消息

## 根本原因分析

### 问题 1: AI 响应不是 AI 生成

**原因**: `CliFirstLlmProvider.CompleteAsync` 需要 CLI 工具(Qwen Code、Claude 等):
- ✅ 已安装
- ✅ 已认证 (`tool login`)
- ✅ 能正常执行

**如果任一条件不满足**,系统会返回硬编码的错误消息:
```csharp
// BaseChatService.GenerateAiResponseAsync (第 147 行)
catch (Exception ex)
{
    _logger.LogError(ex, "AI 応答生成エラー：context={Context}", context);
    return (GetTemplateResponse("error"), "error", null, null, null);  // ← 硬编码错误消息
}
```

### 问题 2: 聊天历史没有保存

**原因**: 前端从**错误的 API 端点**获取历史!

**前端代码** (`dealer-chat-widget.js` 第 1562 行):
```javascript
const resp = await fetch(CONFIG.apiBaseUrl + '/history?limit=50&context=' + chatContext);
```

**CONFIG.apiBaseUrl 的值**:
```javascript
CONFIG.apiBaseUrl = (opts.apiBase || '') + '/' + currentProject + '/api/ai';
// 结果: "/auto-dealer-demo/api/ai"
```

**完整 URL**: `/auto-dealer-demo/api/ai/history?context=dealer-customer`

这个端点是 `AIController.GetHistory`,它从 **chat.db** (AI CLI 历史) 读取数据,而不是从**业务数据库** (`ai_messages` 表) 读取!

**正确的端点**应该是:
```
/auto-dealer-demo/api/ai/chat/session/{conversationId}/messages
```

这是 `AutoDealerChatController.GetSessionMessages`,它从业务数据库读取消息。

## 修复方案

### 修复 1: 前端聊天历史获取逻辑

**文件**: `NetYamlForge/wwwroot/js/dealer-chat-widget.js`

**修改 `restoreFromServer` 函数** (从第 1558 行开始):

```javascript
async function restoreFromServer() {
  // ✅ 修复: 使用正确的 API 端点获取业务数据库的消息
  if (!dealerConversationId) {
    // 没有会话 ID,尝试从 sessionStorage 恢复
    restoreFromStorage();
    return;
  }

  try {
    // ✅ 正确的端点: /{project}/api/ai/chat/session/{conversationId}/messages
    const resp = await fetch(CONFIG.chatApiBase + '/session/' + dealerConversationId + '/messages');
    if (!resp.ok) {
      restoreFromStorage();
      return;
    }
    const messages = await resp.json();
    if (!Array.isArray(messages) || messages.length === 0) {
      restoreFromStorage();
      return;
    }

    // 渲染消息
    const container = document.getElementById('dc-messages-container');
    container.innerHTML = '';
    chatHistory = [];
    messages.forEach(function(m) {
      // sender: customer | ai | agent → user | assistant
      const type = (m.sender === 'customer') ? 'user' : 'assistant';
      const ts = m.timestamp || '';
      chatHistory.push({ content: m.content, type: type, timestamp: ts });
      addMessage(m.content, type, true, ts);
    });
    saveHistory();
  } catch (e) {
    console.error('Failed to restore from server:', e);
    restoreFromStorage();
  }
}
```

### 修复 2: 保存消息到服务器

**文件**: `NetYamlForge/wwwroot/js/dealer-chat-widget.js`

**修改 `saveMessageToServer` 函数** (从第 1621 行开始):

```javascript
function saveMessageToServer(content, type) {
  // ✅ 消息已经在 SendMessageAsync 中保存到业务数据库 (ai_messages 表)
  // 这里不需要再次保存,只需要确保 chat.db 也有记录(用于 AI CLI 历史)
  
  // 可选: 如果你还想在 chat.db 中保留一份副本(用于 AI CLI 历史追踪)
  const chatContext = currentMode === 'customer' ? 'dealer-customer' : 'dealer-staff';
  fetch(CONFIG.apiBaseUrl + '/history', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ content: content, type: type, chatContext: chatContext })
  }).catch(function() {});
}
```

### 修复 3: 确保页面加载时恢复会话 ID

**问题**: 如果页面刷新,`dealerConversationId` 会丢失,导致无法获取历史消息。

**解决方案**: 将 `dealerConversationId` 保存到 `sessionStorage`:

```javascript
// 在 startDealerSession 成功后保存
async function startDealerSession() {
  // ... 现有代码 ...
  
  if (sessionResult && sessionResult.conversationId) {
    dealerConversationId = sessionResult.conversationId;
    // ✅ 保存到 sessionStorage
    sessionStorage.setItem('dealer_conversation_id_' + currentMode, dealerConversationId);
    // ... 现有代码 ...
  }
}

// 在 initPanel 中恢复
function initPanel() {
  // ... 现有代码 ...
  
  // ✅ 恢复会话 ID
  dealerConversationId = sessionStorage.getItem('dealer_conversation_id_' + currentMode);
  
  // 然后尝试从服务器恢复历史
  restoreFromServer();
}
```

## 测试计划

### 测试 1: AI 响应

1. 确保 CLI 工具已安装和认证
2. 以 customer1 身份登录
3. 发送消息: "こんにちは"
4. **预期结果**: 收到真正的 AI 回复(不是硬编码的错误消息)

### 测试 2: 聊天历史保存

1. 发送多条消息
2. 检查业务数据库:
   ```bash
   sqlite3 projects/auto-dealer-demo/database/auto-dealer-demo.db
   SELECT * FROM ai_messages WHERE conversation_id = '<conversation_id>' ORDER BY timestamp;
   ```
3. **预期结果**: 所有消息都在 `ai_messages` 表中

### 测试 3: 聊天历史恢复

1. 刷新页面
2. **预期结果**: 历史消息自动显示

## 优先级

1. **P0 (紧急)**: 修复前端历史获取逻辑 (`restoreFromServer`)
2. **P1 (高)**: 保存和恢复会话 ID
3. **P2 (中)**: 确保 CLI 工具配置正确

## 相关文件

| 文件 | 路径 |
|------|------|
| 前端聊天组件 | `NetYamlForge/wwwroot/js/dealer-chat-widget.js` |
| AutoDealer 聊天控制器 | `NetYamlForge/Controllers/Api/AutoDealerChatController.cs` |
| AutoDealer 聊天服务 | `NetYamlForge/Services/AI/AutoDealerChatService.cs` |
| BaseChatService | `NetYamlForge/Services/AI/BaseChatService.cs` |
| CLI LLM Provider | `NetYamlForge/Services/AI/Providers/CliFirstLlmProvider.cs` |
| 聊天历史服务 | `NetYamlForge/Services/AI/ChatHistoryService.cs` |

## 备注

- 业务数据库和 chat.db 是**独立的**,前者用于业务逻辑,后者用于 AI CLI 历史追踪
- `AIController` 是用于**框架级 AI 聊天**(CLI 工具),不是用于 auto-dealer 业务聊天
- `AutoDealerChatController` 是用于**auto-dealer 业务聊天**,消息存储在业务数据库

---

*创建日期: 2026-04-08*
*最后更新: 2026-04-08*
