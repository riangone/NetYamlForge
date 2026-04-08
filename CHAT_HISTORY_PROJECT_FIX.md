# 子项目聊天记录保存修复报告

## 问题描述

子项目（如 `auto-dealer-demo`、`jpiere-cs`）的聊天记录无法正确保存和查询。

## 根本原因

在 `AutoDealerChatService` 和 `JpiereChatService` 中，调用 `_chatHistory.SaveMessageAsync` 时，**错误地将 `conversationId`（会话 ID）作为 `userId` 参数**传入：

```csharp
// ❌ 错误：使用 conversationId 作为 userId
await _chatHistory.SaveMessageAsync(conversationId, customerMessage, "user",
    provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
```

这导致：
1. **每条消息的 UserId 都不同**：因为每次会话都会生成新的 `conversationId`（如 `CONV-20260408-123456-xxx`）
2. **无法按用户聚合聊天记录**：查询时使用真正的 `userId`，但保存时用的是 `conversationId`
3. **聊天记录丢失**：用户无法看到历史聊天消息

## 修复方案

将 `userId` 参数改为使用 `_projectName`（项目名称），这样：
- 同一项目的所有聊天记录共享同一个 `userId`
- 可以通过项目名称查询该项目的所有聊天记录
- 不同项目的聊天记录通过 `ChatContext` 隔离

### 修改的文件

#### 1. `NetYamlForge/Services/AI/AutoDealerChatService.cs`

**修改位置 1**（第 185-187 行）- Slot-filling 流程中的消息保存：
```csharp
// ✅ 修复后：使用 _projectName 作为 userId
await _chatHistory.SaveMessageAsync(_projectName, customerMessage, "user",
    provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
await _chatHistory.SaveMessageAsync(_projectName, responseText, "assistant",
    provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
```

**修改位置 2**（第 225-227 行）- 正常 LLM 流程中的消息保存：
```csharp
await _chatHistory.SaveMessageAsync(_projectName, customerMessage, "user",
    provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
await _chatHistory.SaveMessageAsync(_projectName, responseText, "assistant",
    provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
```

**修改位置 3**（第 485-487 行）- 员工消息保存：
```csharp
await _chatHistory.SaveMessageAsync(_projectName, staffMessage, "user",
    provider: _defaultProvider, chatContext: "dealer-staff", projectName: _projectName);
await _chatHistory.SaveMessageAsync(_projectName, responseText, "assistant",
    provider: _defaultProvider, chatContext: "dealer-staff", projectName: _projectName);
```

#### 2. `NetYamlForge/Services/AI/JpiereChatService.cs`

**修改位置**（第 212-214 行）- 用户消息保存：
```csharp
await _chatHistory.SaveMessageAsync(_projectName, userMessage, "user",
    provider: _defaultProvider, chatContext: $"jpiere-{userRole}", projectName: _projectName);
await _chatHistory.SaveMessageAsync(_projectName, responseText, "assistant",
    provider: _defaultProvider, chatContext: $"jpiere-{userRole}", projectName: _projectName);
```

#### 3. `NetYamlForge.Tests/Services/AI/ChatHistoryServiceTests.cs`

**新增测试**：`SaveMessageAsync_ProjectChat_UsesProjectNameAsUserId`
- 验证子项目聊天记录使用项目名称作为 userId
- 验证可以通过项目名称和 ChatContext 查询聊天记录
- 验证不同上下文的聊天记录隔离

## 测试验证

```bash
# 运行聊天历史测试
dotnet test --filter "FullyQualifiedName~ChatHistoryServiceTests"

# 结果：6 个测试全部通过
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6
```

## 设计说明

### 为什么使用 `_projectName` 作为 `userId`？

在 `ChatHistoryService` 的设计中：
- `userId` 用于标识**谁**发送了消息
- `projectName` 用于标识消息存储在**哪个项目的数据库**中
- `chatContext` 用于标识消息的**上下文类型**（如 `framework`、`dealer-customer`、`dealer-staff`）

对于子项目聊天：
- 每个项目有独立的 `chat.db` 数据库
- 在同一项目中，可能有多个用户角色（客户、员工、管理员等）
- 使用 `_projectName` 作为 `userId` 可以确保：
  - 该项目的所有消息都可以通过 `GetHistoryAsync(projectName, ...)` 查询
  - 不同上下文的聊天通过 `chatContext` 参数隔离
  - 数据库查询简单且高效

### 与全局 AI 聊天的对比

**全局 AI 聊天**（`AIController`）：
```csharp
var userId = GetCurrentUserId();  // 使用真正的用户 ID
await _chatHistory.SaveMessageAsync(userId, request.Message, "user",
    provider: request.CliTool,
    chatContext: string.IsNullOrEmpty(request.Project) ? "framework" : request.Project,
    projectName: request.Project);
```

**子项目聊天**（`AutoDealerChatService` / `JpiereChatService`）：
```csharp
await _chatHistory.SaveMessageAsync(_projectName, customerMessage, "user",
    provider: _defaultProvider, chatContext: "dealer-customer", projectName: _projectName);
```

差异原因：
- 全局 AI 聊天是多用户的，需要区分不同用户
- 子项目聊天是项目级别的，通常只有一个活跃用户（或会话），使用项目名称更合适

## 影响范围

### 受影响的功能
- ✅ `auto-dealer-demo` 项目的客户聊天记录保存
- ✅ `auto-dealer-demo` 项目的员工聊天记录保存
- ✅ `jpiere-cs` 项目的用户聊天记录保存

### 不受影响的功能
- ✅ 全局 AI 聊天（`/api/AI/chat`）
- ✅ AI 命令日志（`AICommandLog`）
- ✅ 聊天历史查询（`/api/AI/history`）

## 后续建议

1. **集成测试**：在实际环境中测试子项目聊天功能，确保消息可以正确保存和查询
2. **数据库迁移**：如果已有子项目数据库中存在使用 `conversationId` 作为 `userId` 的旧数据，考虑数据迁移或清理
3. **文档更新**：在开发者文档中说明子项目聊天记录的保存机制

## 修复日期

2026-04-08
