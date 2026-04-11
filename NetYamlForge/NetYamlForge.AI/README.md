# NetYamlForge.AI - 独立 AI 聊天服务项目

## 项目概述

`NetYamlForge.AI` 是一个独立的 AI 聊天服务项目，提供：
- AI 对话管理（SignalR 实时通信）
- 自然语言查询解析和执行
- 多种 LLM 提供商支持（Claude、Qwen、GPT、Ollama 等）
- 项目特定聊天服务（AutoDealer、JPiere 等）
- AI 报告和 PDF 生成
- 任务队列和进度跟踪

## 项目结构

```
NetYamlForge.AI/          # AI 服务类库
├── Controllers/          # API 控制器
├── Hubs/                 # SignalR Hubs（实时通信）
├── Services/             # 业务服务层
│   └── Providers/        # LLM 提供商实现
├── Infrastructure/       # 基础设施接口和实现
├── Models/               # 数据模型
├── Config/               # 配置类
└── Client/               # HTTP 客户端代理

NetYamlForge.AI.Web/      # 独立 Web 宿主
├── Program.cs            # 应用入口
├── appsettings.json      # 配置文件
└── wwwroot/              # 静态资源
```

## 快速开始

### 1. 构建项目

```bash
cd /home/ubuntu/ws/NetYamlForge
dotnet build NetYamlForge/NetYamlForge.AI.Web/NetYamlForge.AI.Web.csproj
```

### 2. 启动服务

```bash
# 使用启动脚本（推荐）
./start-ai-web.sh

# 或直接运行
cd NetYamlForge/NetYamlForge.AI.Web
dotnet run
```

服务将在 `http://localhost:5005` 启动。

### 3. 验证服务

```bash
# 运行验证脚本
./test-ai-web.sh

# 手动测试
curl http://localhost:5005/api/ai/reports/preview?type=daily
```

## 配置说明

### appsettings.json 关键配置

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=path/to/database.db"
  },
  "AICli": {
    "DefaultTool": "qwen",
    "TaskTimeoutSeconds": 1800,
    "MaxConcurrentTasks": 2
  },
  "AiWindow": {
    "DealerName": "AI 窓口ディーラー"
  }
}
```

### 支持的 AI 工具

| 工具 | 类型 | 配置项 |
|------|------|--------|
| Qwen Code | 云端 | `QwenCode` |
| Claude Code | 云端 | `Claude` |
| OpenAI Codex | 云端 | `Codex` |
| Google Gemini | 云端 | `Gemini` |
| Ollama | 本地 | `Ollama` |
| LM Studio | 本地 | `LmStudio` |
| Mock | 测试 | `mock` |

## API 端点

### SignalR Hubs（实时通信）

- `/aiChatHub` - AI 聊天
- `/aiProgressHub` - 任务进度
- `/aiDebateHub` - AI 辩论
- `/nlQueryHub` - 自然语言查询

### REST API

- `GET /api/ai/reports/daily` - 日次报告 PDF
- `GET /api/ai/reports/weekly` - 週次报告 PDF
- `GET /api/ai/reports/monthly` - 月次报告 PDF
- `GET /api/ai/reports/preview` - 报告预览

## 运行模式

### 嵌入式模式（默认）

AI 服务直接在主进程内运行，所有服务注册到 DI 容器。

### 独立进程模式

AI 服务在独立进程中运行，主应用通过 HTTP 客户端代理调用。

配置方式：
```json
{
  "AIMode": {
    "Mode": "standalone"
  }
}
```

## 主要服务

### 核心服务

| 服务 | 职责 |
|------|------|
| `IConversationManager` | 对话生命周期管理 |
| `IDirectAIProcessor` | 直接 AI 处理 |
| `IHandoverManager` | 人工交接管理 |
| `ICustomerDataService` | 客户数据服务 |
| `IAppointmentService` | 预约服务 |
| `IOperatorChatService` | 操作员聊天 |

### 项目特定服务

- `AutoDealerChatService` - 汽车经销商聊天服务
- `JpiereChatService` - JPiere 业务聊天服务

### 基础设施服务

- `IAIQueryExecutor` - AI 查询执行器
- `IAIDbConnectionFactory` - 数据库连接工厂
- `IAIProjectContext` - 项目上下文
- `IAIReportPdfService` - AI 报告 PDF 生成

## 开发指南

### 添加新的 LLM 提供商

1. 在 `Services/Providers/` 中实现 `ICLIService` 接口
2. 在 `AIServiceCollectionExtensions.cs` 中注册服务
3. 更新配置支持新的提供商

### 添加新的聊天服务

1. 继承 `BaseChatService` 抽象类
2. 实现必需的抽象方法
3. 在 DI 容器中注册服务

### 测试

```bash
# 运行 AI 相关测试
dotnet test --filter "FullyQualifiedName~Services.AI"

# 运行特定测试
dotnet test --filter "FullyQualifiedName~AutoDealerChatServiceTests"
```

## 已知问题和 TODO

以下 TODO 项目前未完全实现（不影响核心功能）：

1. **AIChatHub.OnDisconnectedAsync** - 连接清理逻辑
2. **NaturalLanguageQueryHub** - 查询取消和历史记录
3. **AIReportController.GetReportPreview** - 使用模拟数据
4. **AutoDealerChatController.GetStaffConversations** - 员工对话列表
5. **AIKnowledgeController.GetKnowledge** - 过滤器处理

这些都是可选功能，核心聊天和查询功能已经可以正常工作。

## 故障排查

### 服务无法启动

检查日志输出，常见原因：
- 数据库连接未配置
- 端口 5005 被占用
- AI CLI 工具配置错误

### 数据库连接错误

确保在 `appsettings.json` 或环境变量中配置了连接字符串：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=your-database.db"
  }
}
```

### AI CLI 工具错误

检查 AI CLI 工具是否正确安装和配置：

```bash
# 检查 Claude
claude --version

# 检查 Qwen Code
qwen --version

# 检查 Ollama
ollama list
```

## 相关文档

- [AI 助手设计文档](../AI-ASSISTANT-DESIGN.md)
- [AI 统一化计划](../AI_UNIFICATION_PLAN.md)
- [流程池实现总结](../PROCESS_POOL_IMPLEMENTPLEMENTATION_SUMMARY.md)

---

最后更新：2026-04-11
