# 常驻 CLI 进程功能文档

## 概述

常驻 CLI 进程功能允许 Qwen Code 和 Claude Code 等 CLI 工具以守护进程模式运行，避免每次请求启动新进程的开销（2-5秒），同时保持会话上下文，支持多轮对话。

## 架构

```
┌─────────────────────────────────────────────────────────────┐
│                    AIController (HTTP API)                   │
│  POST /api/AI/chat  → 返回 TaskId（异步）                     │
│  GET  /api/AI/tasks/{id}  → 轮询进度                         │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│              PooledCLIService (装饰器)                        │
│  - 拦截 CLI 执行请求                                         │
│  - 通过 DaemonChatServiceFactory 获取对应服务                 │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│              DaemonChatServiceFactory                         │
│  - 根据 provider 动态创建 DaemonChatService                   │
│  - 缓存实例，避免重复创建                                     │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│              DaemonChatService                                │
│  - 管理进程池（ConcurrentDictionary）                         │
│  - 获取/创建/归还 DaemonProcessInstance                       │
│  - 失败时自动回退到标准 CLI 执行                              │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│              DaemonProcessInstance                            │
│  - 启动: qwen --yolo --output-format stream-json             │
│  - stdin 写入: JSON 格式消息                                  │
│  - stdout 读取: 后台循环 + JSON 解析                          │
│  - 健康检查: 空闲超时 + 最大请求次数                          │
└─────────────────────────────────────────────────────────────┘
```

## 配置

### appsettings.json

```json
{
  "AICli": {
    "ProcessPool": {
      "EnableDaemonMode": true,
      "MaxPoolSize": 3,
      "IdleTimeoutMinutes": 10,
      "HealthCheckIntervalSeconds": 30,
      "MaxStartRetries": 3,
      "MaxLifetimeMinutes": 60,
      "MaxRequestsPerProcess": 100,
      "EnablePersistentSessions": true
    },
    "QwenCode": {
      "Path": "/path/to/qwen",
      "Model": "qwen-plus",
      "ApiKey": "your-dashscope-api-key"
    },
    "Claude": {
      "Path": "/path/to/claude",
      "Model": "haiku",
      "ChatEffort": "low"
    }
  }
}
```

### 配置项说明

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `EnableDaemonMode` | `true` | 启用常驻进程模式 |
| `MaxPoolSize` | `3` | 进程池最大大小（每个 provider） |
| `IdleTimeoutMinutes` | `10` | 空闲超时（分钟），超时后回收进程 |
| `HealthCheckIntervalSeconds` | `30` | 健康检查间隔（秒） |
| `MaxStartRetries` | `3` | 启动失败的最大重试次数 |
| `MaxLifetimeMinutes` | `60` | 进程最大存活时间（分钟），0=无限制 |
| `MaxRequestsPerProcess` | `100` | 每个进程最大请求次数，0=无限制 |
| `EnablePersistentSessions` | `true` | 启用会话持久化（--resume） |

## 使用方式

### 1. 通过 HTTP API

```bash
# 发送聊天请求（自动使用常驻进程）
curl -X POST http://localhost:5000/api/AI/chat \
  -H "Content-Type: application/json" \
  -d '{
    "message": "分析代码库结构",
    "cliTool": "qwen",
    "project": "my-project"
  }'

# 返回示例
{
  "taskId": "uuid-here",
  "status": "Pending"
}

# 轮询任务状态
curl http://localhost:5000/api/AI/tasks/uuid-here

# 返回示例
{
  "taskId": "uuid-here",
  "status": "Completed",
  "progress": 100,
  "result": "代码库分析完成...",
  "sessionId": "session-abc-123"
}
```

### 2. 通过代码调用

```csharp
// 获取 CLI 服务
var cliService = serviceProvider.GetRequiredService<ICLIService>();

// 流式执行（自动使用常驻进程）
await foreach (var update in cliService.ExecuteStreamingAsync(
    "分析代码库",
    workingDirectory: "/path/to/project",
    sessionId: "optional-session-id"))
{
    Console.WriteLine($"[{update.Status}] {update.Message}");
}

// 非流式执行
var result = await cliService.ExecuteAsync(
    "生成测试报告",
    allowedTools: new() { "Read", "Write", "Bash" });
```

### 3. 进程池管理

```csharp
// 获取进程池统计
var poolManager = serviceProvider.GetRequiredService<CliProcessPoolManager>();
var stats = poolManager.GetPoolStats();

// 清空进程池
poolManager.ClearPool("qwen");
poolManager.ClearAllPools();
```

## Stream-JSON 协议

### Qwen Code 协议

**stdin 输入：**
```json
{
  "type": "message",
  "content": "分析代码库结构",
  "session_id": "optional-session-id",
  "allowed_tools": ["Read", "Write", "Bash"]
}
```

**stdout 输出（流式）：**
```json
// 系统消息
{"type":"system","model":"qwen-plus","tools":5,"session_id":"abc..."}

// 助手消息（流式更新）
{"type":"assistant","message":{"content":[{"type":"text","text":"正在分析..."}]}}

// 工具调用
{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"..."}}]}}

// 最终结果
{"type":"result","result":"分析完成...","session_id":"abc..."}
```

### Claude Code 协议

**stdin 输入：**
```json
{
  "type": "user",
  "message": {
    "content": "分析代码库结构"
  },
  "session_id": "optional-session-id"
}
```

**stdout 输出：**
```json
// 系统消息
{"type":"system","model":"claude-3-haiku","tools":5,"session_id":"abc..."}

// 助手消息
{"type":"assistant","message":{"content":[{"type":"text","text":"正在分析..."}]}}

// 最终结果
{"type":"result","result":"分析完成...","session_id":"abc..."}
```

## 进程生命周期

```
启动 ──────────────────────────────────────────────┐
  │                                                  │
  ├─ 从池中获取健康空闲进程                           │
  │    │                                              │
  │    ├─ 找到 → 复用（Touch 更新时间戳）              │
  │    │                                              │
  │    └─ 未找到 → 创建新进程                         │
  │         │                                          │
  │         ├─ 启动: CLI --yolo --output-format ...   │
  │         ├─ 等待就绪（5秒超时）                      │
  │         └─ 启动后台读写任务                        │
  │                                                    │
执行 ──────────────────────────────────────────────┤
  │                                                  │
  ├─ 发送消息（stdin 写入 JSON）                      │
  │    │                                              │
  │    ├─ 等待响应（stdout 读取 JSON）                 │
  │    │                                              │
  │    ├─ 解析 Stream-JSON 消息                       │
  │    │    ├─ assistant → 流式更新                   │
  │    │    ├─ result → 最终结果                      │
  │    │    └─ error → 错误处理                       │
  │    │                                              │
  │    └─ 返回 ProgressUpdate                         │
  │                                                    │
归还 ──────────────────────────────────────────────┤
  │                                                  │
  ├─ 检查健康状态                                     │
  │    │                                              │
  │    ├─ 健康 → 归还池中                             │
  │    │                                              │
  │    ├─ 不健康 → 释放进程                           │
  │    │                                              │
  │    └─ 池满 → 释放进程                             │
  │                                                    │
清理 ──────────────────────────────────────────────┤
  │                                                  │
  ├─ 定期健康检查（30秒）                             │
  │    │                                              │
  │    ├─ 空闲超时 → 标记不健康                       │
  │    │                                              │
  │    ├─ 超过最大存活时间 → 释放                     │
  │    │                                              │
  │    └─ 超过最大请求次数 → 释放                     │
  │                                                    │
  └──────────────────────────────────────────────────┘
```

## 性能对比

| 场景 | 标准模式 | 常驻模式 | 提升 |
|------|----------|----------|------|
| 首次请求 | 2-5秒（启动）+ 执行 | 执行 | **2-5秒** |
| 后续请求 | 2-5秒（启动）+ 执行 | 执行（进程复用） | **2-5秒** |
| 多轮对话 | 每次重启 | 保持上下文 | **显著** |
| 资源占用 | 低（按需） | 中（池化） | 可控 |

## 故障排除

### 进程启动失败

**症状：** 日志显示 `[常驻进程] 启动失败`

**原因：**
1. CLI 未安装或路径不正确
2. 环境变量缺失（如 `DASHSCOPE_API_KEY`）

**解决：**
```bash
# 检查 CLI 安装
qwen --version
claude --version

# 检查环境变量
echo $DASHSCOPE_API_KEY
echo $ANTHROPIC_API_KEY
```

### 进程不健康

**症状：** 请求回退到标准执行

**原因：**
1. 进程意外退出
2. 空闲超时
3. 超过最大请求次数

**解决：**
- 检查日志中的健康检查信息
- 调整 `MaxLifetimeMinutes` 和 `MaxRequestsPerProcess`

### 会话未保持

**症状：** 多轮对话缺少上下文

**原因：**
1. `EnablePersistentSessions` 未启用
2. 未传递 `sessionId`

**解决：**
```json
{
  "message": "继续上次的分析",
  "sessionId": "previous-session-id"
}
```

## 与 Claude Code 对比

Claude Code 也支持类似功能：

```bash
# Claude 常驻模式
claude --daemon --output-format stream-json --dangerously-skip-permissions
```

我们的实现对两者使用相同的抽象层，通过 `DaemonMessageProtocol` 适配不同协议。

## 未来改进

1. **双向 Stream-JSON**：支持 `--input-format stream-json` 实现真正的双向流
2. **WebSocket 推送**：通过 WebSocket 实时推送进度更新
3. **进程预热**：应用启动时预创建进程池
4. **动态池大小**：根据负载自动调整池大小
5. **指标导出**：导出 Prometheus 指标监控

## 相关文件

| 文件 | 说明 |
|------|------|
| `Services/AI/DaemonProcessInstance.cs` | 常驻进程实例 |
| `Services/AI/DaemonChatService.cs` | 常驻聊天服务 |
| `Services/AI/DaemonChatServiceFactory.cs` | 服务工厂 |
| `Services/AI/DaemonMessageProtocol.cs` | 协议适配器 |
| `Services/AI/PooledCLIService.cs` | 装饰器（接入常驻服务） |
| `Program.cs` | DI 注册配置 |
