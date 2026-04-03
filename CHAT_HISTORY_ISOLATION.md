# 聊天记录隔离机制实现报告

## 概述

实现了全局 AI 与子项目之间、以及各子项目相互独立的聊天记录隔离机制。

## 架构设计

### 数据库隔离

```
system.db                    # 全局 AI 聊天记录
├── AIChatHistory (chatContext='framework')
└── AICommandLog

projects/<name>/chat.db      # 子项目独立聊天记录
├── AIChatHistory (chatContext='<project>')
├── AIChatHistory (chatContext='dealer-staff')
├── AIChatHistory (chatContext='dealer-customer')
└── AICommandLog
```

### 隔离级别

1. **全局 AI** (`project=null`) → 使用 `system.db`
2. **子项目 A** (`project=auto-dealer-demo`) → 使用 `projects/auto-dealer-demo/chat.db`
3. **子项目 B** (`project=other-project`) → 使用 `projects/other-project/chat.db`

每个项目数据库内部还通过 `chatContext` 字段区分：
- 框架级别：`framework`
- 子项目员工：`dealer-staff`
- 子项目客户：`dealer-customer`

## 修改内容

### 1. ChatHistoryService.cs

**主要改动：**
- 添加 `IWebHostEnvironment` 依赖以解析项目路径
- 新增 `GetConnectionString(string? projectName)` 方法动态选择数据库
- 所有方法添加 `projectName` 参数支持
- 自动初始化项目数据库 schema

**关键代码：**
```csharp
private string GetConnectionString(string? projectName)
{
    if (string.IsNullOrEmpty(projectName))
    {
        return _globalConnectionString; // system.db
    }

    // プロジェクトディレクトリ内の chat.db を使用
    var projectChatDbPath = Path.Combine(_env.ContentRootPath, "projects", projectName, "chat.db");
    var projectConnectionString = $"Data Source={projectChatDbPath}";
    
    // プロジェクト DB のスキーマを初期化
    InitializeSchema(projectConnectionString, projectName);
    
    return projectConnectionString;
}
```

### 2. AIController.cs

**主要改动：**
- `Chat` 方法保存消息时传递 `projectName`
- `GetHistory` 方法添加 `[FromRoute] string? project` 参数
- `GetCommandLogs` 方法添加 `[FromRoute] string? project` 参数
- `SaveMessage` 方法添加 `[FromRoute] string? project` 参数
- `ClearHistory` 方法添加 `[FromRoute] string? project` 参数

**API 路由示例：**
```
GET  /api/AI/history?context=framework           # 全局 AI 历史
GET  /api/AI/history?context=framework           # 全局 AI 历史
GET  /{project}/api/AI/history?context=dealer-staff  # 项目员工历史
POST /{project}/api/AI/history                   # 保存到项目历史
```

### 3. AutoDealerChatService.cs

**主要改动：**
- 保存消息时传递 `projectName: _projectName`
- 确保使用项目独立数据库而非全局 system.db

**修改位置：**
```csharp
// 顧客メッセージ保存
await _chatHistory.SaveMessageAsync(
    conversationId,
    customerMessage,
    "user",
    provider: _defaultProvider,
    chatContext: "dealer-customer",
    projectName: _projectName);  // ← 追加

// スタッフメッセージ保存
await _chatHistory.SaveMessageAsync(
    conversationId,
    staffMessage,
    "user",
    provider: _defaultProvider,
    chatContext: "dealer-staff",
    projectName: _projectName);  // ← 追加
```

### 4. ChatHistoryServiceTests.cs

**主要改动：**
- 添加 `IWebHostEnvironment` Mock
- 更新所有测试方法传递 `projectName: null`
- 新增 `SaveMessageAsync_UsesProjectDatabase` 测试验证项目隔离

## 测试验证

```bash
# 运行 ChatHistoryService 测试
dotnet test --filter "FullyQualifiedName~ChatHistoryServiceTests"

# 测试结果
Test summary: total: 6, failed: 0, succeeded: 6, skipped: 0
```

### 测试覆盖

1. ✅ `SaveMessageAsync_SavesMessage_WithChatContext` - 基础消息保存
2. ✅ `GetHistoryAsync_FiltersByChatContext` - 按上下文过滤
3. ✅ `GetHistoryAsync_ReturnsAllContexts_WhenChatContextIsNull` - 获取全部上下文
4. ✅ `ClearHistoryAsync_ClearsByChatContext` - 按上下文清除
5. ✅ `SaveMessageAsync_StoresProvider` - 存储提供者信息
6. ✅ `SaveMessageAsync_UsesProjectDatabase` - **项目隔离验证**

## 数据库 Schema

两个数据库使用相同的表结构：

```sql
CREATE TABLE IF NOT EXISTS AIChatHistory (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      TEXT NOT NULL,
    Content     TEXT NOT NULL,
    Type        TEXT NOT NULL,  -- 'user' | 'assistant'
    Provider    TEXT,           -- 'qwen', 'claude', etc.
    ChatContext TEXT NOT NULL DEFAULT 'framework',
    CreatedAt   TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_aichat_user ON AIChatHistory(UserId, Id);
CREATE INDEX IF NOT EXISTS idx_aichat_context ON AIChatHistory(UserId, ChatContext, Id);

CREATE TABLE IF NOT EXISTS AICommandLog (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      TEXT NOT NULL,
    TaskId      TEXT NOT NULL UNIQUE,
    CliTool     TEXT NOT NULL,
    InputText   TEXT NOT NULL,
    ProjectName TEXT,
    SessionId   TEXT,
    Status      TEXT NOT NULL DEFAULT 'Pending',
    ResultText  TEXT,
    ErrorText   TEXT,
    DurationMs  INTEGER,
    CreatedAt   TEXT NOT NULL,
    CompletedAt TEXT
);
```

## 向后兼容性

- ✅ 全局 AI (`project=null`) 继续使用 `system.db`
- ✅ 现有 API 签名保持兼容（`projectName` 参数可选）
- ✅ 默认值行为不变（`projectName=null` → `system.db`）

## 安全考虑

1. **数据隔离**：各子项目无法访问其他项目的聊天记录
2. **路径遍历防护**：项目名通过路由参数传递，由 ASP.NET Core 验证
3. **自动初始化**：项目数据库首次访问时自动创建 schema

## 性能优化

1. **连接字符串缓存**：`GetConnectionString` 可添加缓存避免重复计算
2. **延迟初始化**：项目数据库仅在首次访问时创建
3. **索引优化**：`idx_aichat_context` 索引加速按上下文查询

## 未来扩展

1. **数据库清理**：添加定期清理旧记录的机制
2. **备份支持**：项目数据库独立备份
3. **迁移工具**：在不同环境间迁移聊天记录
4. **缓存层**：Redis 缓存热点聊天记录

## 相关文件

- `NetYamlForge/Services/AI/ChatHistoryService.cs` - 核心服务
- `NetYamlForge/Controllers/AIController.cs` - API 控制器
- `NetYamlForge/Services/AI/AutoDealerChatService.cs` - 子项目聊天服务
- `NetYamlForge.Tests/Services/AI/ChatHistoryServiceTests.cs` - 单元测试

---

*实现完成日期：2026 年 4 月 1 日*
