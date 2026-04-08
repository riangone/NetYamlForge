# 聊天历史修复测试指南

## 修复内容

### 修改的文件
- `NetYamlForge/wwwroot/js/dealer-chat-widget.js`

### 修改内容
修改了 `restoreFromServer()` 函数,优先使用**业务 API** (`/api/ai/chat/session/{conversationId}/messages`) 而不是 **AI CLI 历史 API** (`/api/ai/history`)。

**修复前**:
```javascript
// 只从 AI CLI 历史 API 获取 (chat.db)
const resp = await fetch(CONFIG.apiBaseUrl + '/history?limit=50&context=' + chatContext);
```

**修复后**:
```javascript
// ✅ 优先从业务 API 获取 (ai_messages 表)
if (dealerConversationId) {
  const resp = await fetch(CONFIG.chatApiBase + '/session/' + dealerConversationId + '/messages');
  // ... 处理响应
  return; // 成功则直接返回
}

// フォールバック: AI CLI 历史 API (chat.db)
const resp = await fetch(CONFIG.apiBaseUrl + '/history?limit=50&context=' + chatContext);
// ... 处理响应
```

## 测试步骤

### 准备测试环境

1. **启动应用程序**:
   ```bash
   cd /home/ubuntu/ws/NetYamlForge
   dotnet run --project NetYamlForge
   ```

2. **确认数据库文件存在**:
   ```bash
   # 业务数据库 (auto-dealer-demo)
   ls -lh NetYamlForge/projects/auto-dealer-demo/database/
   
   # CLI 历史数据库 (chat.db)
   ls -lh NetYamlForge/projects/auto-dealer-demo/chat.db
   ```

### 测试 1: 聊天历史保存 ✅

**步骤**:
1. 以 **customer1** 身份登录
2. 打开聊天窗口 (点击右下角的 🚗 图标)
3. 发送以下消息:
   ```
   こんにちは
   車両を探していますが、在庫を確認できますか？
   試乗を予約したいです
   ```

4. **检查业务数据库**:
   ```bash
   # 找到 conversation_id
   sqlite3 NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db <<EOF
   SELECT conversation_id, customer_id, started_at 
   FROM ai_conversations 
   ORDER BY started_at DESC 
   LIMIT 5;
   EOF
   
   # 使用上面的 conversation_id 查询消息
   sqlite3 NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db <<EOF
   SELECT message_id, sender, substr(content, 1, 50) as content_preview, timestamp
   FROM ai_messages 
   WHERE conversation_id = '<conversation_id>'
   ORDER BY timestamp;
   EOF
   ```

**预期结果**:
- ✅ 所有用户消息 (`sender = 'customer'`) 都已保存
- ✅ 所有 AI 回复 (`sender = 'ai'`) 都已保存
- ✅ 时间戳正确

### 测试 2: 聊天历史恢复 ✅

**步骤**:
1. 发送几条消息后,**刷新页面** (F5)
2. 重新以 **customer1** 身份登录
3. 打开聊天窗口

**预期结果**:
- ✅ 历史消息自动显示
- ✅ 消息顺序正确 (时间升序)
- ✅ 用户消息和 AI 回复都能区分

### 测试 3: AI 响应质量 ⚠️

**前提条件**: CLI 工具必须已安装和认证

1. **检查 CLI 工具状态**:
   ```bash
   # 检查 Qwen Code
   which qwen
   qwen --version
   
   # 或者检查 Claude
   which claude
   claude --version
   ```

2. **发送测试消息**:
   ```
   こんにちは、今日はどんな車がおすすめですか？
   ```

3. **检查应用日志**:
   ```bash
   # 查看日志输出
   tail -f NetYamlForge/logs/*.log | grep -i "AI 応答"
   ```

**预期结果**:

✅ **成功情况**:
- 回复内容是自然的日语
- 回复内容与汽车相关
- 日志显示: `AI 応答生成開始：provider=CliFirstChain`
- 日志显示: `AI 応答取得完了：responseLength=XXX`

❌ **失败情况** (如果 CLI 工具未配置):
- 回复是硬编码的错误消息
- 日志显示: `AI 応答生成エラー`
- 日志显示: `すべての CLI プロバイダーが失敗しました`

### 测试 4: 多会话隔离 ✅

**步骤**:
1. 以 **customer1** 登录,发送消息 "A"
2. 以 **customer2** 登录,发送消息 "B"
3. 检查两个会话的消息是否正确隔离

**验证 SQL**:
```bash
sqlite3 NetYamlForge/projects/auto-dealer-demo/database/auto-dealer-demo.db <<EOF
SELECT c.conversation_id, c.customer_id, m.sender, substr(m.content, 1, 30) as content_preview
FROM ai_conversations c
LEFT JOIN ai_messages m ON c.conversation_id = m.conversation_id
WHERE c.customer_id IN ('customer1', 'customer2')
ORDER BY c.customer_id, m.timestamp;
EOF
```

**预期结果**:
- ✅ customer1 的消息只包含 "A"
- ✅ customer2 的消息只包含 "B"
- ✅ 没有交叉污染

## 常见问题排查

### 问题 1: 聊天历史不显示

**检查点**:
1. 浏览器控制台是否有错误?
   ```
   F12 → Console → 查找错误
   ```

2. 网络请求是否成功?
   ```
   F12 → Network → 查找 `/messages` 请求
   ```

3. 响应内容是什么?
   ```
   检查响应体是否包含消息数组
   ```

### 问题 2: AI 响应是错误消息

**检查点**:
1. CLI 工具是否安装?
   ```bash
   which qwen  # 或 which claude
   ```

2. CLI 工具是否认证?
   ```bash
   qwen status  # 或 claude status
   ```

3. 应用日志中的错误是什么?
   ```bash
   grep -i "CLI" NetYamlForge/logs/*.log | tail -20
   ```

**解决方案**:
- 如果 CLI 工具未安装,可以参考 `QWEN.md` 中的安装指南
- 如果需要在没有 CLI 的情况下测试,可以添加一个备用 AI API

### 问题 3: 会话 ID 没有恢复

**检查点**:
1. sessionStorage 中是否有会话 ID?
   ```javascript
   // 在浏览器控制台执行
   sessionStorage.getItem('dealer_conv_customer')
   ```

2. 如果没有,检查 `startDealerSession` 是否成功
   ```javascript
   // 在浏览器控制台执行
   console.log('dealerConversationId:', dealerConversationId);
   ```

## 测试检查清单

- [ ] 聊天历史正确保存到业务数据库
- [ ] 刷新页面后聊天历史正确恢复
- [ ] AI 响应是正常的 AI 生成内容 (不是错误消息)
- [ ] 多个用户的会话正确隔离
- [ ] 消息顺序正确 (时间升序)
- [ ] 用户消息和 AI 回复正确区分

## 回滚方案

如果修复后出现问题,可以通过 Git 回滚:

```bash
git checkout -- NetYamlForge/wwwroot/js/dealer-chat-widget.js
```

或者手动恢复 `restoreFromServer` 函数到原始版本。

## 联系信息

如果测试中发现问题,请提供以下信息:
1. 浏览器控制台的错误消息
2. 网络请求的详细信息 (URL、状态码、响应体)
3. 应用日志的相关部分
4. 数据库中的消息记录 (使用上面的 SQL)

---

*测试日期: 2026-04-08*
*修复版本: dealer-chat-widget.js (2026-04-08 修改)*
