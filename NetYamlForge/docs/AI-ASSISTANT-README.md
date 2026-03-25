# AI Assistant 功能说明

## 概述

AI Assistant 功能允许用户通过侧边滑出面板与 AI CLI 工具（如 Claude Code、Qwen Code）进行交互，AI 接收指令并执行任务，同时实时显示任务进度。

## 架构

```
前端 UI (右侧滑出面板)
    ↓
AIController (HTTP API)
    ↓
TaskQueueService (任务队列)
    ↓
CLIServiceFactory → ICLIService 实现
    ↓
ProcessExecutor → CLI 工具 (claude, qwen-code)
```

SignalR Hub 用于实时推送进度更新。

## 功能特点

- **侧边滑出式 UI**：不影响现有项目页面
- **多 CLI 支持**：可选择 Claude Code、Qwen Code 或 Mock（测试）
- **实时进度显示**：通过 SignalR 或轮询显示任务进度
- **任务管理**：支持查看任务列表、取消任务
- **OAuth 认证**：CLI 工具自行管理认证，应用层无需处理 API Key

## 安装和配置

### 1. 安装 CLI 工具

**Claude Code:**
```bash
npm install -g @anthropic-ai/claude-code
claude login  # 首次认证
```

**Qwen Code:**
```bash
# 参考阿里云文档安装
```

### 2. 配置应用（可选）

在 `appsettings.json` 中添加配置：

```json
{
  "AICli": {
    "DefaultTool": "claude",
    "TaskTimeoutSeconds": 600,
    "MaxConcurrentTasks": 2,
    "DefaultAllowedTools": ["Read", "Write", "Edit", "Git"]
  }
}
```

或使用环境变量：

```bash
export AI_DEFAULT_TOOL="claude"
export AI_TASK_TIMEOUT_SECONDS=600
export AI_MAX_CONCURRENT_TASKS=2
```

### 3. 运行应用

```bash
dotnet run --project NetYamlForge
```

## 使用方法

### 前端 UI

1. 登录后，点击页面右侧的 AI 助手按钮（💬 图标）
2. 面板从右侧滑出
3. 选择 CLI 工具（Claude Code / Qwen Code / Mock）
4. 在输入框中输入指令
5. 点击发送按钮
6. 实时查看任务进度和日志

### API 端点

| 方法 | 端点 | 描述 |
|------|------|------|
| `POST` | `/api/ai/chat` | 发送对话请求 |
| `GET` | `/api/ai/tasks` | 获取任务列表 |
| `GET` | `/api/ai/tasks/{id}` | 获取任务详情 |
| `DELETE` | `/api/ai/tasks/{id}` | 取消任务 |
| `GET` | `/api/ai/cli-tools` | 获取可用 CLI 工具 |

### 示例请求

```bash
# 发送聊天请求
curl -X POST https://localhost:7000/api/ai/chat \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
    "message": "创建一个用户管理页面",
    "cliTool": "claude",
    "streaming": true
  }'

# 获取 CLI 工具状态
curl https://localhost:7000/api/ai/cli-tools
```

## 测试

使用 Mock CLI 服务进行测试（无需安装实际 CLI）：

1. 在 AI 面板中选择 "Mock (Test)"
2. 输入任意指令
3. 会看到模拟的进度更新

## 文件结构

```
NetYamlForge/
├── Controllers/
│   └── AIController.cs
├── Hubs/
│   └── AIProgressHub.cs
├── Models/AI/
│   ├── AIChatRequest.cs
│   ├── AIChatResponse.cs
│   ├── AITask.cs
│   ├── TaskStatus.cs
│   ├── ProgressUpdate.cs
│   └── CliToolInfo.cs
├── Services/AI/
│   ├── CliConfig.cs
│   ├── ICLIService.cs
│   ├── CLIServiceFactory.cs
│   ├── ProcessExecutor.cs
│   ├── BaseCLIService.cs
│   ├── ProgressTracker.cs
│   ├── TaskQueueService.cs
│   └── Providers/
│       ├── ClaudeCLIService.cs
│       ├── QwenCodeCLIService.cs
│       └── MockCLIService.cs
├── wwwroot/
│   ├── css/ai-assistant.css
│   └── js/ai-assistant.js
└── Views/Shared/_Layout.cshtml (已更新)
```

## 安全考虑

### 工具权限控制

通过 `--allowedTools` 参数限制 CLI 工具权限：

```json
{
  "AICli": {
    "DefaultAllowedTools": ["Read", "Write", "Edit", "Git"]
  }
}
```

可用权限：
- `Read`: 读取文件
- `Write`: 创建/修改文件
- `Edit`: 编辑文件
- `Bash`: 执行 shell 命令
- `Git`: Git 操作
- `Web`: 网络访问

### 文件访问限制

CLI 只能在项目目录内工作，无法访问敏感文件（如 `appsettings.json`、数据库文件）。

### 认证

- CLI 工具通过 OAuth 认证（由 CLI 自行管理）
- 应用层通过 `claude login` 等命令进行认证
- Token 存储在用户目录 (`~/.config/claude-code/`)

## 故障排除

### CLI 未安装

错误：`CLI tool 'claude' is not installed`

解决：
```bash
npm install -g @anthropic-ai/claude-code
```

### CLI 未认证

错误：`CLI tool 'claude' is not authenticated`

解决：
```bash
claude login
```

### 任务超时

默认超时时间为 10 分钟。如需修改：
```json
{
  "AICli": {
    "TaskTimeoutSeconds": 1800
  }
}
```

### SignalR 连接失败

如果 SignalR 客户端无法加载，系统会自动切换到轮询模式（每 1 秒轮询一次）。

## 未来扩展

- [ ] 支持更多 CLI 工具（Ollama、Gemini 等）
- [ ] AI 生成实体/页面 YAML
- [ ] 任务历史记录持久化
- [ ] 预定义指令模板
- [ ] 批处理模式
- [ ] 结果预览和 diff 查看

## 参考文档

- [设计文档](../AI-ASSISTANT-DESIGN.md)
- [Claude Code CLI 文档](https://code.claude.com/docs/en/headless)
- [SignalR 文档](https://docs.microsoft.com/aspnet/core/signalr/)
