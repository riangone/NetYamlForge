# AI Assistant 功能设计文档

## 1. 概述

本文档描述为 NetYamlForge 框架添加 AI 交互功能的设计方案。该功能允许用户通过专门的交互界面与 AI CLI（如 Claude Code CLI、Qwen Code CLI、GitHub Copilot CLI 等）进行通信，AI 接收指令并执行任务，同时实时显示任务进度。

### 1.1 设计目标

- **独立交互空间**：AI 交互界面独立于现有项目页面，不影响业务功能
- **侧边滑出式 UI**：采用右侧滑出抽屉面板，可打开/关闭，可调整宽度
- **多 CLI 支持**：用户可选择使用不同的 AI CLI 工具（Claude Code、Qwen Code、GitHub Copilot 等）
- **实时进度反馈**：任务执行过程中实时显示进度和日志
- **异步任务处理**：支持长时间运行的任务，不阻塞 UI
- **CLI 集成**：通过 Process 调用 CLI 命令，而非 HTTP API
- **OAuth 认证**：利用 CLI 自身的 OAuth 认证机制，无需在应用中处理 API Key

### 1.2 支持的 AI CLI 工具

| CLI 工具 | 类型 | 服务类 | 配置类 | 状态 |
|---------|------|--------|--------|------|
| **GitHub Copilot CLI** | 云端 | `CopilotCLIService` | `CopilotConfig` | ✅ 已实现 |
| Claude Code CLI | 云端 | `ClaudeCLIService` | `ClaudeConfig` | ✅ 已实现 |
| Qwen Code CLI | 云端 | `QwenCodeCLIService` | `QwenCodeConfig` | ✅ 已实现 |
| OpenAI Codex CLI | 云端 | `CodexCLIService` | `CodexConfig` | ✅ 已实现 |
| Google Gemini CLI | 云端 | `GeminiCLIService` | `GeminiConfig` | ✅ 已实现 |
| Ollama CLI | 本地 | `OllamaCLIService` | `OllamaConfig` | ✅ 已实现 |
| LM Studio | 本地 | `LmStudioCLIService` | `LmStudioConfig` | ✅ 已实现 |
| Mock CLI | 测试 | `MockCLIService` | - | ✅ 已实现 |

### 1.3 非目标

- 不修改现有项目页面的布局和功能
- 不强制绑定特定 AI CLI
- 不替代 CLI 工具，而是提供 GUI 封装
- 不直接处理 OAuth 认证（由 CLI 自行管理）

---

## 2. 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                     前端 UI 层                                │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  AI Assistant Panel (右侧滑出抽屉)                      │    │
│  │  ┌─────────────┐  ┌──────────────┐  ┌────────────┐ │    │
│  │  │  对话区域    │  │  指令输入框   │  │  CLI 选择器 │ │    │
│  │  │  (消息流)    │  │  (发送按钮)   │  │  进度显示  │ │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     API 控制器层                              │
│  ┌─────────────────┐  ┌─────────────────┐                  │
│  │ AIController.cs │  │ AIProgressHub.cs│                  │
│  │ (HTTP 请求处理)  │  │ (SignalR 推送)   │                  │
│  └─────────────────┘  └─────────────────┘                  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     服务层                                   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │              ICLIService 接口                        │   │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐            │   │
│  │  │ClaudeCLI │ │QwenCLI   │ │CustomCLI │            │   │
│  │  │Service   │ │Service   │ │Service   │            │   │
│  │  └──────────┘ └──────────┘ └──────────┘            │   │
│  └─────────────────────────────────────────────────────┘   │
│  ┌─────────────────┐  ┌─────────────────┐                  │
│  │ TaskQueueService│  │ ProgressTracker │                  │
│  │ (任务队列管理)   │  │ (进度追踪)      │                  │
│  └─────────────────┘  └─────────────────┘                  │
│  ┌─────────────────┐                                        │
│  │ ProcessExecutor │                                        │
│  │ (进程调用管理)   │                                        │
│  └─────────────────┘                                        │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     基础设施层                               │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │ System.Diagnostics  │  │ SignalR      │  │ 文件系统访问  │     │
│  │ (Process 调用)     │  │ (实时推送)    │  │ (项目文件)    │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
│  ┌──────────────────────────────────────────────────────┐ │
│  │              AI CLI Tools (系统 PATH)                 │ │
│  │    claude        qwen-code       (自定义 CLI)         │ │
│  └──────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. 前端 UI 设计

### 3.1 面板形态

| 状态 | 描述 | 触发方式 |
|------|------|----------|
| **收起** | 屏幕右侧边缘显示图标/按钮 | 默认状态 |
| **展开** | 从右侧滑出抽屉面板（宽度约 400-600px） | 点击边缘图标 |
| **关闭** | 完全隐藏 | 点击面板关闭按钮 |

### 3.2 面板布局

```
┌──────────────────────────────────────────────────────────┐
│  AI Assistant                                      [×]   │  ← 头部栏
├──────────────────────────────────────────────────────────┤
│  AI: [Claude ▼]  [Qwen]  [设置 ⚙️]                        │  ← 控制栏
├──────────────────────────────────────────────────────────┤
│                                                          │
│  ┌────────────────────────────────────────────────────┐ │
│  │ 🤖 AI                                              │ │
│  │                                                    │ │
│  │  你好！我是你的 AI 助手。我可以帮助你：                │ │
│  │  • 创建实体定义                                     │ │
│  │  • 生成页面模板                                     │ │
│  │  • 编写业务逻辑代码                                 │ │
│  │  • 分析项目结构                                     │ │
│  │                                                    │ │
│  │  请告诉我你需要什么帮助？                            │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ┌────────────────────────────────────────────────────┐ │
│  │ 👤 用户                                            │ │
│  │                                                    │ │
│  │  创建一个用户管理页面，包含姓名、邮箱、角色字段       │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ┌────────────────────────────────────────────────────┐ │
│  │ 🤖 AI                                              │ │
│  │                                                    │ │
│  │  ⏳ 任务进行中... 50%                               │ │
│  │  ████████████████░░░░░░░░░░░░░░░░░░░░░░           │ │
│  │                                                    │ │
│  │  [✓] 分析项目结构                                  │ │
│  │  [✓] 生成实体 YAML                                 │ │
│  │  [⏳] 创建页面模板 (进行中...)                       │ │
│  │  [ ] 生成测试文件                                  │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ┌────────────────────────────────────────────────────┐ │
│  │ 🤖 AI                                              │ │
│  │                                                    │ │
│  │  ✅ 任务完成！                                      │ │
│  │  已创建以下文件：                                   │ │
│  │  • projects/demo/entities/user.yml                 │ │
│  │  • projects/demo/pages/user-list.yaml              │ │
│  │                                                    │ │
│  │  [查看文件] [撤销更改]                              │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  [▼ 自动滚动到底部]                                      │
├──────────────────────────────────────────────────────────┤
│  ┌────────────────────────────────────────────────────┐ │
│  │ 输入指令...                                       │  │  ← 输入框
│  └────────────────────────────────────────────────────┘ │
│  [发送 ▶]                     [停止 ⏹] [清除 🗑️]        │
└──────────────────────────────────────────────────────────┘
```

### 3.3 UI 组件规范

- **框架**：原生 JavaScript + HTMX（与现有项目一致）
- **样式**：DaisyUI + Tailwind CSS（复用现有样式系统）
- **图标**：Heroicons 或 Phosphor Icons
- **动画**：CSS Transition 实现平滑滑出效果

---

## 4. 后端设计

### 4.1 CLI 调用方式

采用 **Process 调用 CLI** 方式，支持两种输出模式：

#### 4.1.1 JSON 输出模式（标准响应）

```bash
claude -p "你的指令" --output-format json
```

返回完整 JSON 响应，适合一次性获取结果。

#### 4.1.2 Stream-JSON 模式（实时流式）

```bash
claude -p "你的指令" --output-format stream-json
```

每行输出一个独立的 JSON 对象，适合实时显示进度。

### 4.2 HTTP API 端点

| 方法 | 端点 | 描述 |
|------|------|------|
| `POST` | `/api/ai/chat` | 发送对话请求，创建新任务 |
| `GET` | `/api/ai/tasks` | 获取当前用户的所有任务列表 |
| `GET` | `/api/ai/tasks/{id}` | 获取指定任务的状态和进度 |
| `DELETE` | `/api/ai/tasks/{id}` | 取消指定任务（终止 CLI 进程） |
| `GET` | `/api/ai/cli-tools` | 获取可用的 CLI 工具列表 |
| `GET` | `/api/ai/health` | 检查 CLI 工具安装状态 |

### 4.3 SignalR Hub

```
/ws/ai/progress  - 实时推送任务进度更新
```

**客户端接收的消息类型：**
```json
{
  "type": "ProgressUpdate",
  "taskId": "task_123",
  "progress": 50,
  "status": "Running",
  "message": "正在分析项目结构...",
  "logs": ["步骤 1 完成", "步骤 2 进行中"],
  "streamData": {}  // stream-json 模式的原始数据
}
```

### 4.4 API 请求/响应模型

#### 4.4.1 Chat 请求
```json
POST /api/ai/chat
{
  "message": "创建一个用户管理页面",
  "cliTool": "claude",
  "project": "demo",
  "sessionId": "session_456",  // 可选，用于多轮对话
  "streaming": true,            // 是否启用流式输出
  "allowedTools": ["Read", "Write", "Bash", "Git"]  // 工具权限控制
}
```

#### 4.4.2 Chat 响应
```json
{
  "taskId": "task_123",
  "status": "Pending",
  "message": "任务已创建，等待处理...",
  "progress": 0,
  "sessionId": "session_789"  // 返回 session_id 用于后续对话
}
```

#### 4.4.3 任务状态响应
```json
{
  "taskId": "task_123",
  "status": "Running",
  "progress": 50,
  "message": "正在生成实体定义...",
  "logs": [
    "✓ 分析项目结构",
    "✓ 生成实体 YAML",
    "⏳ 创建页面模板"
  ],
  "result": null,
  "error": null,
  "sessionId": "session_789",
  "createdAt": "2026-03-25T10:00:00Z",
  "updatedAt": "2026-03-25T10:01:30Z"
}
```

#### 4.4.4 CLI 工具列表响应
```json
GET /api/ai/cli-tools
{
  "available": [
    {
      "name": "claude",
      "displayName": "Claude Code",
      "installed": true,
      "version": "1.0.0",
      "authenticated": true
    },
    {
      "name": "qwen-code",
      "displayName": "Qwen Code",
      "installed": false,
      "version": null,
      "authenticated": false
    }
  ],
  "default": "claude"
}
```

---

## 5. 数据模型设计

### 5.1 核心模型

```csharp
// Models/AI/AIChatRequest.cs
namespace NetYamlForge.Models.AI;

/// <summary>
/// AI 聊天请求
/// </summary>
public class AIChatRequest
{
    /// <summary>
    /// 用户输入的指令
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// 选择的 CLI 工具 (claude, qwen-code, etc.)
    /// </summary>
    public string CliTool { get; set; } = "claude";
    
    /// <summary>
    /// 目标项目名
    /// </summary>
    public string? Project { get; set; }
    
    /// <summary>
    /// 会话 ID（用于多轮对话，对应 CLI 的 --resume）
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// 是否启用流式输出 (--output-format stream-json)
    /// </summary>
    public bool Streaming { get; set; } = true;
    
    /// <summary>
    /// 允许的工具列表 (--allowedTools)
    /// </summary>
    public List<string>? AllowedTools { get; set; }
}
```

```csharp
// Models/AI/AIChatResponse.cs
namespace NetYamlForge.Models.AI;

/// <summary>
/// AI 聊天响应
/// </summary>
public class AIChatResponse
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;
    
    /// <summary>
    /// 初始响应消息
    /// </summary>
    public string? Message { get; set; }
    
    /// <summary>
    /// 任务状态
    /// </summary>
    public TaskStatus Status { get; set; }
    
    /// <summary>
    /// 进度百分比 (0-100)
    /// </summary>
    public int Progress { get; set; }
    
    /// <summary>
    /// 会话 ID（用于后续对话）
    /// </summary>
    public string? SessionId { get; set; }
}
```

```csharp
// Models/AI/TaskStatus.cs
namespace NetYamlForge.Models.AI;

/// <summary>
/// 任务状态
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// 等待处理
    /// </summary>
    Pending,
    
    /// <summary>
    /// 正在执行（CLI 进程运行中）
    /// </summary>
    Running,
    
    /// <summary>
    /// 已完成
    /// </summary>
    Completed,
    
    /// <summary>
    /// 失败
    /// </summary>
    Failed,
    
    /// <summary>
    /// 已取消（CLI 进程已终止）
    /// </summary>
    Cancelled
}
```

```csharp
// Models/AI/AITask.cs
namespace NetYamlForge.Models.AI;

/// <summary>
/// AI 任务实体
/// </summary>
public class AITask
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string CliTool { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Project { get; set; }
    public string? SessionId { get; set; }  // CLI session ID
    public TaskStatus Status { get; set; }
    public int Progress { get; set; }
    public string? Result { get; set; }     // CLI 返回的完整结果
    public string? Error { get; set; }
    public List<string> Logs { get; set; } = new();
    public int? ProcessId { get; set; }     // CLI 进程 ID（用于取消）
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

```csharp
// Models/AI/ProgressUpdate.cs
namespace NetYamlForge.Models.AI;

/// <summary>
/// 进度更新（流式）
/// </summary>
public class ProgressUpdate
{
    public string TaskId { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? Message { get; set; }
    public List<string> Logs { get; set; } = new();
    public TaskStatus Status { get; set; }
    
    /// <summary>
    /// Stream-JSON 模式的原始数据
    /// </summary>
    public JsonElement? StreamData { get; set; }
}
```

```csharp
// Models/AI/CliToolInfo.cs
namespace NetYamlForge.Models.AI;

/// <summary>
/// CLI 工具信息
/// </summary>
public class CliToolInfo
{
    /// <summary>
    /// 工具名称（命令）
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// 是否已安装
    /// </summary>
    public bool Installed { get; set; }
    
    /// <summary>
    /// 版本号
    /// </summary>
    public string? Version { get; set; }
    
    /// <summary>
    /// 是否已认证（OAuth）
    /// </summary>
    public bool Authenticated { get; set; }
    
    /// <summary>
    /// 支持的功能
    /// </summary>
    public List<string> Capabilities { get; set; } = new();
}
```

### 5.2 配置模型

```csharp
// Services/AI/CliConfig.cs
namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 配置
/// </summary>
public class CliConfig
{
    public const string SectionName = "AICli";
    
    /// <summary>
    /// 默认 CLI 工具
    /// </summary>
    public string DefaultTool { get; set; } = "claude";
    
    /// <summary>
    /// 任务超时时间（秒）
    /// </summary>
    public int TaskTimeoutSeconds { get; set; } = 600;  // 10 分钟
    
    /// <summary>
    /// 最大并发任务数
    /// </summary>
    public int MaxConcurrentTasks { get; set; } = 2;
    
    /// <summary>
    /// CLI 命令路径（可选，默认从 PATH 查找）
    /// </summary>
    public string? ClaudePath { get; set; }
    public string? QwenCodePath { get; set; }
    
    /// <summary>
    /// 默认工作目录（可选，默认使用项目目录）
    /// </summary>
    public string? DefaultWorkingDirectory { get; set; }
    
    /// <summary>
    /// 工具权限控制
    /// </summary>
    public List<string> DefaultAllowedTools { get; set; } = new()
    {
        "Read", "Write", "Bash", "Edit", "Git"
    };
}
```

---

## 6. 文件结构规划

```
NetYamlForge/
├── Controllers/
│   └── AIController.cs                    # 新增：AI 相关 HTTP API
├── Hubs/
│   └── AIProgressHub.cs                   # 新增：SignalR Hub for 实时推送
├── Models/AI/
│   ├── AIChatRequest.cs                   # 新增
│   ├── AIChatResponse.cs                  # 新增
│   ├── AITask.cs                          # 新增
│   ├── TaskStatus.cs                      # 新增
│   ├── ProgressUpdate.cs                  # 新增
│   └── CliToolInfo.cs                     # 新增
├── Services/AI/
│   ├── ICLIService.cs                     # 新增：CLI 服务接口
│   ├── CLIServiceFactory.cs               # 新增：工厂模式创建 CLI 服务
│   ├── TaskQueueService.cs                # 新增：任务队列管理
│   ├── ProgressTracker.cs                 # 新增：进度追踪器
│   ├── ProcessExecutor.cs                 # 新增：进程调用管理
│   ├── CliConfig.cs                       # 新增：配置模型
│   └── Providers/
│       ├── BaseCLIService.cs              # 新增：CLI 服务基类
│       ├── ClaudeCLIService.cs            # 新增：Claude Code CLI 实现
│       ├── QwenCodeCLIService.cs          # 新增：Qwen Code CLI 实现
│       └── MockCLIService.cs              # 新增：模拟 CLI（测试用）
├── wwwroot/js/
│   └── ai-assistant.js                    # 新增：前端 AI 面板逻辑
├── wwwroot/css/
│   └── ai-assistant.css                   # 新增：AI 面板样式
├── Views/Shared/
│   └── Components/
│       └── AIAssistantPanel.cshtml        # 新增：AI 面板 Partial View
└── AI-ASSISTANT-DESIGN.md                 # 本文档
```

**说明：**
- 不需要 `config/ai-settings.json`（OAuth 认证由 CLI 自行管理）
- 不需要 API Key 相关配置

---

## 7. 服务层详细设计

### 7.1 ICLIService 接口

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 服务接口
/// </summary>
public interface ICLIService
{
    /// <summary>
    /// CLI 工具名称
    /// </summary>
    string ToolName { get; }
    
    /// <summary>
    /// 检查 CLI 是否已安装
    /// </summary>
    Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default);
    
    /// <summary>
    /// 执行命令（流式）
    /// </summary>
    IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message, 
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// 执行命令（一次性）
    /// </summary>
    Task<string> ExecuteAsync(
        string message, 
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// 取消任务（终止进程）
    /// </summary>
    Task CancelAsync(int processId, CancellationToken ct = default);
}
```

### 7.2 CLIServiceFactory

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 服务工厂
/// </summary>
public class CLIServiceFactory
{
    private readonly Dictionary<string, ICLIService> _services;
    
    public CLIServiceFactory(IEnumerable<ICLIService> services)
    {
        _services = services.ToDictionary(s => s.ToolName, StringComparer.OrdinalIgnoreCase);
    }
    
    public ICLIService GetService(string toolName)
    {
        if (!_services.TryGetValue(toolName, out var service))
        {
            throw new InvalidOperationException($"Unknown CLI tool: {toolName}");
        }
        return service;
    }
    
    public async Task<Dictionary<string, CliToolInfo>> GetAvailableToolsAsync()
    {
        var tools = new Dictionary<string, CliToolInfo>();
        foreach (var service in _services.Values)
        {
            var info = await service.GetToolInfoAsync();
            tools[service.ToolName] = info;
        }
        return tools;
    }
}
```

### 7.3 ProcessExecutor

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// 进程执行器（CLI 调用核心）
/// </summary>
public class ProcessExecutor
{
    private readonly ILogger<ProcessExecutor> _logger;
    
    public ProcessExecutor(ILogger<ProcessExecutor> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// 执行 CLI 命令（流式输出）
    /// </summary>
    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        string command,
        string arguments,
        string? workingDirectory = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
            }
        };
        
        _logger.LogInformation("Starting CLI: {Command} {Arguments}", command, arguments);
        
        process.Start();
        _logger.LogInformation("CLI started with PID: {Pid}", process.Id);
        
        // 同时读取标准输出和错误输出
        var outputTask = ReadStreamAsync(process.StandardOutput, ct);
        var errorTask = ReadStreamAsync(process.StandardError, ct);
        
        await foreach (var line in outputTask.WithCancellation(ct))
        {
            yield return line;
        }
        
        await foreach (var line in errorTask.WithCancellation(ct))
        {
            // 错误输出也返回（可能包含有用信息）
            yield return line;
        }
        
        await process.WaitForExitAsync(ct);
        _logger.LogInformation("CLI exited with code: {ExitCode}", process.ExitCode);
    }
    
    /// <summary>
    /// 执行 CLI 命令（一次性）
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> ExecuteAsync(
        string command,
        string arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
            }
        };
        
        process.Start();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        
        await process.WaitForExitAsync(ct);
        
        return (process.ExitCode, output, error);
    }
    
    private static async IAsyncEnumerable<string> ReadStreamAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line != null)
            {
                yield return line;
            }
        }
    }
}
```

### 7.4 ClaudeCLIService 实现

```csharp
namespace NetYamlForge.Services.AI.Providers;

/// <summary>
/// Claude Code CLI 服务
/// </summary>
public class ClaudeCLIService : BaseCLIService
{
    public ClaudeCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        ILogger<ClaudeCLIService> logger)
        : base(executor, config, logger, "claude")
    {
    }
    
    public override async Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default)
    {
        var info = new CliToolInfo
        {
            Name = ToolName,
            DisplayName = "Claude Code",
            Capabilities = new() { "Read", "Write", "Edit", "Bash", "Git", "Web" }
        };
        
        // 检查是否安装
        var result = await Executor.ExecuteAsync(ToolName, "--version");
        if (result.ExitCode == 0)
        {
            info.Installed = true;
            info.Version = result.Output.Trim();
            
            // 检查是否已认证（通过检查配置文件或尝试简单命令）
            var authResult = await Executor.ExecuteAsync(ToolName, "-p \"Hello\" --output-format json");
            info.Authenticated = authResult.ExitCode == 0 && !authResult.Error.Contains("auth", StringComparison.OrdinalIgnoreCase);
        }
        
        return info;
    }
    
    protected override string BuildArguments(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools)
    {
        var args = new List<string>();
        
        // -p 标志：非交互模式
        args.Add("-p");
        args.Add($"\"{message}\"");
        
        // 输出格式
        args.Add(streaming ? "--output-format stream-json" : "--output-format json");
        
        // 会话恢复
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Add("--resume");
            args.Add($"\"{sessionId}\"");
        }
        
        // 工具权限控制
        if (allowedTools != null && allowedTools.Count > 0)
        {
            args.Add("--allowedTools");
            args.Add(string.Join(",", allowedTools));
        }
        
        return string.Join(" ", args);
    }
}
```

### 7.5 BaseCLIService 基类

```csharp
namespace NetYamlForge.Services.AI;

/// <summary>
/// CLI 服务基类
/// </summary>
public abstract class BaseCLIService : ICLIService
{
    protected readonly ProcessExecutor Executor;
    protected readonly CliConfig Config;
    protected readonly ILogger Logger;
    protected readonly string ToolName;
    
    protected BaseCLIService(
        ProcessExecutor executor,
        IOptions<CliConfig> config,
        ILogger logger,
        string toolName)
    {
        Executor = executor;
        Config = config.Value;
        Logger = logger;
        ToolName = toolName;
    }
    
    public string ToolName => this.ToolName;
    
    public abstract Task<CliToolInfo> GetToolInfoAsync(CancellationToken ct = default);
    
    public async IAsyncEnumerable<ProgressUpdate> ExecuteStreamingAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var args = BuildArguments(message, true, sessionId, allowedTools);
        var workingDir = workingDirectory ?? Config.DefaultWorkingDirectory;
        
        await foreach (var line in Executor.ExecuteStreamingAsync(ToolName, args, workingDir, ct))
        {
            // 解析 stream-json 输出
            var update = ParseStreamLine(line);
            if (update != null)
            {
                yield return update;
            }
        }
    }
    
    public async Task<string> ExecuteAsync(
        string message,
        string? workingDirectory = null,
        string? sessionId = null,
        List<string>? allowedTools = null,
        CancellationToken ct = default)
    {
        var args = BuildArguments(message, false, sessionId, allowedTools);
        var workingDir = workingDirectory ?? Config.DefaultWorkingDirectory;
        
        var result = await Executor.ExecuteAsync(ToolName, args, workingDir, ct);
        
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"CLI failed: {result.Error}");
        }
        
        return result.Output;
    }
    
    public async Task CancelAsync(int processId, CancellationToken ct = default)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct);
                Logger.LogInformation("CLI process {Pid} killed", processId);
            }
        }
        catch (ArgumentException)
        {
            // 进程已不存在
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to kill CLI process {Pid}", processId);
        }
    }
    
    /// <summary>
    /// 构建 CLI 参数（由子类实现）
    /// </summary>
    protected abstract string BuildArguments(
        string message,
        bool streaming,
        string? sessionId,
        List<string>? allowedTools);
    
    /// <summary>
    /// 解析流式输出行
    /// </summary>
    protected virtual ProgressUpdate? ParseStreamLine(string line)
    {
        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            
            // 根据 type 字段解析不同类型的消息
            var type = root.GetProperty("type").GetString();
            
            return type switch
            {
                "result" => new ProgressUpdate
                {
                    Message = root.GetProperty("result").GetString(),
                    Progress = 100,
                    Status = TaskStatus.Completed
                },
                "progress" => new ProgressUpdate
                {
                    Message = root.GetProperty("message").GetString(),
                    Progress = root.GetProperty("percentage").GetInt32(),
                    Status = TaskStatus.Running
                },
                "error" => new ProgressUpdate
                {
                    Message = root.GetProperty("error").GetString(),
                    Status = TaskStatus.Failed
                },
                _ => new ProgressUpdate
                {
                    Logs = new() { line },
                    Status = TaskStatus.Running
                }
            };
        }
        catch (JsonException)
        {
            // 非 JSON 输出，作为日志返回
            return new ProgressUpdate
            {
                Logs = new() { line },
                Status = TaskStatus.Running
            };
        }
    }
}
```

---

## 8. 关键技术选型

| 功能 | 技术选型 | 理由 |
|------|----------|------|
| **CLI 调用** | System.Diagnostics.Process | .NET 内置，无需额外依赖 |
| **实时通信** | SignalR | ASP.NET Core 原生支持，自动处理重连 |
| **任务队列** | Channel<T> | .NET 内置异步队列，高性能 |
| **JSON 处理** | System.Text.Json | .NET 内置，高性能 |
| **前端框架** | 原生 JS + HTMX | 与现有项目风格一致 |
| **UI 组件库** | DaisyUI | 复用现有样式系统 |
| **进程管理** | 自定义 ProcessExecutor | 灵活控制进程生命周期 |

---

## 9. 安全设计

### 9.1 认证机制

```
┌─────────────────────────────────────────┐
│  CLI OAuth 认证流程                      │
├─────────────────────────────────────────┤
│  1. 用户首次运行 CLI 时，CLI 提示认证      │
│     claude login                         │
├─────────────────────────────────────────┤
│  2. CLI 打开浏览器进行 OAuth 流程         │
├─────────────────────────────────────────┤
│  3. Token 存储在用户目录                 │
│     ~/.config/claude-code/              │
├─────────────────────────────────────────┤
│  4. 后续调用自动使用存储 的 Token         │
├─────────────────────────────────────────┤
│  5. 应用层无需处理认证逻辑               │
└─────────────────────────────────────────┘
```

**应用层职责：**
- 检查 CLI 是否已安装
- 检查 CLI 是否已认证（通过测试命令）
- 提示用户进行认证（如果未认证）

### 9.2 权限控制

| 角色 | 权限 |
|------|------|
| Admin | 完整 AI 功能，包括 CLI 配置 |
| User | 使用 AI 功能，无法修改配置 |
| Guest | 无权限 |

### 9.3 工具权限控制

```csharp
// 通过 --allowedTools 参数控制 CLI 工具权限
public enum CliToolPermission
{
    Read,       // 读取文件
    Write,      // 创建/修改文件
    Edit,       // 编辑文件
    Bash,       // 执行 shell 命令
    Git,        // Git 操作
    Web         // 网络访问
}

// 配置示例
{
  "AICli": {
    "DefaultAllowedTools": ["Read", "Write", "Edit", "Git"],
    "ForbiddenTools": ["Bash"]  // 禁止执行任意 shell 命令
  }
}
```

### 9.4 文件访问安全

```csharp
// 允许访问的目录白名单
public static class AIFileAccessPolicy
{
    public static readonly string[] AllowedDirectories = new[]
    {
        "projects/{project}/entities",
        "projects/{project}/pages",
        "projects/{project}/config"
    };
    
    public static readonly string[] ForbiddenFiles = new[]
    {
        "appsettings.json",
        "appsettings.Development.json",
        "*.db",
        "*.sqlite",
        ".env*"
    };
    
    // CLI 工作目录限制
    public static string GetSafeWorkingDirectory(string projectPath)
    {
        var root = Path.GetFullPath(projectPath);
        // 确保在项目目录内
        if (!root.StartsWith(AppContext.BaseDirectory))
        {
            throw new SecurityException("Invalid project path");
        }
        return root;
    }
}
```

### 9.5 进程安全

```csharp
// 进程执行限制
public class ProcessSecurity
{
    // 最大执行时间
    public static readonly TimeSpan MaxExecutionTime = TimeSpan.FromMinutes(10);
    
    // 禁止的参数模式
    public static readonly string[] ForbiddenArgs = new[]
    {
        "sudo",
        "rm -rf /",
        "chmod 777",
        // ... 其他危险命令
    };
    
    public static bool IsSafeArgument(string arg)
    {
        return !ForbiddenArgs.Any(f => arg.Contains(f, StringComparison.OrdinalIgnoreCase));
    }
}
```

---

## 10. 扩展性设计

### 10.1 CLI 工具扩展

```
┌─────────────────────────────────────┐
│         ICLIService 接口             │
└─────────────────────────────────────┘
              │
    ┌─────────┼─────────┬──────────┬──────────┐
    ▼         ▼         ▼          ▼          ▼
┌────────┐ ┌──────────┐ ┌──────┐ ┌──────────┐ ┌────────┐
│Claude  │ │ QwenCode │ │Gemini│ │ 自定义 CLI│ │ Ollama │
│Code    │ │   CLI    │ │ CLI  │ │          │ │  CLI   │
└────────┘ └──────────┘ └──────┘ └──────────┘ └────────┘
```

### 10.2 任务类型扩展

```csharp
public enum AITaskType
{
    Chat,               // 普通对话
    GenerateEntity,     // 生成实体定义
    GeneratePage,       // 生成页面
    GenerateCode,       // 生成 C# 代码
    AnalyzeProject,     // 分析项目结构
    GenerateDocument    // 生成文档
}
```

### 10.3 预定义指令模板

```csharp
public static class AIPromptTemplates
{
    public static readonly string GenerateEntity = @"
请为以下实体创建 YAML 定义：
- 实体名：{entityName}
- 字段：{fields}
- 项目目录：{projectPath}

请生成 entities/{entityName}.yml 文件内容。
";

    public static readonly string GeneratePage = @"
请为以下实体创建列表页面：
- 实体名：{entityName}
- 显示字段：{displayFields}
- 操作：{actions}

请生成 pages/{entityName}-list.yaml 文件内容。
";
}
```

---

## 11. 与现有功能集成场景

### 11.1 实体生成

```
用户指令："创建一个产品管理模块，包含名称、价格、库存字段"

AI 执行步骤：
1. 解析意图 → 生成实体定义
2. 读取项目结构 → 确定目标目录
3. 生成 entities/product.yml
4. 生成 pages/product-list.yaml
5. 生成 pages/product-edit.yaml
6. 返回结果并提示用户刷新页面
```

### 11.2 页面生成

```
用户指令："为订单实体创建一个带图表的仪表盘页面"

AI 执行步骤：
1. 查询订单实体定义
2. 分析可用字段
3. 生成 dashboard-order.yaml
4. 包含统计卡片和趋势图表
5. 返回预览链接
```

### 11.3 代码生成

```
用户指令："创建一个 Hook，在用户创建后发送欢迎邮件"

AI 执行步骤：
1. 定位 User 实体
2. 生成 AfterCreate Hook 模板
3. 包含邮件发送逻辑
4. 提示用户配置 SMTP 参数
```

---

## 12. 开发计划

### Phase 1: 基础框架（预计 2-3 天）

- [ ] 创建 Models/AI 目录及数据模型
- [ ] 创建 Services/AI 目录及接口
- [ ] 创建 AIController 基础端点
- [ ] 实现 MockCLIService（测试用）
- [ ] 创建前端 AI 面板 UI 框架
- [ ] 实现 ProcessExecutor 基础功能

### Phase 2: 实时通信（预计 1-2 天）

- [ ] 创建 AIProgressHub (SignalR)
- [ ] 实现 ProgressTracker
- [ ] 前端 WebSocket 连接逻辑
- [ ] 进度条和日志显示组件
- [ ] Stream-JSON 解析逻辑

### Phase 3: CLI 集成（预计 2-3 天）

- [ ] 实现 ClaudeCLIService
- [ ] 实现 QwenCodeCLIService
- [ ] CLI 工具检测逻辑
- [ ] CLI 认证状态检查
- [ ] CLI 选择器组件

### Phase 4: 任务系统（预计 2 天）

- [ ] 实现 TaskQueueService
- [ ] 任务取消功能（终止 CLI 进程）
- [ ] 任务历史记录
- [ ] 错误处理和重试
- [ ] 进程超时控制

### Phase 5: 功能增强（预计 3-5 天）

- [ ] AI 生成实体 YAML
- [ ] AI 生成页面 YAML
- [ ] 与项目文件系统集成
- [ ] 多轮对话支持（--resume）
- [ ] 测试和优化

---

## 13. 测试策略

### 13.1 单元测试

```csharp
public class CLIServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnOutput()
    {
        // Arrange
        var service = new MockCLIService();
        var message = "Hello";

        // Act
        var output = await service.ExecuteAsync(message);

        // Assert
        Assert.NotNull(output);
    }
    
    [Fact]
    public async Task GetToolInfoAsync_ShouldReturnInstalledStatus()
    {
        // Arrange
        var executor = new ProcessExecutor(logger);
        var service = new ClaudeCLIService(executor, config, logger);
        
        // Act
        var info = await service.GetToolInfoAsync();
        
        // Assert
        Assert.True(info.Installed);
    }
}
```

### 13.2 集成测试

```csharp
public class AIControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PostChat_ShouldCreateTask()
    {
        // 测试完整的 HTTP 请求流程
    }
    
    [Fact]
    public async Task GetCliTools_ShouldReturnAvailableTools()
    {
        // 测试 CLI 工具检测
    }
}
```

### 13.3 CLI 集成测试

```csharp
public class ClaudeCLIServiceIntegrationTests
{
    [Fact(Skip = "需要安装 Claude Code CLI")]
    public async Task ExecuteStreamingAsync_ShouldStreamOutput()
    {
        // Arrange
        var service = new ClaudeCLIService(...);
        
        // Act
        await foreach (var update in service.ExecuteStreamingAsync("分析此项目"))
        {
            // Assert: 验证流式输出
            Assert.NotNull(update);
        }
    }
}
```

### 13.4 前端测试

- 手动测试 UI 交互
- 测试 SignalR 重连机制
- 测试不同 CLI 工具切换
- 测试任务取消功能

---

## 14. 配置示例

### 14.1 appsettings.json

```json
{
  "AICli": {
    "DefaultTool": "claude",
    "TaskTimeoutSeconds": 600,
    "MaxConcurrentTasks": 2,
    "DefaultAllowedTools": ["Read", "Write", "Edit", "Git"],
    "DefaultWorkingDirectory": null
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chinook.db"
  }
}
```

### 14.2 环境变量（推荐用于生产）

```bash
# CLI 工具路径（可选，默认从 PATH 查找）
export AI_CLAUDE_PATH="/usr/local/bin/claude"
export AI_QWEN_CODE_PATH="/usr/local/bin/qwen-code"

# 默认配置
export AI_DEFAULT_TOOL="claude"
export AI_TASK_TIMEOUT_SECONDS=600
export AI_MAX_CONCURRENT_TASKS=2

# 工具权限
export AI_DEFAULT_ALLOWED_TOOLS="Read,Write,Edit,Git"
```

### 14.3 系统要求

```bash
# 安装 Claude Code CLI
npm install -g @anthropic-ai/claude-code

# 认证（首次使用）
claude login

# 验证安装
claude --version

# 安装 Qwen Code CLI（如使用）
# 参考：https://help.aliyun.com/zh/dashscope/
```

---

## 15. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| CLI 未安装 | 高 | 启动时检测，提供安装指引 |
| CLI 未认证 | 高 | 认证状态检查，引导用户认证 |
| 进程超时 | 中 | 设置合理超时，支持任务取消 |
| 生成错误内容 | 中 | AI 生成内容需用户确认后才应用 |
| 并发进程过多 | 中 | 队列限流，最大并发数控制 |
| 文件访问越权 | 高 | 白名单机制，路径校验 |
| 危险命令执行 | 高 | --allowedTools 限制，参数过滤 |
| CLI 版本不兼容 | 中 | 版本检测，最小版本要求 |

---

## 16. 未来扩展方向

1. **本地 AI 模型支持**：集成 Ollama CLI，支持离线使用
2. **语音交互**：语音输入指令
3. **多 AI 协作**：复杂任务分发给不同 CLI
4. **AI 学习**：记录用户偏好，优化提示词
5. **插件市场**：第三方 CLI 工具扩展
6. **批处理模式**：预定义任务批量执行
7. **结果预览**：AI 生成内容的 diff 预览
8. **版本控制集成**：自动创建 Git 分支和提交

---

## 17. 参考文档

- [SignalR 官方文档](https://docs.microsoft.com/aspnet/core/signalr/)
- [Claude Code CLI 文档](https://code.claude.com/docs/en/headless)
- [Qwen Code CLI 文档](https://help.aliyun.com/zh/dashscope/)
- [System.Diagnostics.Process](https://learn.microsoft.com/dotnet/api/system.diagnostics.process)
- [DaisyUI 组件库](https://daisyui.com/)
- [HTMX 官方文档](https://htmx.org/)

---

**文档版本**: 1.1  
**创建日期**: 2026-03-25  
**最后更新**: 2026-03-25

**主要变更**:
- 从 HTTP API 改为 CLI 调用方式
- 认证方式从 API Key 改为 OAuth（CLI 自行管理）
- 更新数据模型和服务接口
- 添加 ProcessExecutor 和 CLI 服务实现
- 更新安全设计和配置示例
